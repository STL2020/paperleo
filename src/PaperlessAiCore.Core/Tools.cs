using System.Text.Json;
using System.Text.RegularExpressions;

namespace PaperlessAiCore.Core;

/// <summary>
/// Metadaten-Snapshot einer Paperless-Instanz (Tags/Dokumenttypen/Korrespondenten/
/// Custom Fields), den Tools.cs zum Filtern braucht. Entkoppelt Core bewusst von der
/// konkreten Cache-Implementierung der Api-Schicht (siehe PaperlessMetadataCache).
/// </summary>
public record PaperlessMetadataSnapshotLike(
    List<PaperlessTag> Tags,
    List<PaperlessDocumentType> DocumentTypes,
    List<PaperlessCorrespondent> Correspondents,
    List<PaperlessCustomField> CustomFields);

/// <summary>
/// Definiert die Tools (Function Calling Schemas), die dem LLM im Such-Agenten
/// zur Verfügung stehen, sowie deren Ausführung gegen die Paperless-API.
///
/// WICHTIG - Zero-Hardcoding: Dieser Code kennt KEINE konkreten Tag-/Feld-Namen
/// einer bestimmten Installation. Tags, Dokumenttypen, Korrespondenten und Custom
/// Fields werden immer als Listen von AUSSEN übergeben (siehe PaperlessMetadataCache
/// in der Api-Schicht) - so funktioniert der Such-Agent unverändert auf jeder
/// beliebigen Paperless-ngx-Instanz.
///
/// Tool 1 "search_documents": Standard-Metadatensuche (immer verfügbar).
/// Tool 2 "aggregate_costs": Kosten-Aggregation - PRO Feature.
/// </summary>
public static partial class Tools
{
    [GeneratedRegex(@"-\s*(-?\d[\d.]*,\d{2})\s*€\s*$")]
    private static partial Regex AmountPattern();

    public static double? ParseAmountFromTitle(string? title)
    {
        if (string.IsNullOrEmpty(title)) return null;
        var match = AmountPattern().Match(title);
        if (!match.Success) return null;

        var normalized = match.Groups[1].Value.Replace(".", "").Replace(",", ".");
        return double.TryParse(normalized, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static readonly object CustomFieldsParameterSchema = new
    {
        type = "array",
        items = new
        {
            type = "object",
            properties = new
            {
                name = new { type = "string", description = "EXAKTER Name des Custom Fields aus der oben im System-Prompt gelisteten Liste - niemals einen erfundenen Namen." },
                op = new { type = "string", @enum = new[] { "exact", "icontains", "gt", "gte", "lt", "lte", "isnull" }, description = "Vergleichsoperator. 'exact' für Booleans/Auswahl-Werte, 'icontains' für Text-Teilübereinstimmung, gt/gte/lt/lte für Zahlen/Beträge/Daten." },
                value = new { type = "string", description = "Vergleichswert als String - auch für Zahlen ('45.50'), Booleans ('true'/'false') oder Daten ('2025-01-01')." },
            },
            required = new[] { "name", "op", "value" },
        },
        description = "Strukturierte Filter auf Custom Fields der Instanz (siehe Liste im System-Prompt) - IMMER bevorzugen gegenüber Volltextsuche, wenn ein passendes Feld existiert (z.B. ein Boolean-Feld für eine Ja/Nein-Eigenschaft).",
    };

    public static readonly object SearchToolSchema = new
    {
        type = "function",
        function = new
        {
            name = "search_documents",
            description = "Durchsucht Paperless-ngx nach Dokumenten anhand von Volltext-Query, Tag-Namen, " +
                           "Korrespondent, Dokumenttyp, Custom Fields und/oder Datumsbereich. Gibt eine Liste " +
                           "gefundener Dokumente (Titel, Datum, Korrespondent, Tags) zurück.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    query = new
                    {
                        type = "string",
                        description = "PFLICHTFELD - der wichtigste Suchbegriff aus der Nutzeranfrage " +
                            "(Firmenname, Thema, Stichwort). Kann als leerer String übergeben werden, WENN " +
                            "bereits ein strukturierter Filter (tag_names/correspondent_name/document_type/" +
                            "custom_fields) die Anfrage eindeutig genug eingrenzt. Sonst NIEMALS leer lassen - " +
                            "das naheliegendste Stichwort verwenden (z.B. 'Arztrechnungen' -> 'Arzt').",
                    },
                    tag_names = new { type = "array", items = new { type = "string" }, description = "Liste von Tag-Namen aus der oben gelisteten Liste, nach denen zusätzlich gefiltert werden soll." },
                    document_type = new { type = "string", description = "Exakter Dokumenttyp-Name aus der oben gelisteten Liste, falls die Anfrage einen Dokumenttyp nennt." },
                    correspondent_name = new { type = "string", description = "Name des Korrespondenten/Absenders, z.B. 'Telekom'." },
                    date_from = new { type = "string", description = "Startdatum im Format YYYY-MM-DD (optional). Nutze das im System-Prompt genannte heutige Datum, um relative Angaben ('letzter Monat', 'dieses Jahr') korrekt umzurechnen." },
                    date_to = new { type = "string", description = "Enddatum im Format YYYY-MM-DD (optional). Nutze das im System-Prompt genannte heutige Datum für relative Angaben." },
                    custom_fields = CustomFieldsParameterSchema,
                },
                required = new[] { "query" },
            },
        },
    };

