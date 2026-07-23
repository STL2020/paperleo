namespace PaperlessAiCore.Core;

/// <summary>
/// Verarbeitet Dokumente in zwei getrennten Phasen (siehe ExtractOnlyAsync/ApplyAsync).
///
/// PERFORMANCE: ApplyAsync lädt Tags/Korrespondenten/Dokumenttypen JEWEILS NUR EINMAL
/// pro Aufruf und arbeitet danach rein gegen diese lokalen Listen (Batch-Muster).
/// Vorherige Version rief pro Tag erneut GetOrCreateTagAsync auf, was INTERN jedes
/// Mal die komplette Tag-Liste neu von Paperless holte (N+1-Problem) - bei 3-5 Tags
/// pro Dokument macht das den Unterschied zwischen 2 und 6+ Netzwerk-Roundtrips,
/// was bei trägen Paperless-Instanzen leicht in Timeouts läuft.
/// </summary>
public static class DocumentProcessor
{
    public record Result(ExtractedMetadata Metadata, int DocumentId);

    public static async Task<ExtractedMetadata> ExtractOnlyAsync(
        PaperlessClient paperless,
        LlmClient llm,
        PaperlessDocument doc,
        ProcessingOptions options,
        CancellationToken ct = default)
    {
        var ocrText = doc.Content;
        if (string.IsNullOrWhiteSpace(ocrText))
        {
            var full = await paperless.GetDocumentAsync(doc.Id, ct);
            ocrText = full.Content;
        }
        if (string.IsNullOrWhiteSpace(ocrText))
        {
            throw new MetadataExtractionException($"Dokument #{doc.Id} hat keinen OCR-Text.");
        }

        List<PaperlessTag>? allTags = options.EnableTagsAssignment ? await paperless.ListTagsAsync(ct) : null;
        List<PaperlessDocumentType>? allDocTypes = options.EnableDocumentTypeClassification ? await paperless.ListDocumentTypesAsync(ct) : null;

        // Alle aktiven System-User laden – die KI nutzt sie für assigned_user
        List<PaperlessUser> systemUsers;
        try { systemUsers = await paperless.ListUsersAsync(ct); }
        catch { systemUsers = new(); }

        return await ExtractionService.ExtractAsync(
            llm, ocrText, options,
            allTags?.Select(t => t.Name).ToList(),
            allDocTypes?.Select(t => t.Name).ToList(),
            systemUsers.Select(u => u.DisplayName).ToList(),
            ct);
    }

