using System.Text;
using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Core;

/// <summary>
/// Baut den System-Prompt für die Metadaten-Extraktion. Entweder aus einem
/// vom Nutzer frei definierten Custom-Prompt (Platzhalter [tags] / [document_types]
/// werden durch die live aus Paperless geladenen Listen ersetzt), oder aus dem
/// eingebauten Default, der optional um "nur diese Werte verwenden"-Anweisungen
/// ergänzt wird, wenn UseExistingEntitiesOnly aktiv ist.
/// </summary>
public static class Prompts
{
    private const string DefaultBase = """
        Du bist ein präziser KI-Archivar für ein Dokumentenmanagement-System (Paperless-ngx).
        Dein Fokus liegt auf Daten-Konsolidierung: Du sollst Dokumente so verschlagworten, dass
        KEINE Dubletten durch unterschiedliche Schreibweisen von Firmennamen entstehen.
        Du erhältst den OCR-Rohtext eines Dokuments.

        1. KONSOLIDIERUNG VON KORRESPONDENTEN (höchste Priorität)
           Reduziere Absender zwingend auf ihren allgemeinen Markennamen:
           - Rechtsformen entfernen: GmbH, AG, e.V., S.à r.l., Ltd, Co. KG, Inc. usw.
           - Standorte/Abteilungen/Sparten ignorieren.
           - Einheitlichkeit: "Telekom Deutschland GmbH" / "Telekom Systems" -> "Telekom".
             "Amazon EU S.a.r.l." / "Amazon Payments" -> "Amazon".
           - Typische OCR-Lesefehler korrigieren (z.B. "Am4zon" -> "Amazon").
           - Ist kein Absender zweifelsfrei erkennbar, setze "Unbekannt".

        2. RECHNUNGS-/BELEGNUMMER
           Falls im Dokument eine Rechnungs-, Beleg- oder Vorgangsnummer erkennbar ist,
           extrahiere sie separat (Feld invoice_number). Der Titel wird NICHT von dir
           formatiert, sondern automatisch aus correspondent/document_type/invoice_number/
           amount zusammengesetzt - liefere diese Felder daher möglichst präzise.

        3. THEMATISCHE ZUORDNUNG
           Wähle passende Tags anhand des Inhalts. Bei Versorgern (Strom/Wasser/Gas o.ä.)
           strikt anhand der im Dokument genannten Liefer-/Objektadresse zuordnen, falls im
           Dokument oder in der Tag-Liste ein passendes Adress-/Objekt-Tag existiert.

        Das technische Ausgabeformat (welche Felder wie geliefert werden müssen) steht
        weiter unten und gilt unabhängig von den obigen Punkten.
        """;