    public static readonly object AggregateToolSchema = new
    {
        type = "function",
        function = new
        {
            name = "aggregate_costs",
            description = "[PRO] Summiert die in den standardisierten Dokumenttiteln enthaltenen Euro-Beträge " +
                           "für eine gefilterte Dokumentmenge auf, z.B. um Gesamtkosten eines Versorgers oder " +
                           "Zeitraums zu ermitteln.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Freitext-Suchbegriff/Thema, falls die Anfrage eines nennt (z.B. 'Strom', 'Handwerker')." },
                    tag_names = new { type = "array", items = new { type = "string" }, description = "Tag-Namen zur Filterung, falls ein passender Tag existiert." },
                    document_type = new { type = "string", description = "Exakter Dokumenttyp-Name, falls die Anfrage einen nennt." },
                    correspondent_name = new { type = "string", description = "Korrespondent/Absender, dessen Kosten summiert werden sollen." },
                    date_from = new { type = "string", description = "Startdatum YYYY-MM-DD. Nutze das im System-Prompt genannte heutige Datum für relative Angaben." },
                    date_to = new { type = "string", description = "Enddatum YYYY-MM-DD. Nutze das im System-Prompt genannte heutige Datum für relative Angaben." },
                    custom_fields = CustomFieldsParameterSchema,
                },
            },
        },
    };

    /// <summary>
    /// Baut die Paperless-Query-Parameter aus den vom LLM gelieferten Argumenten.
    /// Tags/Korrespondenten/Dokumenttypen/Custom Fields werden gegen die BEREITS
    /// GELADENEN (gecachten) Listen gematcht - kein erneuter Paperless-Aufruf pro Suche nötig.
    /// </summary>
    private static Dictionary<string, string> BuildSearchParams(JsonElement args, PaperlessMetadataSnapshotLike metadata)
    {
        var query = new Dictionary<string, string> { ["page_size"] = "100" };

        if (args.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(q.GetString()))
            query["query"] = q.GetString()!;

        if (args.TryGetProperty("date_from", out var df) && df.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(df.GetString()))
            query["created__date__gte"] = df.GetString()!;

        if (args.TryGetProperty("date_to", out var dt) && dt.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(dt.GetString()))
            query["created__date__lte"] = dt.GetString()!;

        if (args.TryGetProperty("correspondent_name", out var cn) && cn.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(cn.GetString()))
        {
            var name = cn.GetString()!;
            // Erst exakt versuchen, dann tolerant (enthält) - Korrespondentennamen
            // können vom LLM leicht abweichend formuliert werden ("Huawei" statt
            // vollständigem gespeicherten Namen).
            var match = metadata.Correspondents.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? metadata.Correspondents.FirstOrDefault(c => c.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) query["correspondent__id"] = match.Id.ToString();
        }

        if (args.TryGetProperty("document_type", out var docType) && docType.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(docType.GetString()))
        {
            var match = metadata.DocumentTypes.FirstOrDefault(t => string.Equals(t.Name, docType.GetString(), StringComparison.OrdinalIgnoreCase));
            if (match is not null) query["document_type__id"] = match.Id.ToString();
        }

        if (args.TryGetProperty("tag_names", out var tn) && tn.ValueKind == JsonValueKind.Array)
        {
            var wanted = tn.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
            var ids = metadata.Tags.Where(t => wanted.Any(w => string.Equals(w, t.Name, StringComparison.OrdinalIgnoreCase)))
                                    .Select(t => t.Id.ToString());
            var idList = string.Join(",", ids);
            if (!string.IsNullOrEmpty(idList)) query["tags__id__in"] = idList;
        }

        // Generischer Custom-Field-Filter - funktioniert mit JEDEM Feldnamen der
        // jeweiligen Instanz, kein Hardcoding auf bestimmte Feldnamen.
        if (args.TryGetProperty("custom_fields", out var cf) && cf.ValueKind == JsonValueKind.Array && cf.GetArrayLength() > 0)
        {
            var conditions = new List<string>();
            foreach (var item in cf.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String) continue;

                var field = metadata.CustomFields.FirstOrDefault(f => string.Equals(f.Name, nameEl.GetString(), StringComparison.OrdinalIgnoreCase));
                if (field is null) continue; // Feld existiert auf dieser Instanz nicht - überspringen statt zu raten.

                var op = item.TryGetProperty("op", out var opEl) && opEl.ValueKind == JsonValueKind.String ? opEl.GetString()! : "exact";
                var rawValue = item.TryGetProperty("value", out var valEl) && valEl.ValueKind == JsonValueKind.String ? valEl.GetString() ?? "" : "";
                var jsonValue = FormatCustomFieldQueryValue(field.DataType, rawValue);

                conditions.Add($"[{JsonSerializer.Serialize(field.Name)},{JsonSerializer.Serialize(op)},{jsonValue}]");
            }

            if (conditions.Count == 1)
            {
                query["custom_field_query"] = conditions[0];
            }
            else if (conditions.Count > 1)
            {
                query["custom_field_query"] = $"[\"AND\",[{string.Join(",", conditions)}]]";
            }
        }

        return query;
    }

    /// <summary>Formatiert den Vergleichswert für custom_field_query passend zum Paperless-Datentyp des Feldes.</summary>
    private static string FormatCustomFieldQueryValue(string dataType, string rawValue)
    {
        return dataType switch
        {
            "boolean" => string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false",
            "integer" => int.TryParse(rawValue, out var i) ? i.ToString() : "null",
            "float" or "monetary" => double.TryParse(rawValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var f)
                ? f.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "null",
            _ => JsonSerializer.Serialize(rawValue), // string, select, date, url etc. - als JSON-String
        };
    }

    /// <summary>
    /// Fallback-Suche wenn strukturierte Filter 0 Treffer liefern: extrahiert die
    /// wichtigsten Stichwörter aus der Nutzeranfrage und sucht nur per Volltext.
    /// Ignoriert alle Custom-Field-/Korrespondenten-/Dokumenttyp-Filter.
    /// </summary>
    public static async Task<object> FallbackTextSearchAsync(PaperlessClient client, string userQuery, PaperlessMetadataSnapshotLike metadata)
    {
        // Bekannte Stopwörter/Filter-Wörter entfernen, den Rest als Volltext nutzen
        var stopWords = new[] { "zeige", "alle", "rechnungen", "dokumente", "aus", "von", "für",
            "im", "in", "des", "der", "die", "das", "und", "oder", "mit", "ohne",
            "show", "all", "documents", "invoices", "from", "for", "the", "and" };

        var words = userQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', ',', '?', '!').ToLowerInvariant())
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Distinct()
            .Take(4); // Max 4 Stichwörter für Paperless-Volltext

        var fallbackQuery = string.Join(" ", words);
        if (string.IsNullOrWhiteSpace(fallbackQuery)) fallbackQuery = userQuery.Trim();

        var searchParams = new Dictionary<string, string>
        {
            ["query"] = fallbackQuery,
            ["page_size"] = "25",
        };

        var data = await client.ListDocumentsAsync(searchParams);
        return new
        {
            count = data.Count,
            fallback = true,
            fallback_query = fallbackQuery,
            documents = data.Results.Select(d => new
            {
                id = d.Id,
                title = d.Title,
                created = d.Created,
                correspondent = d.Correspondent,
                tags = d.Tags,
                amount = ParseAmountFromTitle(d.Title),
            })
        };
    }

    public static async Task<object> RunSearchDocumentsAsync(PaperlessClient client, JsonElement args, PaperlessMetadataSnapshotLike metadata)
    {
        var searchParams = BuildSearchParams(args, metadata);
        var data = await client.ListDocumentsAsync(searchParams);
        var results = data.Results.Select(d => new
        {
            id = d.Id,
            title = d.Title,
            created = d.Created,
            correspondent = d.Correspondent,
            tags = d.Tags,
            amount = ParseAmountFromTitle(d.Title),
        });
        return new { count = data.Count, documents = results };
    }

    public static async Task<object> RunAggregateCostsAsync(PaperlessClient client, JsonElement args, PaperlessMetadataSnapshotLike metadata)
    {
        var searchParams = BuildSearchParams(args, metadata);
        var data = await client.ListDocumentsAsync(searchParams);

        double total = 0;
        var matched = new List<object>();
        var unmatchedCount = 0;

        foreach (var d in data.Results)
        {
            var amount = ParseAmountFromTitle(d.Title);
            if (amount is not null)
            {
                total += amount.Value;
                matched.Add(new { id = d.Id, title = d.Title, amount = amount.Value });
            }
            else
            {
                unmatchedCount++;
            }
        }

        return new
        {
            total_amount = Math.Round(total, 2),
            currency = "EUR",
            matched_count = matched.Count,
            unmatched_count = unmatchedCount,
            matched_documents = matched,
        };
    }
}
