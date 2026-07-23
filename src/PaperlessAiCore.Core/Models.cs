using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaperlessAiCore.Core;

public class PaperlessDocument
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("created")]
    public string? Created { get; set; }

    [JsonPropertyName("tags")]
    public List<int> Tags { get; set; } = new();

    [JsonPropertyName("correspondent")]
    public int? Correspondent { get; set; }

    [JsonPropertyName("document_type")]
    public int? DocumentType { get; set; }
}

public class PaperlessListResponse<T>
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("results")]
    public List<T> Results { get; set; } = new();
}

public class PaperlessTag
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public class PaperlessCorrespondent
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public class PaperlessDocumentType
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

/// <summary>Benutzerdefiniertes Feld (Custom Field) in Paperless-ngx.</summary>
public class PaperlessCustomField
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("data_type")]
    public string DataType { get; set; } = "string";

    [JsonPropertyName("extra_data")]
    public PaperlessCustomFieldExtraData? ExtraData { get; set; }
}

public class PaperlessCustomFieldExtraData
{
    [JsonPropertyName("select_options")]
    public List<PaperlessSelectOption>? SelectOptions { get; set; }
}

/// <summary>
/// Eine Auswahl-Option eines "select"-Custom-Fields. Id ist ein String (kein Index!).
/// Nutzt einen toleranten Custom-Converter, weil ältere Paperless-Versionen
/// select_options als reine String-Liste liefern, neuere als Objekte {id, label}.
/// </summary>
[JsonConverter(typeof(PaperlessSelectOptionConverter))]
public class PaperlessSelectOption
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
}

public class PaperlessSelectOptionConverter : JsonConverter<PaperlessSelectOption>
{
    public override PaperlessSelectOption Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Ältere Paperless-Versionen (< API v7): select_options ist eine reine
        // String-Liste - dort dient der Text selbst als "Id" (es gibt keine separate ID).
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString() ?? "";
            return new PaperlessSelectOption { Id = s, Label = s };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            string? id = null;
            string? label = null;

            foreach (var prop in root.EnumerateObject())
            {
                if (string.Equals(prop.Name, "id", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                {
                    id = prop.Value.GetString();
                }
                else if ((string.Equals(prop.Name, "label", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(prop.Name, "value", StringComparison.OrdinalIgnoreCase))
                         && prop.Value.ValueKind == JsonValueKind.String)
                {
                    label = prop.Value.GetString();
                }
            }

            label ??= id ?? "";
            id ??= label;
            return new PaperlessSelectOption { Id = id, Label = label };
        }

        // Unbekanntes Format - überspringen statt die gesamte Deserialisierung crashen zu lassen.
        reader.Skip();
        return new PaperlessSelectOption();
    }

    public override void Write(Utf8JsonWriter writer, PaperlessSelectOption value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("label", value.Label);
        writer.WriteEndObject();
    }
}

/// <summary>
/// Strikt validiertes Ergebnis der LLM-Extraktion. Entspricht 1:1 dem im
/// System-Prompt vorgegebenen JSON-Schema.
/// </summary>
public class ExtractedMetadata
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("correspondent")]
    public string Correspondent { get; set; } = "";

    [JsonPropertyName("document_type")]
    public string DocumentType { get; set; } = "";

    [JsonPropertyName("invoice_number")]
    public string? InvoiceNumber { get; set; }

    [JsonPropertyName("receipt_number")]
    public string? ReceiptNumber { get; set; }

    // Weitere optionale Custom-Field-Kandidaten - nur befüllt, wenn im Dokument
    // eindeutig erkennbar. Werden NUR geschrieben, wenn EnableCustomFields aktiv ist
    // und das jeweilige Paperless-Custom-Field existiert.
    [JsonPropertyName("serial_number")]
    public string? SerialNumber { get; set; }

    [JsonPropertyName("vehicle")]
    public string? Vehicle { get; set; }

    [JsonPropertyName("trade_service")]
    public string? TradeService { get; set; }

    [JsonPropertyName("property_address")]
    public string? PropertyAddress { get; set; }

    [JsonPropertyName("family_member")]
    public string? FamilyMember { get; set; }

    [JsonPropertyName("contract_end_date")]
    public string? ContractEndDate { get; set; }

    [JsonPropertyName("contract_or_customer_number")]
    public string? ContractOrCustomerNumber { get; set; }

    [JsonPropertyName("due_date")]
    public string? DueDate { get; set; }

    /// <summary>
    /// Einschätzung der KI, ob das Dokument steuerlich relevant ist (absetzbar,
    /// belegpflichtig für die Steuererklärung o.ä.) - null wenn nicht beurteilbar.
    /// Wird auf das bestehende Paperless-Custom-Field "Steuer" (Wahrheitswert)
    /// geschrieben, wenn EnableCustomFields aktiv ist.
    /// </summary>
    [JsonPropertyName("is_tax_relevant")]
    public bool? IsTaxRelevant { get; set; }

    [JsonPropertyName("amount")]
    public double? Amount { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    /// <summary>Neues Feld: Steuerbetrag (extrahiert, nicht errechnet)</summary>
    [JsonPropertyName("tax_amount")]
    public double? TaxAmount { get; set; }

    /// <summary>Aus der User-Liste zugewiesener Nutzer oder null</summary>
    [JsonPropertyName("assigned_user")]
    public string? AssignedUser { get; set; }

    /// <summary>Flexibles Key-Value-Dict für zukünftige Custom Fields</summary>
    [JsonPropertyName("custom_fields")]
    public Dictionary<string, object?>? CustomFields { get; set; }

    /// <summary>Dokumentdatum aus KI-Extraktion (neues Feld, ersetzt "date")</summary>
    [JsonPropertyName("document_date")]
    public string? DocumentDate { get; set; }

    // Nicht Teil des LLM-JSON-Schemas - wird nach dem Aufruf von ExtractionService
    // aus der API-Antwort ("usage") befüllt, für die Token-Verbrauchs-Anzeige im Dashboard.
    [JsonIgnore]
    public int? PromptTokens { get; set; }
    [JsonIgnore]
    public int? CompletionTokens { get; set; }
    [JsonIgnore]
    public string? RawResponse { get; set; }
}

