using System.Net.Http.Json;
using Microsoft.JSInterop;
using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Web.Services;

/// <summary>Schlanker Wrapper um HttpClient-Aufrufe gegen die eigene Api (gleicher Origin).</summary>
public class ApiClient(HttpClient http)
{
    public async Task<SettingsDto> GetSettingsAsync() =>
        await http.GetFromJsonAsync<SettingsDto>("api/settings") ?? new SettingsDto();

    public async Task SaveSettingsAsync(SettingsDto dto) =>
        await http.PostAsJsonAsync("api/settings", dto);

    public async Task<ConnectionTestResult> TestPaperlessAsync(SettingsDto dto)
    {
        var resp = await http.PostAsJsonAsync("api/settings/test-paperless", dto);
        return await resp.Content.ReadFromJsonAsync<ConnectionTestResult>() ?? new ConnectionTestResult { Success = false, Message = "Keine Antwort." };
    }

    public async Task<ConnectionTestResult> TestLlmAsync(SettingsDto dto)
    {
        var resp = await http.PostAsJsonAsync("api/settings/test-llm", dto);
        return await resp.Content.ReadFromJsonAsync<ConnectionTestResult>() ?? new ConnectionTestResult { Success = false, Message = "Keine Antwort." };
    }

    public async Task<StatusDto> GetStatusAsync() =>
        await http.GetFromJsonAsync<StatusDto>("api/status") ?? new StatusDto();

    public async Task<List<ProcessedDocumentDto>> GetActivityAsync() =>
        await http.GetFromJsonAsync<List<ProcessedDocumentDto>>("api/status/activity") ?? new();

    public async Task<List<DocumentSummaryDto>> SearchDocumentsAsync(string? query)
    {
        var url = string.IsNullOrWhiteSpace(query) ? "api/documents" : $"api/documents?query={Uri.EscapeDataString(query)}";
        return await http.GetFromJsonAsync<List<DocumentSummaryDto>>(url) ?? new();
    }

    public async Task<DocumentDetailDto?> GetDocumentDetailAsync(int id)
    {
        var resp = await http.GetAsync($"api/documents/{id}/detail");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<DocumentDetailDto>();
    }