    public static async Task<Result> ApplyAsync(
        PaperlessClient paperless,
        PaperlessDocument doc,
        string processedTagName,
        ProcessingOptions options,
        ExtractedMetadata metadata,
        CancellationToken ct = default)
    {
        // Listen IMMER genau einmal laden
        var allTags = options.EnableTagsAssignment ? await paperless.ListTagsAsync(ct) : new List<PaperlessTag>();
        var allDocTypes = options.EnableDocumentTypeClassification ? await paperless.ListDocumentTypesAsync(ct) : new List<PaperlessDocumentType>();
        var allCorrespondents = options.EnableCorrespondentDetection ? await paperless.ListCorrespondentsAsync(ct) : new List<PaperlessCorrespondent>();

        // System-User laden für AssignedUser-Zuweisung
        List<PaperlessUser> allUsers;
        try { allUsers = await paperless.ListUsersAsync(ct); }
        catch { allUsers = new(); }

        var patch = new Dictionary<string, object?>();

        if (options.EnableTitleGeneration && !string.IsNullOrWhiteSpace(metadata.Title))
        {
            patch["title"] = TruncateTitle(metadata.Title);
        }

        // DocumentDate bevorzugen (neues Feld), fallback auf Date
        var validDate = NormalizeDate(metadata.DocumentDate ?? metadata.Date);
        if (validDate is not null)
        {
            patch["created"] = validDate;
        }

        if (options.EnableCorrespondentDetection && !string.IsNullOrWhiteSpace(metadata.Correspondent))
        {
            var correspondentId = await ResolveOrCreateAsync(
                allCorrespondents, c => c.Id, c => c.Name,
                metadata.Correspondent, options.UseExistingCorrespondentsOnly,
                name => paperless.CreateCorrespondentAsync(name, ct));

            if (correspondentId is not null)
            {
                patch["correspondent"] = correspondentId;
            }
        }

        // AssignedUser: KI-Vorschlag auf echten Paperless-Nutzer mappen
        if (!string.IsNullOrWhiteSpace(metadata.AssignedUser) && allUsers.Count > 0)
        {
            var matchedUser = allUsers.FirstOrDefault(u =>
                string.Equals(u.DisplayName, metadata.AssignedUser, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(u.Username, metadata.AssignedUser, StringComparison.OrdinalIgnoreCase) ||
                u.DisplayName.Contains(metadata.AssignedUser, StringComparison.OrdinalIgnoreCase));
            if (matchedUser is not null)
            {
                patch["owner"] = matchedUser.Id;
            }
        }

        // Bookkeeping-Tag ("ai-processed" o.ä.) wird immer angelegt/gesetzt,
        // unabhängig von EnableTagsAssignment - reine Prozess-Markierung. Wird
        // ebenfalls gegen die (falls schon geladene) Tag-Liste gematcht statt
        // separat erneut zu listen.
        var processedTags = options.EnableTagsAssignment ? allTags : await paperless.ListTagsAsync(ct);
        var processedTagId = await ResolveOrCreateAsync(
            processedTags, t => t.Id, t => t.Name,
            processedTagName, useExistingOnly: false,
            name => paperless.CreateTagAsync(name, ct))
            ?? throw new InvalidOperationException("Bookkeeping-Tag konnte nicht angelegt werden.");

        // Tags werden ERSETZT, nicht mit bestehenden Tags des Dokuments angehäuft -
        // sonst sammelt sich über mehrere Testläufe/Re-Analysen hinweg immer mehr
        // Tag-Müll an. Einzige Ausnahme: der Bookkeeping-Tag bleibt immer gesetzt.
        var allTagIds = new List<int> { processedTagId };

        if (options.EnableTagsAssignment)
        {
            // Harte Obergrenze, falls die KI sich nicht an "3-5 Tags" aus dem Prompt hält.
            const int maxContentTags = 5;
            foreach (var tagName in metadata.Tags.Take(maxContentTags))
            {
                var tagId = await ResolveOrCreateAsync(
                    allTags, t => t.Id, t => t.Name,
                    tagName, options.UseExistingTagsOnly,
                    name => paperless.CreateTagAsync(name, ct));

                if (tagId is not null)
                {
                    allTagIds.Add(tagId.Value);
                    // Neu angelegten Tag lokal mitführen, falls derselbe Name
                    // mehrfach in metadata.Tags vorkommt (dann kein zweites Anlegen).
                    if (!allTags.Any(t => t.Id == tagId.Value))
                    {
                        allTags.Add(new PaperlessTag { Id = tagId.Value, Name = tagName });
                    }
                }
            }
        }
        patch["tags"] = allTagIds.Distinct().ToList();

        if (options.EnableDocumentTypeClassification && !string.IsNullOrWhiteSpace(metadata.DocumentType))
        {
            var documentTypeId = await ResolveOrCreateAsync(
                allDocTypes, t => t.Id, t => t.Name,
                metadata.DocumentType, options.UseExistingDocumentTypesOnly,
                name => paperless.CreateDocumentTypeAsync(name, ct));

            if (documentTypeId is not null)
            {
                patch["document_type"] = documentTypeId;
            }
        }

        // Custom Fields: strukturierte Werte als ECHTE, durchsuchbare Paperless-Felder
        // (statt nur als Text im Titel versteckt). Experimentell/opt-in (EnableCustomFields).
        // Der Wert wird passend zum TATSÄCHLICHEN Datentyp des Feldes formatiert:
        // - "monetary": ISO-4217-Währungscode + Betrag mit 2 Nachkommastellen (z.B. "EUR45.52")
        // - "date": YYYY-MM-DD
        // - "select": die ID der passenden Auswahl-Option (nicht der Text!) - wird nur
        //   gesetzt, wenn eine Option existiert, deren Label zum extrahierten Wert passt
        // - alles andere ("string" etc.): unverändert als Text
        if (options.EnableCustomFields)
        {
            var customFieldValues = new (string FieldName, string? RawValue)[]
            {
                ("Rechnungsnummer", metadata.InvoiceNumber),
                ("Belegnummer", metadata.ReceiptNumber),
                ("Rechnungsbetrag", metadata.Amount is > 0
                    ? metadata.Amount.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                    : null),
                ("Seriennummer", metadata.SerialNumber),
                ("Fahrzeug", metadata.Vehicle),
                ("Gewerk_Dienstleistung", metadata.TradeService),
                ("Objekt / Adresse", metadata.PropertyAddress),
                ("Familienmitglied", metadata.FamilyMember),
                ("Vertragsende / Laufzeit bis", metadata.ContractEndDate),
                ("Vertragsnummer / Kundennummer", metadata.ContractOrCustomerNumber),
                ("Fälligkeitsdatum", metadata.DueDate),
            };

            var customFieldsPayload = new List<object>();
            foreach (var (fieldName, rawValue) in customFieldValues)
            {
                if (string.IsNullOrWhiteSpace(rawValue)) continue;

                var field = await paperless.FindOrCreateCustomFieldAsync(fieldName, ct);

                if (field.DataType == "select")
                {
                    var option = field.ExtraData?.SelectOptions?
                        .FirstOrDefault(o => string.Equals(o.Label, rawValue, StringComparison.OrdinalIgnoreCase));
                    if (option is not null)
                    {
                        customFieldsPayload.Add(new { field = field.Id, value = option.Id });
                    }
                    // Kein passendes Options-Label gefunden -> Feld einfach überspringen,
                    // statt eine neue Option zu erfinden (das ist über die API riskant/instabil).
                    continue;
                }

                var formattedValue = FormatCustomFieldValue(field.DataType, rawValue);
                if (formattedValue is not null)
                {
                    customFieldsPayload.Add(new { field = field.Id, value = formattedValue });
                }
            }

            // "Steuer" (Wahrheitswert) separat behandelt: braucht einen ECHTEN JSON-Bool
            // im Payload, keinen String "true"/"false" - Paperless würde das sonst ablehnen.
            if (metadata.IsTaxRelevant is not null)
            {
                var taxField = await paperless.FindOrCreateBooleanCustomFieldAsync("Steuer", ct);
                customFieldsPayload.Add(new { field = taxField.Id, value = metadata.IsTaxRelevant.Value });
            }

            // "Mietobjekt" (Wahrheitswert) wird NICHT separat von der KI erfragt, sondern
            // deterministisch aus der bereits ermittelten Objekt-Zuordnung (property_address)
            // abgeleitet - das schließt Widersprüche aus (z.B. Tag sagt "MFH Kripp", aber
            // ein unabhängig erratenes Mietobjekt-Flag sagt "nein"). Nur gesetzt, wenn das
            // Dokument überhaupt eindeutig einem der beiden bekannten Objekte zugeordnet ist -
            // bei unrelated Dokumenten bleibt das Feld unangetastet statt fälschlich "false".
            if (!string.IsNullOrWhiteSpace(metadata.PropertyAddress))
            {
                var isRentalProperty = string.Equals(metadata.PropertyAddress, "MFH Kripp", StringComparison.OrdinalIgnoreCase);
                var rentalField = await paperless.FindOrCreateBooleanCustomFieldAsync("Mietobjekt", ct);
                customFieldsPayload.Add(new { field = rentalField.Id, value = isRentalProperty });
            }

            if (customFieldsPayload.Count > 0)
            {
                patch["custom_fields"] = customFieldsPayload;
            }
        }

        // PATCH-STRATEGIE: Paperless triggert bei JEDEM PATCH einen Datei-Rename-Versuch.
        // Wenn Titel + Korrespondent gleich bleiben schlägt der Rename mit "duplicate key" fehl
        // (Paperless-interner Fehler, kein paperLeo-Fehler). Um das zu minimieren:
        //   - Titel/Datum NUR patchen wenn sie sich vom aktuellen Wert unterscheiden
        //   - Custom Fields IMMER separat (Paperless-Bug erfordert das)

        var customFieldsPayloadForPatch = patch.ContainsKey("custom_fields") ? patch["custom_fields"] : null;
        patch.Remove("custom_fields");

        // Patch 1 – Nur skalare Felder: title, created – NUR wenn geändert
        var patch1 = new Dictionary<string, object?>();
        if (patch.TryGetValue("title", out var newTitle) && newTitle is string newTitleStr)
        {
            var currentTitle = doc.Title ?? "";
            if (!string.Equals(currentTitle, newTitleStr, StringComparison.Ordinal))
            { patch1["title"] = newTitleStr; }
            patch.Remove("title");
        }
        if (patch.TryGetValue("created", out var newCreated))
        {
            var currentCreated = (doc.Created ?? "").Substring(0, Math.Min(10, (doc.Created ?? "").Length));
            var newCreatedStr = (newCreated?.ToString() ?? "").Substring(0, Math.Min(10, (newCreated?.ToString() ?? "").Length));
            if (!string.Equals(currentCreated, newCreatedStr, StringComparison.Ordinal))
            { patch1["created"] = newCreated; }
            patch.Remove("created");
        }

        // Patch 2 – Relationale Felder: tags, correspondent, document_type, owner
        var patch2 = patch;

        if (patch1.Count > 0)
        {
            try { await paperless.UpdateDocumentAsync(doc.Id, patch1, ct); await Task.Delay(150, ct); }
            catch (HttpRequestException ex) when (ex.Message.Contains("500"))
            {
                paperless.OnWriteLog?.Invoke($"WARNUNG Dok #{doc.Id}: Patch1 (Titel/Datum) 500 – übersprungen: {ex.Message[..Math.Min(120, ex.Message.Length)]}");
            }
        }

        if (patch2.Count > 0)
        {
            try
            {
                await paperless.UpdateDocumentAsync(doc.Id, patch2, ct);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("500"))
            {
                // Fallback: nur den Verarbeitungs-Tag setzen damit das Dokument nicht ewig wiederholt wird
                paperless.OnWriteLog?.Invoke($"WARNUNG Dok #{doc.Id}: Patch2 (Tags/Korrespondent) 500 – versuche Fallback mit nur Tags: {ex.Message[..Math.Min(120, ex.Message.Length)]}");
                await Task.Delay(300, ct);
                try
                {
                    // Minimaler Fallback: nur Tags (ohne Korrespondent/Typ die oft den 500 auslösen)
                    if (patch2.TryGetValue("tags", out var tagsOnly))
                        await paperless.UpdateDocumentAsync(doc.Id, new { tags = tagsOnly }, ct);
                }
                catch (HttpRequestException fallbackEx)
                {
                    paperless.OnWriteLog?.Invoke($"WARNUNG Dok #{doc.Id}: Auch Fallback fehlgeschlagen – Dokument wird als verarbeitet markiert um Endlosschleife zu vermeiden: {fallbackEx.Message[..Math.Min(80, fallbackEx.Message.Length)]}");
                }
            }
        }

        // Patch 3 – Custom Fields
        if (customFieldsPayloadForPatch is not null)
        {
            await Task.Delay(300, ct);
            try { await paperless.UpdateDocumentAsync(doc.Id, new { custom_fields = customFieldsPayloadForPatch }, ct); }
            catch (HttpRequestException cfEx)
            {
                paperless.OnWriteLog?.Invoke($"WARNUNG Dok #{doc.Id}: custom_fields 500 – übersprungen: {cfEx.Message[..Math.Min(80, cfEx.Message.Length)]}");
            }
        }

        // Verifikation
        if (patch.TryGetValue("tags", out var expectedTagsObj) && expectedTagsObj is List<int> expectedTags)
        {
            try
            {
                var verifyDoc = await paperless.GetDocumentAsync(doc.Id, ct);
                var missing = expectedTags.Except(verifyDoc.Tags).ToList();
                if (missing.Count > 0)
                    paperless.OnWriteLog?.Invoke($"WARNUNG Dok #{doc.Id}: Tags [{string.Join(",", missing)}] fehlen nach PATCH.");
            }
            catch { /* Verifikationsfehler nicht weiterwerfen */ }
        }

        return new Result(metadata, doc.Id);
    }

    /// <summary>
    /// Paperless-ngx begrenzt das Titel-Feld auf 128 Zeichen - ein zu langer Titel
    /// (z.B. lange Rechnungsnummer + langer Korrespondentenname) würde die PATCH-
    /// Anfrage mit einem 400er scheitern lassen. Sicherheitshalber kürzen statt riskieren.
    /// </summary>
    private static string TruncateTitle(string title) =>
        title.Length > 128 ? title[..124] + "…" : title;

    /// <summary>
    /// Prüft, ob das von der KI gelieferte Datum als YYYY-MM-DD geparst werden kann.
    /// Ist es das nicht (unerwartetes Format, offensichtlicher Unsinn), wird das Feld
    /// komplett weggelassen statt eine potenziell ungültige PATCH-Anfrage zu riskieren -
    /// das vorhandene Erstellungsdatum in Paperless bleibt dann einfach unangetastet.
    /// </summary>
    private static string? NormalizeDate(string? rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate)) return null;

