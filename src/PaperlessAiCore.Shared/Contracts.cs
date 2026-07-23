namespace PaperlessAiCore.Shared;

/// <summary>
/// Admin-konfigurierbare Einstellungen. Wird als lesbare .env-Datei persistiert
/// (data/settings.env), damit ein Self-Hosted-Kunde alles bequem im Browser
/// einrichten kann, die Datei bei Bedarf aber auch direkt inspizieren/sichern kann.
/// </summary>
public class SettingsDto
{
    public string PaperlessUrl { get; set; } = "";
    public string PaperlessApiToken { get; set; } = "";

    public string LlmProvider { get; set; } = "openai"; // "openai" | "ollama" | "custom"
    public string LlmModel { get; set; } = "gpt-4o-mini";
    public string LlmApiKey { get; set; } = "";
    public string? LlmApiBase { get; set; }
    public double LlmTemperature { get; set; } = 0.3;
    public int LlmMaxTokens { get; set; } = 4096;

    public int PollIntervalSeconds { get; set; } = 60;
    public string ProcessedTagName { get; set; } = "ai-processed";
    public bool AutoModeEnabled { get; set; } = true;

    public bool EnableTagsAssignment { get; set; } = true;
    public bool EnableCorrespondentDetection { get; set; } = true;
    public bool EnableDocumentTypeClassification { get; set; } = true;
    public bool EnableTitleGeneration { get; set; } = true;
    public bool UseExistingCorrespondentsOnly { get; set; } = false;
    public bool UseExistingDocumentTypesOnly { get; set; } = false;
    public bool UseExistingTagsOnly { get; set; } = false;
    public bool EnableCustomFields { get; set; } = false;
    /// <summary>
    /// Komma-getrennte Liste der Custom-Field-Namen, die paperLeo aktiv befüllen soll.
    /// Leer = alle bekannten Felder werden befüllt (bisheriges Verhalten).
    /// </summary>
    public string? EnabledCustomFieldNames { get; set; }
    public string? CustomSystemPrompt { get; set; }
    /// <summary>
    /// Komma-getrenntes, vom Nutzer gepflegtes Tag-Vokabular - unabhängig vom aktuellen
    /// Zustand der Paperless-Datenbank. Wird zusätzlich zur Live-Tag-Liste in den
    /// [tags]-Platzhalter des Prompts eingespeist, damit die KI auch nach einem
    /// Tag-Reset (leere Datenbank) weiterhin das vollständige Vokabular kennt.
    /// </summary>
    public string? DefaultTagVocabulary { get; set; }
    /// <summary>Wie DefaultTagVocabulary, aber für Dokumenttypen.</summary>
    public string? DefaultDocumentTypeVocabulary { get; set; }

    public string? PremiumLicenseKey { get; set; }

    /// <summary>True wenn Paperless-URL/Token und LLM-Key gesetzt sind (Setup-Wizard abgeschlossen).</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PaperlessUrl) &&
        !string.IsNullOrWhiteSpace(PaperlessApiToken) &&
        !string.IsNullOrWhiteSpace(LlmApiKey);
}

public class ConnectionTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int? DocumentCount { get; set; }
}

public record QueryRequest(string Query);

public class QueryResponse
{
    public string Answer { get; set; } = "";
    public object? RawResults { get; set; }
}

public class StatusDto
{
    public bool IsConfigured { get; set; }
    public bool ProMode { get; set; }
    public bool AutoModeEnabled { get; set; }
    public DateTime? LastPollAt { get; set; }
    public int ProcessedSinceStart { get; set; }
    public string? LastError { get; set; }
    public int BuildNumber { get; set; }
}

public class ProcessedDocumentDto
{
    public int DocumentId { get; set; }
    public string Title { get; set; } = "";
    public double Confidence { get; set; }
    public DateTime ProcessedAt { get; set; }
    public string? DocumentType { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens => PromptTokens is null && CompletionTokens is null
        ? null
        : (PromptTokens ?? 0) + (CompletionTokens ?? 0);
}

public class DocumentSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Created { get; set; }
}

/// <summary>Vollständige Detailansicht für die Vorschau-Drawer (Namen statt IDs aufgelöst).</summary>
public class DocumentDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Created { get; set; }
    public string? Correspondent { get; set; }
    public string? DocumentType { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? ContentExcerpt { get; set; }
}