    public async Task<(bool Success, ProcessDocumentResultDto? Result, string? Error)> ProcessDocumentAsync(int id)
    {
        var resp = await http.PostAsync($"api/documents/{id}/process", null);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return (false, null, body);
        }
        var result = await resp.Content.ReadFromJsonAsync<ProcessDocumentResultDto>();
        return (true, result, null);
    }

    public async Task<(bool Success, string? Error)> ApplyDocumentAsync(int id, ProcessDocumentResultDto preview)
    {
        var resp = await http.PostAsJsonAsync($"api/documents/{id}/apply", preview);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return (false, body);
        }
        return (true, null);
    }

    public async Task<QueryResponse> QueryAsync(string query)
    {
        var resp = await http.PostAsJsonAsync("api/query", new QueryRequest(query));
        if (!resp.IsSuccessStatusCode)
        {
            var problem = await resp.Content.ReadAsStringAsync();
            return new QueryResponse { Answer = $"Fehler: {problem}" };
        }
        return await resp.Content.ReadFromJsonAsync<QueryResponse>() ?? new QueryResponse { Answer = "Keine Antwort." };
    }

    public async Task<DashboardDto> GetDashboardAsync(IJSRuntime? js = null)
    {
        // Wenn Splash-Screen die Daten bereits vorgeladen hat, sofort zurückgeben
        if (js is not null)
        {
            try
            {
                var cached = await js.InvokeAsync<string?>("getPaperLeoPreloaded", "dashboard");
                if (!string.IsNullOrEmpty(cached))
                {
                    var dto = System.Text.Json.JsonSerializer.Deserialize<DashboardDto>(cached,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (dto is not null) return dto;
                }
            }
            catch { /* Fallback: frisch laden */ }
        }
        return await http.GetFromJsonAsync<DashboardDto>("api/dashboard") ?? new DashboardDto();
    }

    public async Task<ScanNowResult> ScanNowAsync()
    {
        var resp = await http.PostAsync("api/dashboard/scan-now", null);
        return await resp.Content.ReadFromJsonAsync<ScanNowResult>() ?? new ScanNowResult { Success = false, Message = "Keine Antwort." };
    }

    public async Task<IndexResetResult> ResetIndexAsync()
    {
        var resp = await http.PostAsync("api/dashboard/reset-index", null);
        return await resp.Content.ReadFromJsonAsync<IndexResetResult>() ?? new IndexResetResult { Success = false, Message = "Keine Antwort." };
    }

    public async Task<FullResetResult> FullResetAsync()
    {
        var resp = await http.PostAsync("api/dashboard/full-reset", null);
        return await resp.Content.ReadFromJsonAsync<FullResetResult>() ?? new FullResetResult { Success = false, Message = "Keine Antwort." };
    }

    public async Task<DeleteAllResult> DeleteAllTagsAsync()
    {
        var resp = await http.PostAsync("api/dashboard/delete-all-tags", null);
        return await resp.Content.ReadFromJsonAsync<DeleteAllResult>() ?? new DeleteAllResult { Success = false, Message = "Keine Antwort." };
    }

    public async Task<DeleteAllResult> DeleteAllDocumentTypesAsync()
    {
        var resp = await http.PostAsync("api/dashboard/delete-all-document-types", null);
        return await resp.Content.ReadFromJsonAsync<DeleteAllResult>() ?? new DeleteAllResult { Success = false, Message = "Keine Antwort." };
    }

    public async Task<DeleteAllResult> DeleteAllCorrespondentsAsync()
    {
        var resp = await http.PostAsync("api/dashboard/delete-all-correspondents", null);
        return await resp.Content.ReadFromJsonAsync<DeleteAllResult>() ?? new DeleteAllResult { Success = false, Message = "Keine Antwort." };
    }

    public async Task<SeedTagsResult> SeedTagsAsync()
    {
        var resp = await http.PostAsync("api/dashboard/seed-tags", null);
        return await resp.Content.ReadFromJsonAsync<SeedTagsResult>() ?? new SeedTagsResult { Success = false, Message = "Keine Antwort." };
    }

    public async Task<SeedTagsResult> SeedDocumentTypesAsync()
    {
        var resp = await http.PostAsync("api/dashboard/seed-document-types", null);
        return await resp.Content.ReadFromJsonAsync<SeedTagsResult>() ?? new SeedTagsResult { Success = false, Message = "Keine Antwort." };
    }

    public async Task<SuggestVocabularyResult> SuggestTagsAsync()
    {
        var resp = await http.PostAsync("api/dashboard/suggest-tags", null);
        return await resp.Content.ReadFromJsonAsync<SuggestVocabularyResult>() ?? new SuggestVocabularyResult { Success = false, Message = "Keine Antwort." };
    }

    public async Task<List<CustomFieldDto>> GetCustomFieldsAsync()
    {
        var resp = await http.GetAsync("api/dashboard/custom-fields");
        return await resp.Content.ReadFromJsonAsync<List<CustomFieldDto>>() ?? new();
    }

    public async Task<(bool Success, string? Message)> CreateCustomFieldAsync(string name, string dataType)
    {
        var resp = await http.PostAsJsonAsync("api/dashboard/custom-fields", new { name, data_type = dataType });
        if (resp.IsSuccessStatusCode) return (true, null);
        var err = await resp.Content.ReadAsStringAsync();
        return (false, err);
    }

    /// <summary>
    /// Lädt alle Korrespondenten-Namen und führt die Duplikat-Erkennung
    /// LOKAL im Browser durch – kein API-Timeout, sofortiges Ergebnis.
    /// Nur der Merge-Schritt selbst geht danach ans Backend.
    /// </summary>
    public async Task<List<CorrespondentMergeSuggestion>> SuggestCorrespondentMergesAsync()
    {
        // Schritt 1: Nur Namen-Liste vom Backend holen
        var resp = await http.GetAsync("api/dashboard/correspondent-names");
        if (!resp.IsSuccessStatusCode) return new();
        var names = await resp.Content.ReadFromJsonAsync<List<string>>() ?? new();
        if (names.Count < 2) return new();

        // Schritt 2: Algorithmus läuft komplett im Frontend (kein zweiter API-Call)
        return CorrespondentDuplicateFinder.FindGroups(names);
    }

    public async Task<MergeResult> MergeCorrespondentsAsync(string primaryName, List<string> aliases)
    {
        var resp = await http.PostAsJsonAsync("api/dashboard/merge-correspondents", new { primaryName, aliases });
        return await resp.Content.ReadFromJsonAsync<MergeResult>() ?? new MergeResult { Success = false };
    }

    public async Task<BackfillResult> BackfillCustomFieldsAsync()
    {
        var resp = await http.PostAsync("api/dashboard/backfill-custom-fields", null);
        return await resp.Content.ReadFromJsonAsync<BackfillResult>() ?? new BackfillResult { Success = false, Message = "Keine Antwort." };
    }

    public async Task<BackfillProgress?> GetBackfillProgressAsync()
    {
        try
        {
            var resp = await http.GetAsync("api/dashboard/backfill-progress");
            return await resp.Content.ReadFromJsonAsync<BackfillProgress>();
        }
        catch { return null; }
    }

    public async Task<SuggestVocabularyResult> SuggestDocumentTypesAsync()
    {
        var resp = await http.PostAsync("api/dashboard/suggest-document-types", null);
        return await resp.Content.ReadFromJsonAsync<SuggestVocabularyResult>() ?? new SuggestVocabularyResult { Success = false, Message = "Keine Antwort." };
    }

    public async Task<(bool Allowed, List<LogEntryDto> Entries)> GetLogsAsync()
    {
        var resp = await http.GetAsync("api/dashboard/logs");
        if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return (false, new List<LogEntryDto>());
        }
        var entries = await resp.Content.ReadFromJsonAsync<List<LogEntryDto>>() ?? new();
        return (true, entries);
    }

    public async Task<List<ProcessingJobDto>> GetJobsAsync(string? filter = null, CancellationToken ct = default)
    {
        var url = string.IsNullOrEmpty(filter) || filter == "all"
            ? "api/dashboard/jobs"
            : $"api/dashboard/jobs?filter={filter}";
        var resp = await http.GetAsync(url, ct);
        return resp.IsSuccessStatusCode
            ? await resp.Content.ReadFromJsonAsync<List<ProcessingJobDto>>(cancellationToken: ct) ?? new()
            : new();
    }

    public async Task<(int queued, int running, int done, int failed)> GetJobCountsAsync(CancellationToken ct = default)
    {
        var resp = await http.GetAsync("api/dashboard/jobs/counts", ct);
        if (!resp.IsSuccessStatusCode) return (0,0,0,0);
        var obj = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: ct);
        return (
            obj.GetProperty("queued").GetInt32(),
            obj.GetProperty("running").GetInt32(),
            obj.GetProperty("done").GetInt32(),
            obj.GetProperty("failed").GetInt32()
        );
    }

    public async Task ClearJobsAsync(CancellationToken ct = default)
        => await http.DeleteAsync("api/dashboard/jobs", ct);


    public async Task<SuggestVocabularyResult> ImportTagsFromPaperlessAsync()
    {
        var resp = await http.PostAsync("api/dashboard/import-tags-from-paperless", null);
        return resp.IsSuccessStatusCode
            ? await resp.Content.ReadFromJsonAsync<SuggestVocabularyResult>() ?? new()
            : new() { Success = false, Message = $"HTTP {(int)resp.StatusCode}" };
    }

    public async Task<SuggestVocabularyResult> ImportDocTypesFromPaperlessAsync()
    {
        var resp = await http.PostAsync("api/dashboard/import-doctypes-from-paperless", null);
        return resp.IsSuccessStatusCode
            ? await resp.Content.ReadFromJsonAsync<SuggestVocabularyResult>() ?? new()
            : new() { Success = false, Message = $"HTTP {(int)resp.StatusCode}" };
    }

    public async Task<(bool Success, string Message)> ActivateLicenseAsync(string licenseKey)
    {
        try
        {
            var resp = await http.PostAsJsonAsync("api/license/activate", new { LicenseKey = licenseKey });
            var result = await resp.Content.ReadFromJsonAsync<LicenseResult>();
            return (result?.Success ?? false, result?.Message ?? "Unbekannter Fehler");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private record LicenseResult(bool Success, string? Message);
}