        if (DateTime.TryParse(rawDate, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
        {
            // Plausibilitäts-Check: Datumsangaben weit in der Zukunft oder vor 1900
            // sind praktisch immer ein KI-Halluzinations-/Parsing-Fehler.
            if (parsed.Year is >= 1900 and <= 2100)
            {
                return parsed.ToString("yyyy-MM-dd");
            }
        }

        return null;
    }

    /// <summary>
    /// Formatiert einen rohen Extraktions-Wert passend zum tatsächlichen Paperless-
    /// Datentyp des Custom Fields. "monetary" braucht laut Paperless-Doku einen
    /// ISO-4217-Währungscode direkt vor dem Betrag mit exakt 2 Nachkommastellen
    /// (Punkt, kein Komma) - z.B. "EUR45.52". EUR ist als Standard-Annahme hart
    /// hinterlegt (es gibt aktuell keinen zuverlässigen Weg, die pro Feld hinterlegte
    /// Default-Währung über die API auszulesen) - bei anderen Währungen im Kunden-
    /// Setup müsste das angepasst werden.
    /// </summary>
    private static string? FormatCustomFieldValue(string dataType, string rawValue)
    {
        switch (dataType)
        {
            case "monetary":
                // rawValue kommt bei uns bereits als "0.00"-formatierte Zahl rein (siehe Aufrufer).
                return double.TryParse(rawValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount)
                    ? $"EUR{amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}"
                    : null;
            case "integer":
                return int.TryParse(rawValue, out var i) ? i.ToString() : null;
            case "float":
                return double.TryParse(rawValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var f)
                    ? f.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : null;
            case "date":
                return DateTime.TryParse(rawValue, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d) && d.Year is >= 1900 and <= 2100
                    ? d.ToString("yyyy-MM-dd")
                    : null;
            default:
                // "string", "url", "boolean" u.a. - unverändert als Text übernehmen.
                return rawValue;
        }
    }

    /// <summary>
    /// Sucht "name" case-insensitive in der bereits geladenen lokalen Liste. Nicht
    /// gefunden UND useExistingOnly=false -> legt per createFunc neu an (EIN
    /// zusätzlicher Roundtrip, kein erneutes Listen). Nicht gefunden UND
    /// useExistingOnly=true -> gibt null zurück (Feld bleibt in Paperless unverändert).
    /// </summary>
    private static async Task<int?> ResolveOrCreateAsync<T>(
        List<T> localList, Func<T, int> getId, Func<T, string> getName,
        string name, bool useExistingOnly, Func<string, Task<int>> createFunc)
    {
        var existing = localList.FirstOrDefault(x => string.Equals(getName(x), name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return getId(existing);
        if (useExistingOnly) return null;

        return await createFunc(name);
    }

    /// <summary>
    /// Schreibt ausschließlich Custom Fields für ein bereits verarbeitetes Dokument nach
    /// (Backfill-Funktion). Tastet Titel, Tags, Korrespondent, Dokumenttyp nicht an.
    /// </summary>
    public static async Task ApplyCustomFieldsOnlyAsync(
        PaperlessClient paperless,
        int documentId,
        ExtractedMetadata metadata,
        ProcessingOptions options,
        CancellationToken ct = default)
    {
        var customFieldsPayload = new List<object>();

        // Alle Custom-Field-Kandidaten
        var textFields = new (string FieldName, string? RawValue)[]
        {
            ("Rechnungsnummer", metadata.InvoiceNumber),
            ("Belegnummer", metadata.ReceiptNumber),
            ("Rechnungsbetrag", metadata.Amount is > 0
                ? metadata.Amount.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                : null),
            ("Seriennummer", metadata.SerialNumber),
            ("Fahrzeug", metadata.Vehicle),
            ("Gewerk_Dienstleistung", metadata.TradeService),
            ("Objekt / Adresse", metadata.PropertyAddress),
            ("Familienmitglied", metadata.FamilyMember),
            ("Vertragsende / Laufzeit bis", metadata.ContractEndDate),
            ("Vertragsnummer / Kundennummer", metadata.ContractOrCustomerNumber),
            ("Fälligkeitsdatum", metadata.DueDate),
        };

        foreach (var (fieldName, rawValue) in textFields)
        {
            if (string.IsNullOrWhiteSpace(rawValue)) continue;
            var field = await paperless.FindOrCreateCustomFieldAsync(fieldName, ct);

            if (field.DataType == "select")
            {
                var option = field.ExtraData?.SelectOptions?
                    .FirstOrDefault(o => string.Equals(o.Label, rawValue, StringComparison.OrdinalIgnoreCase));
                if (option is not null)
                    customFieldsPayload.Add(new { field = field.Id, value = option.Id });
                continue;
            }

            var formatted = FormatCustomFieldValue(field.DataType, rawValue);
            if (formatted is not null)
                customFieldsPayload.Add(new { field = field.Id, value = formatted });
        }

        if (metadata.IsTaxRelevant is not null)
        {
            var taxField = await paperless.FindOrCreateBooleanCustomFieldAsync("Steuer", ct);
            customFieldsPayload.Add(new { field = taxField.Id, value = metadata.IsTaxRelevant.Value });
        }

        if (!string.IsNullOrWhiteSpace(metadata.PropertyAddress))
        {
            var isRental = string.Equals(metadata.PropertyAddress, "MFH Kripp", StringComparison.OrdinalIgnoreCase);
            var rentalField = await paperless.FindOrCreateBooleanCustomFieldAsync("Mietobjekt", ct);
            customFieldsPayload.Add(new { field = rentalField.Id, value = isRental });
        }

        if (customFieldsPayload.Count > 0)
        {
            await paperless.UpdateDocumentAsync(documentId, new { custom_fields = customFieldsPayload }, ct);
        }
    }

    /// <summary>Kompletter Durchlauf (Extraktion + Schreiben) für Worker/Webhook - kein manuelles Bestätigen.</summary>
    public static async Task<Result> ProcessAsync(
        PaperlessClient paperless,
        LlmClient llm,
        PaperlessDocument doc,
        string processedTagName,
        ProcessingOptions options,
        CancellationToken ct = default)
    {
        var metadata = await ExtractOnlyAsync(paperless, llm, doc, options, ct);
        return await ApplyAsync(paperless, doc, processedTagName, options, metadata, ct);
    }
}