/// <summary>
/// Ergebnis einer manuellen Einzeldokument-Extraktion (Testen-Seite, Vorschau).
/// Wird 1:1 an /apply zurückgeschickt, um genau diese Werte (ohne erneuten
/// LLM-Aufruf) tatsächlich nach Paperless zu schreiben.
/// </summary>
public class ProcessDocumentResultDto
{
    public int DocumentId { get; set; }
    public string Title { get; set; } = "";
    public string Correspondent { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string? InvoiceNumber { get; set; }
    public string? ReceiptNumber { get; set; }
    public List<string> Tags { get; set; } = new();
    public double? Amount { get; set; }
    public string? Date { get; set; }
    public double Confidence { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
}

public class TokenBucketDto
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

public class DocumentTypeCountDto
{
    public string Type { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>Aggregierte Kennzahlen für das Admin-Dashboard.</summary>
public class DashboardDto
{
    public string AppVersion { get; set; } = "";
    public int BuildNumber { get; set; }
    public int TotalDocuments { get; set; }
    public int ProcessedDocuments { get; set; }
    public int UnprocessedDocuments { get; set; }
    public int TotalTags { get; set; }
    public int TotalCorrespondents { get; set; }

    public long TotalTokensUsed { get; set; }
    public int DocumentsWithTokenData { get; set; }
    public double AveragePromptTokens { get; set; }
    public double AverageCompletionTokens { get; set; }
    public double AverageTotalTokens { get; set; }

    public List<TokenBucketDto> TokenDistribution { get; set; } = new();
    public List<DocumentTypeCountDto> DocumentTypeDistribution { get; set; } = new();

    public bool AutoModeEnabled { get; set; }
    public bool IsIdle { get; set; }
    public DateTime? LastPollAt { get; set; }
    public int ProcessedToday { get; set; }
    public DateTime? LastProcessedAt { get; set; }
    public string? LastError { get; set; }

    public bool ProMode { get; set; }
    public int ProcessedSinceStart { get; set; }
    public int FreeDocsRemaining { get; set; }
    public int DemoDocsRemaining { get; set; }

    // System-Health ("Dienstüberwachung")
    public string PaperlessHealth { get; set; } = "unknown"; // "ok" | "error" | "unknown"
    public string? PaperlessHealthMessage { get; set; }
    public string? PaperlessVersion { get; set; }
    public string LlmHealth { get; set; } = "unknown";
    public string? LlmHealthMessage { get; set; }

    // Echte Ressourcen-Nutzung DES EIGENEN PROZESSES (nicht von Paperless/Host).
    public List<ResourceSampleDto> ResourceHistory { get; set; } = new();
}

public class ResourceSampleDto
{
    public string Time { get; set; } = "";
    public double CpuPercent { get; set; }
    public double MemoryMb { get; set; }
}

public class LogEntryDto
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
}

public class ScanNowResult
{
    public bool Success { get; set; }
    public int ProcessedCount { get; set; }
    public string? Message { get; set; }
}

public class IndexResetResult
{
    public bool Success { get; set; }
    public int ResetCount { get; set; }
    public string? Message { get; set; }
}

public class FullResetResult
{
    public bool Success { get; set; }
    public int CorrespondentsDeleted { get; set; }
    public int DocumentTypesDeleted { get; set; }
    public int TagsDeleted { get; set; }
    public string? Message { get; set; }
}

public class DeleteAllResult
{
    public bool Success { get; set; }
    public int DeletedCount { get; set; }
    public string? Message { get; set; }
}

public class SeedTagsResult
{
    public bool Success { get; set; }
    public int CreatedCount { get; set; }
    public int AlreadyExistedCount { get; set; }
    public string? Message { get; set; }
}

public class BackfillResult
{
    public bool Success { get; set; }
    public int Processed { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public string? Message { get; set; }
}

/// <summary>Echtzeit-Fortschritt des laufenden Backfill-Jobs (wird per Polling abgerufen).</summary>
public class BackfillProgress
{
    public bool IsRunning { get; set; }
    public int Total { get; set; }
    public int Current { get; set; }
    public int Updated { get; set; }
    public int Errors { get; set; }
    public string? CurrentDocTitle { get; set; }
    public bool IsComplete { get; set; }
    public BackfillResult? FinalResult { get; set; }
}

public class CustomFieldDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
}

public class CreateCustomFieldRequest
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "string";
}

public class CorrespondentMergeSuggestion
{
    public string PrimaryName { get; set; } = "";
    public List<string> Aliases { get; set; } = new();
    /// <summary>Alle Namen der Gruppe – User wählt im Frontend welcher der Primary wird</summary>
    public List<string> AllNames { get; set; } = new();
    /// <summary>1=sicher, 2=wahrscheinlich, 3=möglich (von KI bewertet)</summary>
    public int Level { get; set; } = 2;
}

public class MergeCorrespondentsRequest
{
    public string PrimaryName { get; set; } = "";
    public List<string> Aliases { get; set; } = new();
}

public class MergeResult
{
    public bool Success { get; set; }
    public int DocumentsReassigned { get; set; }
    public int AliasesDeleted { get; set; }
    public string? Message { get; set; }
}

public class SuggestVocabularyResult
{
    public bool Success { get; set; }
    public string? SuggestedVocabulary { get; set; }
    public int DocumentsAnalyzed { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Versionsnummer der Anwendung selbst (nicht von Paperless-ngx) - bei jedem
/// nennenswerten Release-Stand hier manuell hochzählen, wird im Dashboard angezeigt.
/// </summary>
public static class AppInfo
{
    public const string Version = "5.2.0";
    public const int DemoDocumentLimit = 50;
    public const int FreeDocumentLimit = 50; // Alias für bestehenden Code
    public static readonly string[] FreeDefaultTags = ["Rechnung", "Vertrag", "Privat", "Arbeit", "Sonstiges"];
    public static readonly string[] FreeDefaultDocumentTypes = ["Rechnung", "Vertrag", "Anschreiben", "Bescheid", "Sonstiges"];
}


public class ProcessingJobDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public int? DocumentId { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "queued"; // queued|running|done|failed
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public double? Confidence { get; set; }
}
