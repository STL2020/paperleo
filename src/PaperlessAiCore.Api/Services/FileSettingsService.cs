using System.Text;
using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Api.Services;

public interface ISettingsService
{
    Task<SettingsDto> GetAsync(CancellationToken ct = default);
    Task SaveAsync(SettingsDto dto, CancellationToken ct = default);
    Task<string> ExportRawAsync(CancellationToken ct = default);
    Task ImportRawAsync(string content, CancellationToken ct = default);
}

/// <summary>
/// Speichert die Konfiguration als lesbare .env-Datei (Standard: data/settings.env)
/// statt in einer SQLite-Datenbank. Bewusste Entscheidung: keine externe
/// YAML-Bibliothek als zusätzliche NuGet-Abhängigkeit, kein Schema-Migrations-Thema
/// wie bei EF Core - Werte werden beim Lesen einfach mit sinnvollen Defaults befüllt,
/// wenn ein Feld (noch) fehlt. Werte werden immer gequotet + escaped geschrieben,
/// damit auch mehrzeilige Werte (Custom-Prompt) sicher funktionieren.
/// </summary>
public class FileSettingsService : ISettingsService
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileSettingsService(IConfiguration config)
    {
        var configuredPath = config["ConfigPath"]
            ?? Environment.GetEnvironmentVariable("CONFIG_PATH")
            ?? "data/settings.env";
        _path = Path.GetFullPath(configuredPath);

        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public async Task<SettingsDto> GetAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return ReadFile();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(SettingsDto dto, CancellationToken ct = default)
    {
        // Normalisieren, bevor geschrieben wird - u.a. damit ein leer gelassenes
        // Tag-Namen-Feld nicht als leerer String an Paperless geschickt wird
        // (führt dort zu "This field is required." beim Tag-Anlegen).
        dto.PaperlessUrl = dto.PaperlessUrl.Trim();
        dto.PaperlessApiToken = dto.PaperlessApiToken.Trim();
        dto.LlmModel = dto.LlmModel.Trim();
        dto.LlmApiKey = dto.LlmApiKey.Trim();
        dto.LlmApiBase = string.IsNullOrWhiteSpace(dto.LlmApiBase) ? null : dto.LlmApiBase.Trim();
        dto.PollIntervalSeconds = dto.PollIntervalSeconds is > 0 ? dto.PollIntervalSeconds : 60;
        dto.ProcessedTagName = string.IsNullOrWhiteSpace(dto.ProcessedTagName) ? "ai-processed" : dto.ProcessedTagName.Trim();
        dto.CustomSystemPrompt = string.IsNullOrWhiteSpace(dto.CustomSystemPrompt) ? null : dto.CustomSystemPrompt.Trim();
        dto.DefaultTagVocabulary = string.IsNullOrWhiteSpace(dto.DefaultTagVocabulary) ? null : dto.DefaultTagVocabulary.Trim();
        dto.DefaultDocumentTypeVocabulary = string.IsNullOrWhiteSpace(dto.DefaultDocumentTypeVocabulary) ? null : dto.DefaultDocumentTypeVocabulary.Trim();
        dto.PremiumLicenseKey = string.IsNullOrWhiteSpace(dto.PremiumLicenseKey) ? null : dto.PremiumLicenseKey.Trim();
        // Geklemmt auf 0-2 (üblicher Temperature-Bereich) - ein Ausreißer wie z.B.
        // 7.0 würde jeden Aufruf an den LLM-Provider mit einem API-Fehler scheitern lassen.
        dto.LlmTemperature = Math.Clamp(dto.LlmTemperature, 0.0, 2.0);
        dto.LlmMaxTokens = dto.LlmMaxTokens is > 0 ? dto.LlmMaxTokens : 4096;

        await _lock.WaitAsync(ct);
        try
        {
            WriteFile(dto);
        }
        finally
        {
            _lock.Release();
        }
    }

    private SettingsDto ReadFile()
    {
        var dto = new SettingsDto();
        if (!File.Exists(_path))
        {
            return dto;
        }

        var values = ParseEnvFile(File.ReadAllText(_path));

        dto.PaperlessUrl = values.GetValueOrDefault("PAPERLESS_URL", "");
        dto.PaperlessApiToken = values.GetValueOrDefault("PAPERLESS_API_TOKEN", "");
        dto.LlmProvider = values.GetValueOrDefault("LLM_PROVIDER", "openai");
        dto.LlmModel = values.GetValueOrDefault("LLM_MODEL", "gpt-4o-mini");
        dto.LlmApiKey = values.GetValueOrDefault("LLM_API_KEY", "");
        dto.LlmApiBase = NullIfEmpty(values.GetValueOrDefault("LLM_API_BASE", ""));
        dto.LlmTemperature = Math.Clamp(ParseDouble(values.GetValueOrDefault("LLM_TEMPERATURE"), 0.3), 0.0, 2.0);
        dto.LlmMaxTokens = ParseInt(values.GetValueOrDefault("LLM_MAX_TOKENS"), 4096);
        dto.MaxPagesPerDocument = ParseInt(values.GetValueOrDefault("MAX_PAGES_PER_DOCUMENT"), 0);
        dto.MonthlyCostLimitEur = (decimal)ParseDouble(values.GetValueOrDefault("MONTHLY_COST_LIMIT_EUR"), 0);
        dto.MonthlyTokenUsed = ParseInt(values.GetValueOrDefault("MONTHLY_TOKEN_USED"), 0);
        dto.MonthlyTokenLimit = ParseInt(values.GetValueOrDefault("MONTHLY_TOKEN_LIMIT"), 0);
        dto.PollIntervalSeconds = ParseInt(values.GetValueOrDefault("POLL_INTERVAL_SECONDS"), 60);
        dto.ProcessedTagName = values.GetValueOrDefault("PROCESSED_TAG_NAME", "");
        dto.AutoModeEnabled = ParseBool(values.GetValueOrDefault("AUTO_MODE_ENABLED"), true);
        dto.EnableTagsAssignment = ParseBool(values.GetValueOrDefault("ENABLE_TAGS_ASSIGNMENT"), true);
        dto.EnableCorrespondentDetection = ParseBool(values.GetValueOrDefault("ENABLE_CORRESPONDENT_DETECTION"), true);
        dto.EnableDocumentTypeClassification = ParseBool(values.GetValueOrDefault("ENABLE_DOCUMENT_TYPE_CLASSIFICATION"), true);
        dto.EnableTitleGeneration = ParseBool(values.GetValueOrDefault("ENABLE_TITLE_GENERATION"), true);
        // Migration: falls die alte kombinierte Einstellung noch existiert, aber die neuen
        // granularen Flags noch nie gespeichert wurden, den alten Wert für alle drei übernehmen.
        var legacyValue = values.TryGetValue("USE_EXISTING_ENTITIES_ONLY", out var legacy) && bool.TryParse(legacy, out var lv) && lv;
        dto.UseExistingCorrespondentsOnly = values.ContainsKey("USE_EXISTING_CORRESPONDENTS_ONLY")
            ? ParseBool(values.GetValueOrDefault("USE_EXISTING_CORRESPONDENTS_ONLY"), false) : legacyValue;
        dto.UseExistingDocumentTypesOnly = values.ContainsKey("USE_EXISTING_DOCUMENT_TYPES_ONLY")
            ? ParseBool(values.GetValueOrDefault("USE_EXISTING_DOCUMENT_TYPES_ONLY"), false) : legacyValue;
        dto.UseExistingTagsOnly = values.ContainsKey("USE_EXISTING_TAGS_ONLY")
            ? ParseBool(values.GetValueOrDefault("USE_EXISTING_TAGS_ONLY"), false) : legacyValue;
        dto.EnableCustomFields = ParseBool(values.GetValueOrDefault("ENABLE_CUSTOM_FIELDS"), false);
        dto.CustomSystemPrompt = NullIfEmpty(values.GetValueOrDefault("CUSTOM_SYSTEM_PROMPT", ""));
        dto.DefaultTagVocabulary = NullIfEmpty(values.GetValueOrDefault("DEFAULT_TAG_VOCABULARY", ""));
        dto.DefaultDocumentTypeVocabulary = NullIfEmpty(values.GetValueOrDefault("DEFAULT_DOCUMENT_TYPE_VOCABULARY", ""));
        dto.EnabledCustomFieldNames = NullIfEmpty(values.GetValueOrDefault("ENABLED_CUSTOM_FIELD_NAMES", ""));
        dto.PremiumLicenseKey = NullIfEmpty(values.GetValueOrDefault("PREMIUM_LICENSE_KEY", ""));
        dto.TitleParts     = NullIfEmpty(values.GetValueOrDefault("TITLE_PARTS", ""));
        dto.TitleSeparator = NullIfEmpty(values.GetValueOrDefault("TITLE_SEPARATOR", ""));

        return dto;
    }

    private void WriteFile(SettingsDto dto)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Paperless-AI Core - Konfiguration");
        sb.AppendLine("# Wird über den Settings-Screen der App verwaltet. Manuelle Bearbeitung möglich -");
        sb.AppendLine("# ein Neustart der App ist dann nötig, damit Änderungen wirksam werden.");
        sb.AppendLine();

        AppendKv(sb, "PAPERLESS_URL", dto.PaperlessUrl);
        AppendKv(sb, "PAPERLESS_API_TOKEN", dto.PaperlessApiToken);
        sb.AppendLine();
        AppendKv(sb, "LLM_PROVIDER", dto.LlmProvider);
        AppendKv(sb, "LLM_MODEL", dto.LlmModel);
        AppendKv(sb, "LLM_API_KEY", dto.LlmApiKey);
        AppendKv(sb, "LLM_API_BASE", dto.LlmApiBase ?? "");
        AppendKv(sb, "LLM_TEMPERATURE", dto.LlmTemperature.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendKv(sb, "LLM_MAX_TOKENS", dto.LlmMaxTokens.ToString());
        AppendKv(sb, "MAX_PAGES_PER_DOCUMENT", dto.MaxPagesPerDocument.ToString());
        AppendKv(sb, "MONTHLY_COST_LIMIT_EUR", dto.MonthlyCostLimitEur.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendKv(sb, "MONTHLY_TOKEN_USED", dto.MonthlyTokenUsed.ToString());
        AppendKv(sb, "MONTHLY_TOKEN_LIMIT", dto.MonthlyTokenLimit.ToString());
        sb.AppendLine();
        AppendKv(sb, "POLL_INTERVAL_SECONDS", dto.PollIntervalSeconds.ToString());
        AppendKv(sb, "PROCESSED_TAG_NAME", dto.ProcessedTagName);
        AppendKv(sb, "AUTO_MODE_ENABLED", dto.AutoModeEnabled ? "true" : "false");
        sb.AppendLine();
        AppendKv(sb, "ENABLE_TAGS_ASSIGNMENT", dto.EnableTagsAssignment ? "true" : "false");
        AppendKv(sb, "ENABLE_CORRESPONDENT_DETECTION", dto.EnableCorrespondentDetection ? "true" : "false");
        AppendKv(sb, "ENABLE_DOCUMENT_TYPE_CLASSIFICATION", dto.EnableDocumentTypeClassification ? "true" : "false");
        AppendKv(sb, "ENABLE_TITLE_GENERATION", dto.EnableTitleGeneration ? "true" : "false");
        AppendKv(sb, "USE_EXISTING_CORRESPONDENTS_ONLY", dto.UseExistingCorrespondentsOnly ? "true" : "false");
        AppendKv(sb, "USE_EXISTING_DOCUMENT_TYPES_ONLY", dto.UseExistingDocumentTypesOnly ? "true" : "false");
        AppendKv(sb, "USE_EXISTING_TAGS_ONLY", dto.UseExistingTagsOnly ? "true" : "false");
        AppendKv(sb, "ENABLE_CUSTOM_FIELDS", dto.EnableCustomFields ? "true" : "false");
        sb.AppendLine();
        AppendKv(sb, "CUSTOM_SYSTEM_PROMPT", dto.CustomSystemPrompt ?? "");
        AppendKv(sb, "DEFAULT_TAG_VOCABULARY", dto.DefaultTagVocabulary ?? "");
        AppendKv(sb, "DEFAULT_DOCUMENT_TYPE_VOCABULARY", dto.DefaultDocumentTypeVocabulary ?? "");
        AppendKv(sb, "ENABLED_CUSTOM_FIELD_NAMES", dto.EnabledCustomFieldNames ?? "");
        sb.AppendLine();
        AppendKv(sb, "PREMIUM_LICENSE_KEY", dto.PremiumLicenseKey ?? "");
        AppendKv(sb, "TITLE_PARTS", dto.TitleParts ?? "");
        AppendKv(sb, "TITLE_SEPARATOR", dto.TitleSeparator ?? "");

        File.WriteAllText(_path, sb.ToString());
    }

    private static void AppendKv(StringBuilder sb, string key, string value)
    {
        sb.Append(key).Append('=').Append(EncodeValue(value)).Append('\n');
    }

    // Werte werden IMMER gequotet + escaped geschrieben (auch einfache), das macht
    // das Format robust gegenüber Leerzeichen, '#' im Wert und Mehrzeilern.
    private static string EncodeValue(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r\n", "\n")
            .Replace("\n", "\\n");
        return $"\"{escaped}\"";
    }

    private static Dictionary<string, string> ParseEnvFile(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var idx = line.IndexOf('=');
            if (idx < 0) continue;

            var key = line[..idx].Trim();
            var rawValue = line[(idx + 1)..].Trim();
            result[key] = DecodeValue(rawValue);
        }
        return result;
    }

    private static string DecodeValue(string rawValue)
    {
        if (rawValue.Length >= 2 && rawValue.StartsWith('"') && rawValue.EndsWith('"'))
        {
            var inner = rawValue[1..^1];
            return inner
                .Replace("\\n", "\n")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }
        return rawValue;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    private static int ParseInt(string? s, int fallback) => int.TryParse(s, out var v) ? v : fallback;
    private static double ParseDouble(string? s, double fallback) =>
        double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    private static bool ParseBool(string? s, bool fallback) => bool.TryParse(s, out var v) ? v : fallback;

    public async Task<string> ExportRawAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return File.Exists(_path) ? await File.ReadAllTextAsync(_path, ct) : "";
        }
        finally { _lock.Release(); }
    }

    public async Task ImportRawAsync(string content, CancellationToken ct = default)
    {
        // Validierung: muss mindestens PAPERLESS_URL enthalten
        if (!content.Contains("PAPERLESS_URL") && !content.Contains("LLM_PROVIDER"))
            throw new InvalidOperationException("Ungültige settings.env — PAPERLESS_URL oder LLM_PROVIDER fehlt.");

        await _lock.WaitAsync(ct);
        try
        {
            // Backup der aktuellen Datei
            if (File.Exists(_path))
                File.Copy(_path, _path + ".bak", overwrite: true);

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(_path, content, ct);
        }
        finally { _lock.Release(); }
    }
}