/// <summary>
/// Laufzeit-Konfiguration für einen einzelnen Paperless-Client/LLM-Aufruf.
/// Wird aus den admin-konfigurierten Settings gebaut (DB), nicht aus ENV,
/// damit Nutzer alles bequem im Setup-Wizard/Settings-Screen eintragen können.
/// </summary>
public record PaperlessConnectionConfig(string BaseUrl, string ApiToken);

public record LlmConnectionConfig(string Model, string ApiKey, string? ApiBase, double Temperature = 0, int? MaxTokens = null);

/// <summary>
/// Steuert, welche KI-Funktionen aktiv sind und wie streng mit bestehenden
/// Paperless-Entitäten umgegangen wird. Direktes Pendant zu den "AI Function
/// Limits" / "Use existing Correspondents and Tags" Optionen bekannter
/// vergleichbarer Tools, plus einem frei editierbaren System-Prompt.
/// </summary>
public record ProcessingOptions(
    bool EnableTagsAssignment,
    bool EnableCorrespondentDetection,
    bool EnableDocumentTypeClassification,
    bool EnableTitleGeneration,
    bool UseExistingCorrespondentsOnly,
    bool UseExistingDocumentTypesOnly,
    bool UseExistingTagsOnly,
    string? CustomSystemPrompt,
    bool EnableCustomFields = false,
    string? DefaultTagVocabulary = null,
    string? DefaultDocumentTypeVocabulary = null,
    bool IsProMode = false);

/// <summary>Paperless-Nutzer aus /api/users/</summary>
public class PaperlessUser
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = "";
    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = "";
    [JsonPropertyName("email")]
    public string Email { get; set; } = "";
    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;
    public string DisplayName => !string.IsNullOrWhiteSpace(FirstName) || !string.IsNullOrWhiteSpace(LastName)
        ? $"{FirstName} {LastName}".Trim()
        : Username;
}

/// <summary>Review-Queue-Eintrag für Dokumente mit niedrigem Konfidenzwert</summary>
public class ReviewQueueEntry
{
    public int DocumentId { get; set; }
    public string DocumentTitle { get; set; } = "";
    public double Confidence { get; set; }
    public string Reason { get; set; } = "";
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public bool IsReviewed { get; set; }
    public ExtractedMetadata? ProposedMetadata { get; set; }
}
