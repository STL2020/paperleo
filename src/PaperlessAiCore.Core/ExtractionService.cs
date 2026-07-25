using System.Globalization;
using System.Text.Json;

namespace PaperlessAiCore.Core;

public class MetadataExtractionException(string message) : Exception(message);

/// <summary>
/// Kapselt den kompletten Extraktions-Schritt: OCR-Text -> LLM -> validiertes
/// ExtractedMetadata. Parsing ist bewusst SEHR tolerant: case-insensitive
/// Property-Namen, gängige Alias-Felder, Beträge auch als String, Tags auch als
/// Objekte statt reiner Strings.
///
/// WICHTIG: Der Titel wird NICHT roh von der KI übernommen und gegen ein starres
/// Format geprüft (das brach bei jeder kleinen KI-Abweichung, z.B. Punkt statt
/// Komma bei Beträgen oder englischen Dokumenttyp-Namen, komplett ab). Stattdessen
/// bauen WIR den Titel deterministisch aus den strukturierten Feldern
/// (document_type, correspondent, invoice_number, amount) zusammen - garantiert
/// korrektes Format, unabhängig von KI-Formatierungslaunen.
/// </summary>
public static class ExtractionService
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");

    public static async Task<ExtractedMetadata> ExtractAsync(
        LlmClient llm,
        string ocrText,
        ProcessingOptions options,
        IReadOnlyList<string>? existingTags,
        IReadOnlyList<string>? existingDocumentTypes,
        IReadOnlyList<string>? systemUsers = null,
        CancellationToken ct = default)
    {
        string rawJson;
        LlmUsage? usage;
        var systemPrompt = Prompts.BuildSystemPrompt(options, existingTags, existingDocumentTypes, systemUsers);
        var userPrompt = Prompts.BuildUserPrompt(ocrText);
        try
        {
            var completion = await llm.CompleteJsonAsync(systemPrompt, userPrompt, ct: ct);
            rawJson = completion.Content;
            usage = completion.Usage;
        }
        catch (Exception ex)
        {
            throw new MetadataExtractionException($"LLM-Aufruf fehlgeschlagen: {ex.Message}");
        }

        ExtractedMetadata metadata;
        try
        {
            metadata = ParseTolerant(rawJson);
        }
        catch (JsonException) when (LooksTruncated(rawJson))
        {
            // Antwort wurde mitten im JSON abgeschnitten (max_tokens zu knapp für das
            // inzwischen recht große Extraktionsschema). Statt sofort zu scheitern: EIN
            // automatischer Retry mit deutlich mehr Tokens (8000, unabhängig von der
            // konfigurierten Einstellung) - behebt das Problem unabhängig davon, ob der
            // Nutzer sein Token-Limit schon angepasst hat.
            try
            {
                var retryCompletion = await llm.CompleteJsonAsync(systemPrompt, userPrompt, maxTokensOverride: 8000, ct: ct);
                rawJson = retryCompletion.Content;
                usage = retryCompletion.Usage;
                metadata = ParseTolerant(rawJson);
            }
            catch (JsonException ex2)
            {
                throw new MetadataExtractionException(
                    $"LLM-Antwort war auch nach Retry mit mehr Tokens kein valides JSON: {ex2.Message} | Antwort: {Truncate(rawJson, 500)}. " +
                    "Erhöhe ggf. das Token-Limit in den Einstellungen dauerhaft.");
            }
        }
        catch (JsonException ex)
        {
            throw new MetadataExtractionException($"LLM-Antwort ist kein valides JSON: {ex.Message} | Antwort: {Truncate(rawJson, 500)}");
        }

        metadata.PromptTokens = usage?.PromptTokens;
        metadata.CompletionTokens = usage?.CompletionTokens;
        metadata.RawResponse = rawJson;

        if (options.EnableTitleGeneration)
        {
            metadata.Title = BuildTitle(metadata.DocumentType, metadata.Correspondent, metadata.InvoiceNumber, metadata.Amount);
        }

        return metadata;
    }

    /// <summary>
    /// Baut den Titel deterministisch: "[TYP] - [ABSENDER]" plus optional
    /// " - Nr. [Rechnungsnummer]" plus optional " - [BETRAG] €" (deutsches
    /// Komma-Format). Fehlt der Betrag, wird er komplett weggelassen - NICHT
    /// als "0,00 €" angezeigt, das wäre irreführend.
    /// </summary>
    public static string BuildTitle(string? documentType, string? correspondent, string? invoiceNumber, double? amount)
    {
        var type = string.IsNullOrWhiteSpace(documentType) ? "Dokument" : documentType.Trim();
        var who = string.IsNullOrWhiteSpace(correspondent) ? "Unbekannt" : correspondent.Trim();

        var parts = new List<string> { type, who };

        if (!string.IsNullOrWhiteSpace(invoiceNumber))
        {
            parts.Add($"Nr. {invoiceNumber.Trim()}");
        }

        var title = string.Join(" - ", parts);

        if (amount is > 0)
        {
            title += $" - {amount.Value.ToString("N2", GermanCulture)} €";
        }

        return title;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    /// <summary>Grobe Heuristik: sieht die Antwort so aus, als wäre sie mitten im JSON abgeschnitten worden?</summary>
    private static bool LooksTruncated(string rawJson)
    {
        var trimmed = rawJson.TrimEnd();
        return trimmed.Length > 0 && !trimmed.EndsWith('}');
    }

    /// <summary>
    /// Liest die bekannten Felder SEHR tolerant aus dem JSON: case-insensitive
    /// Property-Suche, mehrere Alias-Namen pro Feld, Zahlen auch als String
    /// (Komma oder Punkt als Dezimaltrennzeichen), Tags auch als Objekte statt
    /// reiner Strings.
    /// </summary>
    private static ExtractedMetadata ParseTolerant(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        var tags = GetStringArray(root, "tags");
        if (tags.Count == 0) tags = GetStringArray(root, "labels");

        return new ExtractedMetadata
        {
            Title = GetString(root, "title") ?? "",
            Correspondent = GetString(root, "correspondent") ?? GetString(root, "sender") ?? "Unbekannt",
            DocumentType = GetString(root, "document_type") ?? GetString(root, "documentType") ?? GetString(root, "doc_type") ?? "",
            InvoiceNumber = GetString(root, "invoice_number") ?? GetString(root, "invoiceNumber") ?? GetString(root, "rechnungsnummer"),
            ReceiptNumber = GetString(root, "receipt_number") ?? GetString(root, "receiptNumber") ?? GetString(root, "belegnummer"),
            SerialNumber = GetString(root, "serial_number") ?? GetString(root, "seriennummer"),
            Vehicle = GetString(root, "vehicle") ?? GetString(root, "fahrzeug"),
            TradeService = GetString(root, "trade_service") ?? GetString(root, "gewerk_dienstleistung") ?? GetString(root, "gewerk"),
            PropertyAddress = GetString(root, "property_address") ?? GetString(root, "objekt_adresse") ?? GetString(root, "objekt"),
            FamilyMember = GetString(root, "family_member") ?? GetString(root, "familienmitglied"),
            ContractEndDate = GetString(root, "contract_end_date") ?? GetString(root, "vertragsende"),
            ContractOrCustomerNumber = GetString(root, "contract_or_customer_number") ?? GetString(root, "vertragsnummer") ?? GetString(root, "kundennummer"),
            DueDate = GetString(root, "due_date") ?? GetString(root, "faelligkeitsdatum") ?? GetString(root, "fälligkeitsdatum"),
            IsTaxRelevant = GetBool(root, "is_tax_relevant") ?? GetBool(root, "tax_relevant") ?? GetBool(root, "steuerrelevant"),
            Amount = GetNumber(root, "amount") ?? GetNumber(root, "total") ?? GetNumber(root, "betrag"),
            TaxAmount = GetNumber(root, "tax_amount") ?? GetNumber(root, "tax") ?? GetNumber(root, "mwst"),
            AssignedUser = GetString(root, "assigned_user") ?? GetString(root, "assignedUser"),
            DocumentDate = GetString(root, "document_date") ?? GetString(root, "date") ?? GetString(root, "documentDate"),
            Date = GetString(root, "date") ?? GetString(root, "document_date") ?? GetString(root, "documentDate"),
            Tags = tags,
            Confidence = GetNumber(root, "confidence") ?? 1.0,
        };
    }

    private static JsonElement? FindPropertyCaseInsensitive(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return prop.Value;
            }
        }
        return null;
    }

    private static string? GetString(JsonElement root, string name)
    {
        var el = FindPropertyCaseInsensitive(root, name);
        if (el is not { } value) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static double? GetNumber(JsonElement root, string name)
    {
        var el = FindPropertyCaseInsensitive(root, name);
        if (el is not { } value) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            // Akzeptiert sowohl "45,90" als auch "45.90" als String-Zahl.
            JsonValueKind.String when TryParseFlexibleNumber(value.GetString(), out var d) => d,
            _ => null,
        };
    }

    private static bool? GetBool(JsonElement root, string name)
    {
        var el = FindPropertyCaseInsensitive(root, name);
        if (el is not { } value) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var b) => b,
            _ => null,
        };
    }

    private static bool TryParseFlexibleNumber(string? raw, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var s = raw.Trim();
        var lastComma = s.LastIndexOf(',');
        var lastDot = s.LastIndexOf('.');

        // Das letzte vorkommende Komma/Punkt ist das Dezimaltrennzeichen; das
        // jeweils andere Zeichen wird als Tausendertrennzeichen entfernt.
        if (lastComma > lastDot)
        {
            s = s.Replace(".", "").Replace(",", ".");
        }
        else if (lastDot > lastComma)
        {
            s = s.Replace(",", "");
        }

        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    private static List<string> GetStringArray(JsonElement root, string name)
    {
        var el = FindPropertyCaseInsensitive(root, name);
        if (el is not { ValueKind: JsonValueKind.Array } arr) return new();

        var result = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) result.Add(s.Trim());
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                var nameEl = FindPropertyCaseInsensitive(item, "name")
                    ?? FindPropertyCaseInsensitive(item, "tag")
                    ?? FindPropertyCaseInsensitive(item, "value");
                if (nameEl is { ValueKind: JsonValueKind.String } ne)
                {
                    var s = ne.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) result.Add(s.Trim());
                }
            }
        }
        return result;
    }
}