    public static string BuildSystemPrompt(ProcessingOptions options, IReadOnlyList<string>? existingTags, IReadOnlyList<string>? existingDocumentTypes, IReadOnlyList<string>? systemUsers = null)
    {
        string[] vocabularyTags;
        string[] vocabularyTypes;

        if (options.IsProMode)
        {
            // PRO: vom Nutzer gepflegtes Vokabular (oder leer, wenn nicht gesetzt)
            vocabularyTags = (options.DefaultTagVocabulary ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            vocabularyTypes = (options.DefaultDocumentTypeVocabulary ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        else
        {
            // Free: fest auf die 5 Standard-Werte beschränkt, nicht editierbar
            vocabularyTags = AppInfo.FreeDefaultTags;
            vocabularyTypes = AppInfo.FreeDefaultDocumentTypes;
        }

        var mergedTags = (existingTags ?? Array.Empty<string>())
            .Concat(vocabularyTags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var tagsList = mergedTags.Count > 0 ? string.Join(", ", mergedTags) : "(keine hinterlegt)";

        var mergedTypes = (existingDocumentTypes ?? Array.Empty<string>())
            .Concat(vocabularyTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var typesList = mergedTypes.Count > 0 ? string.Join(", ", mergedTypes) : "(keine hinterlegt)";

        string basePrompt;
        if (!string.IsNullOrWhiteSpace(options.CustomSystemPrompt))
        {
            // Eigener Prompt: Platzhalter [tags] / [document_types] werden ersetzt,
            // falls der Nutzer sie (wie in gängigen Vorlagen üblich) verwendet hat.
            basePrompt = options.CustomSystemPrompt
                .Replace("[tags]", tagsList)
                .Replace("[document_types]", typesList);
        }
        else
        {
            basePrompt = DefaultBase;
        }

        // "Nur vorhandene Werte verwenden" gilt jetzt PRO KATEGORIE (Korrespondenten/
        // Dokumenttypen/Tags getrennt einstellbar) und wird IMMER angehängt - unabhängig
        // davon, ob ein eigener oder der Standard-Prompt genutzt wird.
        var restrictions = new StringBuilder();
        if (options.UseExistingTagsOnly && options.EnableTagsAssignment)
        {
            restrictions.AppendLine($"Wähle Tags AUSSCHLIESSLICH aus dieser Liste: {tagsList}. Erfinde keine neuen Tags.");
        }
        if (options.UseExistingDocumentTypesOnly && options.EnableDocumentTypeClassification)
        {
            restrictions.AppendLine($"Wähle document_type AUSSCHLIESSLICH aus dieser Liste: {typesList}. Erfinde keinen neuen Typ.");
        }
        if (options.UseExistingCorrespondentsOnly && options.EnableCorrespondentDetection)
        {
            restrictions.AppendLine("Nutze für correspondent nur Namen, die im Dokument eindeutig als bereits bekannter Absender erkennbar sind - erfinde keinen komplett neuen Korrespondenten-Namen, falls unsicher setze \"Unbekannt\".");
        }

        if (restrictions.Length > 0)
        {
            basePrompt += "\n\nWICHTIG - Nur vorhandene Werte verwenden:\n" + restrictions;
        }

        // Wird IMMER angehängt, unabhängig davon, ob ein eigener oder der Standard-Prompt
        // genutzt wird - das technische JSON-Schema, das unser Code (ExtractionService)
        // tatsächlich parst, darf NIE von einem veralteten eigenen Prompt-Text überschrieben
        // oder unvollständig gelassen werden. So kommen neue Felder automatisch bei JEDEM
        // Nutzer an, ganz ohne den eigenen Prompt manuell nachpflegen zu müssen.
        return basePrompt + "\n\n" + BuildMandatorySchema(systemUsers);
    }

    private static string BuildMandatorySchema(IReadOnlyList<string>? users = null)
    {
        var userBlock = users is { Count: > 0 }
            ? "assigned_user: Ordne das Dokument EINEM der folgenden Nutzer zu falls eindeutig erkennbar: "
              + string.Join(", ", users) + ". Sonst null."
            : "assigned_user: null (keine Nutzer im System konfiguriert).";

        return
            "--- VERBINDLICHES AUSGABEFORMAT (überschreibt alle anderen Anweisungen) ---\n" +
            "Antworte AUSSCHLIESSLICH mit exakt diesem JSON-Objekt. Kein Fließtext, kein Markdown, keine Codeblöcke.\n\n" +
            "FELDREGELN:\n" +
            "correspondent: Kurzform des Absenders ohne Rechtsform (GmbH, AG etc.).\n" +
            "document_date: Datum des Dokuments im Format YYYY-MM-DD. Inhaltsdatum, nicht Druckdatum.\n" +
            "amount: Gesamtbetrag als Dezimalzahl oder null. Nicht raten.\n" +
            "tax_amount: Enthaltener MwSt.-/Steuerbetrag als Dezimalzahl oder null. Nur wenn explizit ausgewiesen.\n" +
            "invoice_number: Rechnungs-/Belegnummer als String oder null.\n" +
            userBlock + "\n" +
            "custom_fields: Immer als leeres Objekt {} zurueckgeben.\n" +
            "confidence: Konfidenzwert 0.0-1.0 (1.0=sicher, unter 0.8=unsicher).\n" +
            "tags: Nur aus der vorgegebenen Tag-Liste waehlen.\n" +
            "document_type: Nur aus der vorgegebenen Typ-Liste waehlen.\n\n" +
            JsonSchemaExample;
    }

    private const string MandatorySchemaInstructions = "";  // Ersetzt durch BuildMandatorySchema()

    // JSON-Beispiel als Konstante – so umgeht man die {{ }}-Problematik in $-Strings
    private const string JsonSchemaExample =
        "{\n" +
        "  \"correspondent\": \"string\",\n" +
        "  \"amount\": 0.00,\n" +
        "  \"invoice_number\": \"string oder null\",\n" +
        "  \"document_date\": \"YYYY-MM-DD oder null\",\n" +
        "  \"document_type\": \"string\",\n" +
        "  \"tax_amount\": 0.00,\n" +
        "  \"tags\": [\"string\"],\n" +
        "  \"assigned_user\": \"string oder null\",\n" +
        "  \"custom_fields\": {},\n" +
        "  \"confidence\": 0.0\n" +
        "}";

    public static string BuildUserPrompt(string ocrText)
    {
        var trimmed = ocrText.Trim();
        if (trimmed.Length > 6000)
        {
            trimmed = trimmed[..6000];
        }
        return $"OCR-Text des Dokuments:\n\n{trimmed}";
    }

    /// <summary>
    /// Baut den Such-Agent-Prompt DYNAMISCH mit den Live-Metadaten der jeweiligen
    /// Paperless-Instanz. Enthält explizite Übersetzungsbeispiele für Personen-Namen
    /// und Steuer-Keywords auf Custom Fields, damit die KI nicht rät.
    /// </summary>
    public static string BuildAgentSystemPrompt(DateTime today, PaperlessMetadataSnapshotLike metadata)
    {
        var tagsList = metadata.Tags.Count > 0
            ? string.Join(", ", metadata.Tags.Select(t => t.Name))
            : "(keine Tags hinterlegt)";
        var typesList = metadata.DocumentTypes.Count > 0
            ? string.Join(", ", metadata.DocumentTypes.Select(t => t.Name))
            : "(keine Dokumenttypen hinterlegt)";
        var correspondentsList = metadata.Correspondents.Count > 0
            ? string.Join(", ", metadata.Correspondents.Select(c => c.Name).Take(40))
            : "(keine Korrespondenten hinterlegt)";

        var customFieldsList = metadata.CustomFields.Count > 0
            ? string.Join("\n", metadata.CustomFields.Select(DescribeCustomField))
            : "(keine Custom Fields hinterlegt)";

        // Explizite Personen-Mapping-Hinweise aus dem Select-Feld "Familienmitglied"
        var familyField = metadata.CustomFields.FirstOrDefault(f =>
            f.Name.Contains("Familienmitglied", StringComparison.OrdinalIgnoreCase) ||
            f.Name.Contains("family", StringComparison.OrdinalIgnoreCase));
        var familyHint = familyField?.ExtraData?.SelectOptions is { Count: > 0 } opts
            ? $"\n- Bei Anfragen mit Personennamen: IMMER prüfen ob der Name in \"{familyField.Name}\" vorkommt! Bekannte Personen: {string.Join(", ", opts.Select(o => o.Label))}. Beispiel: \"Stephan\" → custom_fields: [{{name:\"{familyField.Name}\", op:\"exact\", value:\"Stephan\"}}]"
            : "";

        // Boolean-Felder explizit benennen für bessere KI-Nutzung
        var boolFields = metadata.CustomFields.Where(f => f.DataType == "boolean").ToList();
        var boolHint = boolFields.Count > 0
            ? $"\n- Boolean-Filter-Felder: {string.Join(", ", boolFields.Select(f => $"\"{f.Name}\" (true/false)"))}. Beispiel: steuerrelevant/Steuer → custom_fields: [{{name:\"{boolFields.FirstOrDefault()?.Name ?? "Steuer"}\", op:\"exact\", value:\"true\"}}]"
            : "";

        return $$"""
            Du bist ein präziser Such-Agent für ein Dokumentenmanagement-System (Paperless-ngx).
            Deine einzige Informationsquelle sind die verfügbaren Tools.
            Erfinde NIEMALS Dokumente, Beträge oder Inhalte.

            === AKTUELLES DATUM: {{today:yyyy-MM-dd}} (heute) ===
            Nutze dieses Datum, um relative Angaben exakt umzurechnen:
            - "aus 2025" → date_from: "2025-01-01", date_to: "2025-12-31"
            - "letzter Monat" → erster bis letzter Tag des Vormonats
            - "dieses Jahr" → 01.01.{{today.Year}} bis {{today:yyyy-MM-dd}}

            === VERFÜGBARE METADATEN DIESER PAPERLESS-INSTANZ ===
            Tags: {{tagsList}}
            Dokumenttypen: {{typesList}}
            Korrespondenten (Auszug): {{correspondentsList}}
            Custom Fields:
            {{customFieldsList}}

            === ÜBERSETZUNGSREGELN (WICHTIG - genau befolgen) ===
            {{familyHint}}{{boolHint}}

            - "Arztrechnung", "Arzt", "Doktor", "Dr." → correspondent_name mit "Dr." ODER query: "Arzt" ODER document_type: "Rechnung" + query: "Arzt"
            - "steuerrelevant", "Steuer", "für die Steuer" → custom_fields mit dem Boolean-Feld "Steuer" = true
            - "von [Person]", "für [Person]", "[Personenname]" → PRÜFE zuerst ob der Name in den Custom Fields (Familienmitglied o.ä.) vorkommt, dann setze custom_fields-Filter
            - Korrespondenten-Namen: nutze correspondent_name mit dem EXAKTEN Namen aus der Liste oben; bei Teilnamen → query statt correspondent_name
            - Jahreszahlen/Zeiträume: IMMER date_from UND date_to setzen (niemals nur eines)

            === SUCH-STRATEGIE ===
            1. Fragen nach Summen/Kosten → aggregate_costs
            2. Alle anderen Suchanfragen → search_documents
            3. Nutze IMMER den spezifischsten verfügbaren Filter:
               Custom Field > Korrespondent/Dokumenttyp > Tag > Volltext (query)
            4. Bei Personennamen: ZUERST Custom Field prüfen, DANN Tag, DANN query
            5. query leer lassen NUR wenn custom_fields/correspondent/document_type bereits eindeutig filtern
            6. Setze query auf den inhaltlich stärksten Begriff der Anfrage (Thema/Absender/Kategorie)

            === ANTWORT-REGELN ===
            - Antworte in der Sprache des Nutzers (Deutsch wenn Frage auf Deutsch)
            - Nenne Anzahl der Treffer
            - Liste max. 5 Treffer mit Titel, Datum, Korrespondent auf
            - Bei 0 Treffern: erkläre welche Filter gesetzt wurden und schlage Alternativen vor
            - Zitiere NUR Fakten aus dem Tool-Ergebnis, erfinde nichts
            """;
    }

    private static string DescribeCustomField(PaperlessCustomField field)
    {
        var typeLabel = field.DataType switch
        {
            "boolean" => "Wahrheitswert (Werte: true/false)",
            "select" => field.ExtraData?.SelectOptions is { Count: > 0 } opts
                ? $"Auswahl (erlaubte Werte: {string.Join(", ", opts.Select(o => o.Label))})"
                : "Auswahl",
            "date" => "Datum (Format YYYY-MM-DD, Operatoren gt/gte/lt/lte für Zeiträume möglich)",
            "monetary" => "Währung/Betrag (numerischer Vergleich mit gt/gte/lt/lte möglich)",
            "integer" or "float" => "Zahl (numerischer Vergleich mit gt/gte/lt/lte möglich)",
            "url" => "URL/Link (Text)",
            _ => "Text",
        };
        return $"- \"{field.Name}\" ({typeLabel})";
    }
}
