using Microsoft.AspNetCore.Mvc;
using PaperlessAiCore.Api.Services;
using PaperlessAiCore.Core;
using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController(
    ISettingsService settingsService,
    IHttpClientFactory httpFactory,
    IActivityLogService activityLog,
    IIngestScanService scanService,
    WorkerStatus status,
    InMemoryLogStore logStore,
    ProcessMetricsService processMetrics,
    BuildCounterService buildCounter,
    BackfillJobService backfillJob,
    ReviewQueueService reviewQueue,
    ProcessingJobService jobService,
    ILogger<DashboardController> log) : ControllerBase
{
    private static readonly (int Min, int Max, string Label)[] TokenBuckets =
    [
        (0, 1000, "0-1k"),
        (1000, 2000, "1k-2k"),
        (2000, 3000, "2k-3k"),
        (3000, 4000, "3k-4k"),
        (4000, 5000, "4k-5k"),
        (5000, int.MaxValue, "5k+"),
    ];

    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        var dto = new DashboardDto
        {
            AppVersion = AppInfo.Version,
            BuildNumber = buildCounter.BuildNumber,
            AutoModeEnabled = settings.AutoModeEnabled,
            LastPollAt = status.LastPollAt,
            LastError = status.LastError,
            ProMode = LicenseCheck.IsProMode(settings.PremiumLicenseKey),
            ProcessedSinceStart = status.ProcessedSinceStart,
            FreeDocsRemaining = 0, // wird unten nach Paperless-Abfrage gesetzt
            DemoDocsRemaining = 0,
        };

        // ---------- System-Health ("Dienstüberwachung") ----------
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.PaperlessApiToken))
        {
            dto.PaperlessHealth = "unknown";
            dto.PaperlessHealthMessage = "Nicht konfiguriert";
        }
        else
        {
            try
            {
                var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
                    new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));

                var allDocs = await paperless.ListDocumentsAsync(new() { ["page_size"] = "1" }, ct);
                dto.TotalDocuments = allDocs.Count;
                dto.PaperlessHealth = "ok";
                dto.PaperlessVersion = paperless.LastKnownServerVersion;

                var tags = await paperless.ListTagsAsync(ct);
                dto.TotalTags = tags.Count;
                var processedTag = tags.FirstOrDefault(t => string.Equals(t.Name, settings.ProcessedTagName, StringComparison.OrdinalIgnoreCase));

                if (processedTag is not null)
                {
                    var processedDocs = await paperless.ListDocumentsAsync(
                        new() { ["page_size"] = "1", ["tags__id__in"] = processedTag.Id.ToString() }, ct);
                    dto.ProcessedDocuments = processedDocs.Count;
                }
                dto.UnprocessedDocuments = Math.Max(dto.TotalDocuments - dto.ProcessedDocuments, 0);
                dto.FreeDocsRemaining = Math.Max(0, AppInfo.FreeDocumentLimit - dto.ProcessedDocuments);
                dto.DemoDocsRemaining = Math.Max(0, AppInfo.DemoDocumentLimit - dto.ProcessedDocuments);

                var correspondents = await paperless.ListCorrespondentsAsync(ct);
                dto.TotalCorrespondents = correspondents.Count;
            }
            catch (Exception ex)
            {
                dto.PaperlessHealth = "error";
                dto.PaperlessHealthMessage = ex.Message;
                log.LogWarning(ex, "Dashboard: Paperless-Health-Check fehlgeschlagen.");
            }
        }

        // LLM-Health wird NICHT live geprüft (würde bei jedem Dashboard-Aufruf Kosten
        // verursachen) - stattdessen aus dem letzten bekannten Scan-Status abgeleitet.
        if (string.IsNullOrWhiteSpace(settings.LlmApiKey))
        {
            dto.LlmHealth = "unknown";
            dto.LlmHealthMessage = "Nicht konfiguriert";
        }
        else if (status.LastError is { } err && err.Contains("LLM", StringComparison.OrdinalIgnoreCase))
        {
            dto.LlmHealth = "error";
            dto.LlmHealthMessage = err;
        }
        else
        {
            dto.LlmHealth = "ok";
        }

        // ---------- Token-Nutzung & Dokumenttyp-Verteilung (aus Activity-Log) ----------
        try
        {
            var activity = await activityLog.GetRecentAsync(5000, ct);
            var withTokens = activity.Where(a => a.TotalTokens is not null).ToList();

            dto.DocumentsWithTokenData = withTokens.Count;
            dto.TotalTokensUsed = withTokens.Sum(a => (long)(a.TotalTokens ?? 0));
            if (withTokens.Count > 0)
            {
                dto.AveragePromptTokens = withTokens.Average(a => a.PromptTokens ?? 0);
                dto.AverageCompletionTokens = withTokens.Average(a => a.CompletionTokens ?? 0);
                dto.AverageTotalTokens = withTokens.Average(a => a.TotalTokens ?? 0);
            }

            dto.TokenDistribution = TokenBuckets.Select(b => new TokenBucketDto
            {
                Label = b.Label,
                Count = withTokens.Count(a => a.TotalTokens >= b.Min && a.TotalTokens < b.Max),
            }).ToList();

            dto.DocumentTypeDistribution = activity
                .Where(a => !string.IsNullOrWhiteSpace(a.DocumentType))
                .GroupBy(a => a.DocumentType!)
                .Select(g => new DocumentTypeCountDto { Type = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(8)
                .ToList();

            var today = DateTime.UtcNow.Date;
            dto.ProcessedToday = activity.Count(a => a.ProcessedAt.Date == today);
            dto.LastProcessedAt = activity.OrderByDescending(a => a.ProcessedAt).FirstOrDefault()?.ProcessedAt;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Dashboard: Activity-Log konnte nicht gelesen werden.");
        }

        dto.IsIdle = status.LastError is null;

        try
        {
            dto.ResourceHistory = processMetrics.GetRecentSamples()
                .Select(s => new ResourceSampleDto
                {
                    Time = s.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                    CpuPercent = s.CpuPercent,
                    MemoryMb = s.MemoryMb,
                })
                .ToList();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Dashboard: Metriken konnten nicht gelesen werden.");
        }

        return Ok(dto);
    }

    /// <summary>Manueller Sofort-Scan: verarbeitet alle unverarbeiteten Dokumente jetzt.</summary>
    [HttpPost("scan-now")]
    public async Task<ActionResult<ScanNowResult>> ScanNow(CancellationToken ct)
    {
        try
        {
            var settings = await settingsService.GetAsync(ct);
            var count = await scanService.RunScanAsync(settings, ct);
            return Ok(new ScanNowResult { Success = true, ProcessedCount = count });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Manueller Scan fehlgeschlagen");
            return Ok(new ScanNowResult { Success = false, Message = ex.Message });
        }
    }

    /// <summary>
    /// Entfernt den Bookkeeping-Tag (z.B. "Klaus") von ALLEN Dokumenten, die ihn
    /// tragen - dadurch gelten sie für den Ingest-Worker/Scan wieder als
    /// "unverarbeitet" und werden beim nächsten Durchlauf komplett neu von der KI
    /// analysiert (Titel, Tags, Korrespondent, Dokumenttyp werden dann überschrieben).
    /// Löscht NICHT den OCR-Text oder das Dokument selbst - nur die eine Markierung,
    /// die unser System zur Fortschritts-Verfolgung nutzt.
    /// </summary>
    [HttpPost("reset-index")]
    public async Task<ActionResult<IndexResetResult>> ResetIndex(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.PaperlessApiToken))
        {
            return Ok(new IndexResetResult { Success = false, Message = "Bitte zuerst im Setup Paperless konfigurieren." });
        }

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));

        try
        {
            var tags = await paperless.ListTagsAsync(ct);
            var processedTag = tags.FirstOrDefault(t => string.Equals(t.Name, settings.ProcessedTagName, StringComparison.OrdinalIgnoreCase));
            if (processedTag is null)
            {
                return Ok(new IndexResetResult { Success = true, ResetCount = 0, Message = "Kein Dokument war als verarbeitet markiert - nichts zu tun." });
            }

            var resetCount = 0;
            var queryParams = new Dictionary<string, string> { ["page_size"] = "200", ["tags__id__in"] = processedTag.Id.ToString() };
            var docs = await paperless.ListDocumentsAsync(queryParams, ct);
            var safetyIterations = 0;

            while (docs.Results.Count > 0 && safetyIterations++ < 500)
            {
                foreach (var doc in docs.Results)
                {
                    ct.ThrowIfCancellationRequested();
                    var remainingTags = doc.Tags.Where(id => id != processedTag.Id).ToList();
                    await paperless.UpdateDocumentAsync(doc.Id, new { tags = remainingTags }, ct);
                    resetCount++;
                }

                // Nach dem Entfernen des Tags fällt jedes bearbeitete Dokument aus dem
                // "tags__id__in"-Filter raus - daher IMMER wieder Seite 1 derselben
                // Abfrage neu holen, statt der (jetzt verschobenen) "next"-Seite zu folgen.
                docs = await paperless.ListDocumentsAsync(queryParams, ct);
            }

            log.LogInformation("Index-Reset: {Count} Dokumente wieder als unverarbeitet markiert.", resetCount);
            return Ok(new IndexResetResult { Success = true, ResetCount = resetCount });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Index-Reset fehlgeschlagen");
            return Ok(new IndexResetResult { Success = false, Message = ex.Message });
        }
    }

    /// <summary>
    /// UNWIDERRUFLICH: Löscht ALLE Korrespondenten, ALLE Dokumenttypen und ALLE Tags
    /// im gesamten Paperless-System - nicht nur KI-generierte. Dokumente und ihr
    /// OCR-Text/Inhalt bleiben unangetastet, verlieren aber jede Zuordnung. Gedacht
    /// für einen kompletten Neustart der Verschlagwortung. Nur explizit nach
    /// ausdrücklicher Nutzer-Bestätigung aufrufen (siehe Frontend-Bestätigungsdialog).
    /// </summary>
    /// <summary>UNWIDERRUFLICH: Löscht ALLE Tags im gesamten Paperless-System.</summary>
    [HttpPost("delete-all-tags")]
    public async Task<ActionResult<DeleteAllResult>> DeleteAllTags(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.PaperlessApiToken))
        {
            return Ok(new DeleteAllResult { Success = false, Message = "Bitte zuerst im Setup Paperless konfigurieren." });
        }

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
        try
        {
            var tags = await paperless.ListTagsAsync(ct);
            foreach (var tag in tags)
            {
                ct.ThrowIfCancellationRequested();
                await paperless.DeleteTagAsync(tag.Id, ct);
            }
            log.LogWarning("Alle {Count} Tags gelöscht.", tags.Count);
            return Ok(new DeleteAllResult { Success = true, DeletedCount = tags.Count });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Löschen aller Tags fehlgeschlagen");
            return Ok(new DeleteAllResult { Success = false, Message = ex.Message });
        }
    }

    /// <summary>
    /// Legt alle Tags aus dem "Standard-Tag-Vokabular" (Settings) einmalig in Paperless
    /// an - idempotent, bereits existierende werden übersprungen. Sinnvoll direkt nach
    /// einem Tag-Reset, damit "Nur vorhandene Werte verwenden" wieder etwas zum Matchen hat.
    /// </summary>
    [HttpPost("seed-tags")]
    public async Task<ActionResult<SeedTagsResult>> SeedTags(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.PaperlessApiToken))
        {
            return Ok(new SeedTagsResult { Success = false, Message = "Bitte zuerst im Setup Paperless konfigurieren." });
        }
        if (string.IsNullOrWhiteSpace(settings.DefaultTagVocabulary))
        {
            return Ok(new SeedTagsResult { Success = false, Message = "Kein Standard-Tag-Vokabular in den Einstellungen hinterlegt." });
        }

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
        try
        {
            var wanted = settings.DefaultTagVocabulary.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var existing = await paperless.ListTagsAsync(ct);
            int created = 0, alreadyExisted = 0;

            foreach (var name in wanted)
            {
                ct.ThrowIfCancellationRequested();
                if (existing.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    alreadyExisted++;
                    continue;
                }
                await paperless.GetOrCreateTagAsync(name, ct);
                created++;
            }

            log.LogInformation("Tag-Vokabular angelegt: {Created} neu, {Existed} bereits vorhanden.", created, alreadyExisted);
            return Ok(new SeedTagsResult { Success = true, CreatedCount = created, AlreadyExistedCount = alreadyExisted });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Anlegen des Tag-Vokabulars fehlgeschlagen");
            return Ok(new SeedTagsResult { Success = false, Message = ex.Message });
        }
    }

    /// <summary>Wie SeedTags, aber für das "Standard-Dokumenttyp-Vokabular".</summary>
    [HttpPost("seed-document-types")]
    public async Task<ActionResult<SeedTagsResult>> SeedDocumentTypes(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.PaperlessApiToken))
        {
            return Ok(new SeedTagsResult { Success = false, Message = "Bitte zuerst im Setup Paperless konfigurieren." });
        }
        if (string.IsNullOrWhiteSpace(settings.DefaultDocumentTypeVocabulary))
        {
            return Ok(new SeedTagsResult { Success = false, Message = "Kein Standard-Dokumenttyp-Vokabular in den Einstellungen hinterlegt." });
        }

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
        try
        {
            var wanted = settings.DefaultDocumentTypeVocabulary.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var existing = await paperless.ListDocumentTypesAsync(ct);
            int created = 0, alreadyExisted = 0;

            foreach (var name in wanted)
            {
                ct.ThrowIfCancellationRequested();
                if (existing.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    alreadyExisted++;
                    continue;
                }
                await paperless.GetOrCreateDocumentTypeAsync(name, ct);
                created++;
            }

            log.LogInformation("Dokumenttyp-Vokabular angelegt: {Created} neu, {Existed} bereits vorhanden.", created, alreadyExisted);
            return Ok(new SeedTagsResult { Success = true, CreatedCount = created, AlreadyExistedCount = alreadyExisted });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Anlegen des Dokumenttyp-Vokabulars fehlgeschlagen");
            return Ok(new SeedTagsResult { Success = false, Message = ex.Message });
        }
    }

    /// <summary>UNWIDERRUFLICH: Löscht ALLE Dokumenttypen im gesamten Paperless-System.</summary>
    [HttpPost("delete-all-document-types")]
    public async Task<ActionResult<DeleteAllResult>> DeleteAllDocumentTypes(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.PaperlessApiToken))
        {
            return Ok(new DeleteAllResult { Success = false, Message = "Bitte zuerst im Setup Paperless konfigurieren." });
        }

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
        try
        {
            var docTypes = await paperless.ListDocumentTypesAsync(ct);
            foreach (var t in docTypes)
            {
                ct.ThrowIfCancellationRequested();
                await paperless.DeleteDocumentTypeAsync(t.Id, ct);
            }
            log.LogWarning("Alle {Count} Dokumenttypen gelöscht.", docTypes.Count);
            return Ok(new DeleteAllResult { Success = true, DeletedCount = docTypes.Count });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Löschen aller Dokumenttypen fehlgeschlagen");
            return Ok(new DeleteAllResult { Success = false, Message = ex.Message });
        }
    }

    /// <summary>UNWIDERRUFLICH: Löscht ALLE Korrespondenten im gesamten Paperless-System.</summary>
    [HttpPost("delete-all-correspondents")]
    public async Task<ActionResult<DeleteAllResult>> DeleteAllCorrespondents(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.PaperlessApiToken))
        {
            return Ok(new DeleteAllResult { Success = false, Message = "Bitte zuerst im Setup Paperless konfigurieren." });
        }

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
        try
        {
            var correspondents = await paperless.ListCorrespondentsAsync(ct);
            foreach (var c in correspondents)
            {
                ct.ThrowIfCancellationRequested();
                await paperless.DeleteCorrespondentAsync(c.Id, ct);
            }
            log.LogWarning("Alle {Count} Korrespondenten gelöscht.", correspondents.Count);
            return Ok(new DeleteAllResult { Success = true, DeletedCount = correspondents.Count });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Löschen aller Korrespondenten fehlgeschlagen");
            return Ok(new DeleteAllResult { Success = false, Message = ex.Message });
        }
    }

    [HttpPost("full-reset")]
    public async Task<ActionResult<FullResetResult>> FullReset(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.PaperlessApiToken))
        {
            return Ok(new FullResetResult { Success = false, Message = "Bitte zuerst im Setup Paperless konfigurieren." });
        }

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));

        try
        {
            var correspondents = await paperless.ListCorrespondentsAsync(ct);
            foreach (var c in correspondents)
            {
                ct.ThrowIfCancellationRequested();
                await paperless.DeleteCorrespondentAsync(c.Id, ct);
            }

            var docTypes = await paperless.ListDocumentTypesAsync(ct);
            foreach (var t in docTypes)
            {
                ct.ThrowIfCancellationRequested();
                await paperless.DeleteDocumentTypeAsync(t.Id, ct);
            }

            var tags = await paperless.ListTagsAsync(ct);
            foreach (var tag in tags)
            {
                ct.ThrowIfCancellationRequested();
                await paperless.DeleteTagAsync(tag.Id, ct);
            }

            log.LogWarning(
                "VOLLSTÄNDIGER RESET durchgeführt: {C} Korrespondenten, {D} Dokumenttypen, {T} Tags gelöscht.",
                correspondents.Count, docTypes.Count, tags.Count);

            return Ok(new FullResetResult
            {
                Success = true,
                CorrespondentsDeleted = correspondents.Count,
                DocumentTypesDeleted = docTypes.Count,
                TagsDeleted = tags.Count,
            });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Vollständiger Reset fehlgeschlagen");
            return Ok(new FullResetResult { Success = false, Message = ex.Message });
        }
    }


    // ============================================================
    // REVIEW QUEUE
    // ============================================================

    [HttpGet("review-queue")]
    public async Task<ActionResult<List<ReviewQueueEntry>>> GetReviewQueue(CancellationToken ct)
        => Ok(await reviewQueue.GetPendingAsync(ct));

    [HttpGet("review-queue/count")]
    public async Task<ActionResult<int>> GetReviewQueueCount(CancellationToken ct)
        => Ok(await reviewQueue.GetPendingCountAsync(ct));

    [HttpPost("review-queue/{documentId:int}/done")]
    public async Task<IActionResult> MarkReviewed(int documentId, CancellationToken ct)
    {
        await reviewQueue.MarkReviewedAsync(documentId, ct);
        return Ok();
    }

    [HttpDelete("review-queue")]
    public async Task<IActionResult> ClearReviewQueue(CancellationToken ct)
    {
        await reviewQueue.ClearAsync(ct);
        return Ok();
    }

    // ============================================================
    // JOB QUEUE – Aktuelle Aufgaben
    // ============================================================

    [HttpGet("jobs")]
    public ActionResult<List<ProcessingJobDto>> GetJobs([FromQuery] string? filter = null)
        => Ok(jobService.GetAll(filter));

    [HttpGet("jobs/counts")]
    public ActionResult<object> GetJobCounts()
    {
        var (queued, running, done, failed) = jobService.GetCounts();
        return Ok(new { queued, running, done, failed });
    }

    [HttpDelete("jobs")]
    public IActionResult ClearJobs() { jobService.Clear(); return Ok(); }

    // ============================================================
    // HARD RESET (erweiterter Full-Reset inkl. Custom Fields + Log-Dateien)
    // ============================================================

    [HttpPost("hard-reset")]
    public async Task<ActionResult<FullResetResult>> HardReset(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl))
            return Ok(new FullResetResult { Success = false, Message = "Paperless nicht konfiguriert." });

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));

        var result = new FullResetResult();
        try
        {
            // 1. Paperless-Metadaten löschen
            var correspondents = await paperless.ListCorrespondentsAsync(ct);
            foreach (var c in correspondents) { ct.ThrowIfCancellationRequested(); await paperless.DeleteCorrespondentAsync(c.Id, ct); }
            result.CorrespondentsDeleted = correspondents.Count;

            var docTypes = await paperless.ListDocumentTypesAsync(ct);
            foreach (var t in docTypes) { ct.ThrowIfCancellationRequested(); await paperless.DeleteDocumentTypeAsync(t.Id, ct); }
            result.DocumentTypesDeleted = docTypes.Count;

            var tags = await paperless.ListTagsAsync(ct);
            foreach (var tag in tags) { ct.ThrowIfCancellationRequested(); await paperless.DeleteTagAsync(tag.Id, ct); }
            result.TagsDeleted = tags.Count;

            // 2. Custom Fields löschen
            try
            {
                var cfResp = await httpFactory.CreateClient("paperless")
                    .GetAsync($"{settings.PaperlessUrl.TrimEnd('/')}/api/custom_fields/?page_size=200", ct);
                if (cfResp.IsSuccessStatusCode)
                {
                    var cfJson = await cfResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: ct);
                    if (cfJson.TryGetProperty("results", out var cfResults))
                        foreach (var cf in cfResults.EnumerateArray())
                        {
                            var cfId = cf.GetProperty("id").GetInt32();
                            await httpFactory.CreateClient("paperless")
                                .DeleteAsync($"{settings.PaperlessUrl.TrimEnd('/')}/api/custom_fields/{cfId}/", ct);
                        }
                }
            }
            catch (Exception ex) { log.LogWarning("Custom-Fields-Reset fehlgeschlagen: {E}", ex.Message); }

            // 3. Lokale paperLeo-Daten löschen (Logs, Review-Queue, Vocabulary)
            var dataFiles = new[] { "data/activity.jsonl", "data/paperless-writes.log", "data/review-queue.jsonl",
                                    "data/tag-vocabulary.txt", "data/doctype-vocabulary.txt" };
            foreach (var f in dataFiles)
                if (System.IO.File.Exists(f)) { System.IO.File.Delete(f); log.LogWarning("Gelöscht: {F}", f); }

            await reviewQueue.ClearAsync(ct);

            result.Success = true;
            log.LogWarning("HARD RESET: {C} Korrespondenten, {D} Typen, {T} Tags + alle lokalen Daten gelöscht.",
                result.CorrespondentsDeleted, result.DocumentTypesDeleted, result.TagsDeleted);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Hard Reset fehlgeschlagen");
            result.Success = false; result.Message = ex.Message;
        }
        return Ok(result);
    }

    /// <summary>
    /// Log-Viewer. TEMPORÄR für alle freigegeben (auch Community), damit du beim
    /// Debuggen der Schreib-Probleme live mitlesen kannst, ohne einen Lizenzschlüssel
    /// zu brauchen. Die PRO-Prüfung steht nur auskommentiert daneben - sag Bescheid,
    /// wenn sie wieder scharf geschaltet werden soll.
    /// </summary>
    [HttpGet("logs")]
    public async Task<ActionResult<List<LogEntryDto>>> GetLogs(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        // if (!LicenseCheck.IsProMode(settings.PremiumLicenseKey))
        // {
        //     return StatusCode(403, new { error = "Log-Viewer ist ein PRO-Feature. Bitte Lizenzschlüssel in den Einstellungen hinterlegen." });
        // }
        _ = settings; // aktuell nur noch für die (deaktivierte) PRO-Prüfung gebraucht

        var entries = logStore.GetRecent(200)
            .Select(e => new LogEntryDto { Timestamp = e.Timestamp, Level = e.Level, Category = e.Category, Message = e.Message })
            .ToList();

        return Ok(entries);
    }

    /// <summary>
    /// Analysiert eine Stichprobe der zuletzt erstellten Dokumente und lässt die KI eine
    /// sinnvolle Tag-Struktur vorschlagen. Landet NUR im Antwort-Feld (Frontend füllt das
    /// Textfeld damit) - wird NICHT automatisch gespeichert oder in Paperless angelegt.
    /// </summary>
    [HttpGet("custom-fields")]
    public async Task<ActionResult<List<CustomFieldDto>>> GetCustomFields(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl))
            return Ok(new List<CustomFieldDto>());

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
        var fields = await paperless.ListCustomFieldsAsync(ct);
        return Ok(fields.Select(f => new CustomFieldDto { Id = f.Id, Name = f.Name, DataType = f.DataType }).ToList());
    }

    [HttpPost("custom-fields")]
    public async Task<ActionResult> CreateCustomField([FromBody] CreateCustomFieldRequest req, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl))
            return BadRequest("Paperless nicht konfiguriert.");

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
        try
        {
            await paperless.FindOrCreateCustomFieldAsync(req.Name, ct);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("suggest-tags")]
    public async Task<ActionResult<SuggestVocabularyResult>> SuggestTags(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.LlmApiKey))
        {
            return Ok(new SuggestVocabularyResult { Success = false, Message = "Bitte zuerst Paperless und den LLM-Provider konfigurieren." });
        }

        try
        {
            var (sampleText, count) = await BuildDocumentSampleAsync(settings, ct);
            if (count == 0)
            {
                return Ok(new SuggestVocabularyResult { Success = false, Message = "Keine Dokumente zum Analysieren gefunden." });
            }

            var llm = new LlmClient(httpFactory.CreateClient("llm"), settings.ToLlmConfig());
            var prompt = $"""
                Hier sind Auszüge aus {count} Dokumenten aus einem persönlichen Archiv:

                {sampleText}

                Schlage eine Liste von 20-40 sinnvollen, THEMATISCHEN Tags vor, die dieses Archiv gut
                abdecken würden (breite, wiederverwendbare Kategorien wie "Strom", "Versicherung",
                "Steuer", "Wartung" - keine Dubletten, keine zu spezifischen Einzelfälle wie einzelne
                Rechnungsnummern). Antworte AUSSCHLIESSLICH mit einer komma-getrennten Liste, ohne
                Erklärung, ohne Nummerierung, ohne Anführungszeichen.
                """;

            var result = await llm.ChatAsync(new List<LlmChatMessage> { new() { Role = "user", Content = prompt } }, null, ct);
            return Ok(new SuggestVocabularyResult { Success = true, SuggestedVocabulary = (result.Content ?? "").Trim(), DocumentsAnalyzed = count });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "KI-Tag-Vorschlag fehlgeschlagen");
            return Ok(new SuggestVocabularyResult { Success = false, Message = ex.Message });
        }
    }

    /// <summary>Wie SuggestTags, aber für Dokumenttypen.</summary>
    [HttpPost("suggest-document-types")]
    public async Task<ActionResult<SuggestVocabularyResult>> SuggestDocumentTypes(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.LlmApiKey))
        {
            return Ok(new SuggestVocabularyResult { Success = false, Message = "Bitte zuerst Paperless und den LLM-Provider konfigurieren." });
        }

        try
        {
            var (sampleText, count) = await BuildDocumentSampleAsync(settings, ct);
            if (count == 0)
            {
                return Ok(new SuggestVocabularyResult { Success = false, Message = "Keine Dokumente zum Analysieren gefunden." });
            }

            var llm = new LlmClient(httpFactory.CreateClient("llm"), settings.ToLlmConfig());
            var prompt = $"""
                Hier sind Auszüge aus {count} Dokumenten aus einem persönlichen Archiv:

                {sampleText}

                Schlage eine Liste von 10-20 sinnvollen DOKUMENTTYPEN vor, die dieses Archiv gut
                abdecken würden (breite Kategorien wie "Rechnung", "Vertrag", "Mahnung", "Kontoauszug" -
                keine Dubletten, keine zu spezifischen Einzelfälle). Antworte AUSSCHLIESSLICH mit einer
                komma-getrennten Liste, ohne Erklärung, ohne Nummerierung, ohne Anführungszeichen.
                """;

            var result = await llm.ChatAsync(new List<LlmChatMessage> { new() { Role = "user", Content = prompt } }, null, ct);
            return Ok(new SuggestVocabularyResult { Success = true, SuggestedVocabulary = (result.Content ?? "").Trim(), DocumentsAnalyzed = count });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "KI-Dokumenttyp-Vorschlag fehlgeschlagen");
            return Ok(new SuggestVocabularyResult { Success = false, Message = ex.Message });
        }
    }

    [HttpPost("import-tags-from-paperless")]
    public async Task<ActionResult<SuggestVocabularyResult>> ImportTagsFromPaperless(CancellationToken ct)
    {
        try
        {
            var settings = await settingsService.GetAsync(ct);
            if (!settings.IsConfigured)
                return Ok(new SuggestVocabularyResult { Success = false, Message = "Bitte zuerst Paperless konfigurieren." });
            var paperless = new PaperlessAiCore.Core.PaperlessClient(
                httpFactory.CreateClient("paperless"),
                new PaperlessAiCore.Core.PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
            var tags = await paperless.ListTagsAsync(ct);
            var names = tags
                .Where(t => !string.Equals(t.Name, settings.ProcessedTagName, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Name)
                .OrderBy(n => n)
                .ToList();
            var vocab = string.Join(", ", names);
            // Vocabulary speichern
            settings.DefaultTagVocabulary = vocab;
            await settingsService.SaveAsync(settings, ct);
            return Ok(new SuggestVocabularyResult { Success = true, SuggestedVocabulary = vocab, DocumentsAnalyzed = names.Count });
        }
        catch (Exception ex) { return Ok(new SuggestVocabularyResult { Success = false, Message = ex.Message }); }
    }

    [HttpPost("import-doctypes-from-paperless")]
    public async Task<ActionResult<SuggestVocabularyResult>> ImportDocTypesFromPaperless(CancellationToken ct)
    {
        try
        {
            var settings = await settingsService.GetAsync(ct);
            if (!settings.IsConfigured)
                return Ok(new SuggestVocabularyResult { Success = false, Message = "Bitte zuerst Paperless konfigurieren." });
            var paperless = new PaperlessAiCore.Core.PaperlessClient(
                httpFactory.CreateClient("paperless"),
                new PaperlessAiCore.Core.PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
            var types = await paperless.ListDocumentTypesAsync(ct);
            var names = types.Select(t => t.Name).OrderBy(n => n).ToList();
            var vocab = string.Join(", ", names);
            settings.DefaultDocumentTypeVocabulary = vocab;
            await settingsService.SaveAsync(settings, ct);
            return Ok(new SuggestVocabularyResult { Success = true, SuggestedVocabulary = vocab, DocumentsAnalyzed = names.Count });
        }
        catch (Exception ex) { return Ok(new SuggestVocabularyResult { Success = false, Message = ex.Message }); }
    }

    /// <summary>
    /// Findet Korrespondenten-Dubletten rein algorithmisch (ohne KI).
    /// Gruppiert Namen nach Normalisierung: Rechtsformen entfernen, Groß-/Kleinschreibung
    /// ignorieren, Whitespace normalisieren – dann Präfix/Substring/Token-Match.
    /// </summary>
    [HttpGet("correspondent-names")]
    public async Task<ActionResult<List<string>>> GetCorrespondentNames(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl)) return Ok(new List<string>());
        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
        var list = await paperless.ListCorrespondentsAsync(ct);
        return Ok(list.Select(c => c.Name).ToList());
    }

        [HttpGet("suggest-correspondent-merges")]
    public async Task<ActionResult<List<CorrespondentMergeSuggestion>>> SuggestCorrespondentMerges(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl))
            return Ok(new List<CorrespondentMergeSuggestion>());

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
        var correspondents = await paperless.ListCorrespondentsAsync(ct);

        if (correspondents.Count < 2)
            return Ok(new List<CorrespondentMergeSuggestion>());

        var names = correspondents.Select(c => c.Name).ToList();

        // STUFE 1+2: Algorithmusbasierte Vorfilterung (alle 3 Level)
        var groups = FindSimilarCorrespondentGroups(names);

        // STUFE 3: KI verfeinert und validiert die Kandidatengruppen (optional, nur wenn LLM konfiguriert)
        List<CorrespondentMergeSuggestion> suggestions;
        if (!string.IsNullOrWhiteSpace(settings.LlmApiKey))
        {
            suggestions = await RefineWithLlmAsync(groups, settings, ct);
        }
        else
        {
            // Ohne LLM: Level-1 und Level-2 direkt zurückgeben
            suggestions = groups.Select(g =>
            {
                var primary = g.OrderBy(n => NormalizeCorrespondent(n).Length).ThenBy(n => n).First();
                var aliases = g.Where(n => !string.Equals(n, primary, StringComparison.OrdinalIgnoreCase)).ToList();
                return new CorrespondentMergeSuggestion { PrimaryName = primary, Aliases = aliases, AllNames = g, Level = 2 };
            })
            .Where(s => s.Aliases.Count > 0)
            .ToList();
        }

        return Ok(suggestions);
    }

    private async Task<List<CorrespondentMergeSuggestion>> RefineWithLlmAsync(
        List<List<string>> groups, SettingsDto settings, CancellationToken ct)
    {
        if (groups.Count == 0) return new();

        var llm = new LlmClient(httpFactory.CreateClient("llm"), settings.ToLlmConfig());
        var result = new List<CorrespondentMergeSuggestion>();

        // Max 8 Gruppen pro KI-Call → verhindert JSON-Truncation
        const int batchSize = 8;
        for (int batchStart = 0; batchStart < groups.Count; batchStart += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = groups.Skip(batchStart).Take(batchSize).ToList();
            var groupText = string.Join("\n", batch.Select((g, i) =>
                $"[{i}] {string.Join(" | ", g)}"));

            var prompt = $$"""
                Bewerte diese Korrespondenten-Gruppen mit Konfidenz-Level.
                Antworte AUSSCHLIESSLICH mit dem JSON-Array, KEIN Text davor oder danach.

                Gruppen:
                {{groupText}}

                Level: 1=sicher (Rechtsform/Tipp/Groß-Klein), 2=wahrscheinlich (Abkürzung/Zusatz), 3=möglich, -1=verschiedene Org. (weglassen)

                JSON (kompakt, eine Zeile):
                [{"idx":0,"level":1},{"idx":1,"level":2}]
                """;

            try
            {
                var llmResult = await llm.ChatAsync(
                    new List<LlmChatMessage> { new() { Role = "user", Content = prompt } }, null, ct);
                var raw = (llmResult.Content ?? "").Trim();
                // JSON extrahieren
                var s = raw.IndexOf('[');
                var e = raw.LastIndexOf(']');
                if (s < 0 || e <= s) throw new Exception("Kein JSON-Array");
                raw = raw[s..(e + 1)];

                var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var ratings = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(raw, opts) ?? new();

                foreach (var rating in ratings)
                {
                    if (!rating.TryGetProperty("idx", out var idxEl)) continue;
                    if (!rating.TryGetProperty("level", out var levelEl)) continue;
                    int idx = idxEl.GetInt32();
                    int level = levelEl.GetInt32();
                    if (level < 1 || idx < 0 || idx >= batch.Count) continue;

                    var group = batch[idx];
                    var primary = group.OrderBy(n => NormalizeCorrespondent(n).Length).ThenBy(n => n).First();
                    var aliases = group.Where(n => !string.Equals(n, primary, StringComparison.OrdinalIgnoreCase)).ToList();
                    result.Add(new CorrespondentMergeSuggestion
                        { PrimaryName = primary, Aliases = aliases, AllNames = group, Level = level });
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("KI-Batch fehlgeschlagen, nutze Algorithmus-Level: {Msg}", ex.Message);
                foreach (var group in batch)
                {
                    var primary = group.OrderBy(n => NormalizeCorrespondent(n).Length).ThenBy(n => n).First();
                    var aliases = group.Where(n => !string.Equals(n, primary, StringComparison.OrdinalIgnoreCase)).ToList();
                    result.Add(new CorrespondentMergeSuggestion
                        { PrimaryName = primary, Aliases = aliases, AllNames = group, Level = 2 });
                }
            }
        }

        return result.OrderBy(s => s.Level).ToList();
    }

    /// <summary>Führt eine bestätigte Korrespondenten-Zusammenlegung durch.</summary>
    [HttpPost("merge-correspondents")]
    public async Task<ActionResult<MergeResult>> MergeCorrespondents(
        [FromBody] MergeCorrespondentsRequest req, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl))
            return BadRequest("Paperless nicht konfiguriert.");

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));

        var correspondents = await paperless.ListCorrespondentsAsync(ct);
        var primary = correspondents.FirstOrDefault(c =>
            string.Equals(c.Name, req.PrimaryName, StringComparison.OrdinalIgnoreCase));

        if (primary is null)
        {
            var primaryId = await paperless.GetOrCreateCorrespondentAsync(req.PrimaryName, ct);
            primary = new PaperlessCorrespondent { Id = primaryId, Name = req.PrimaryName };
        }

        int reassigned = 0, deleted = 0;
        foreach (var aliasName in req.Aliases)
        {
            ct.ThrowIfCancellationRequested();
            var alias = correspondents.FirstOrDefault(c =>
                string.Equals(c.Name, aliasName, StringComparison.OrdinalIgnoreCase));
            if (alias is null || alias.Id == primary.Id) continue;

            // Alle Dokumente des Alias-Korrespondenten auf Primary umschreiben
            int page = 1;
            while (true)
            {
                var docs = await paperless.ListDocumentsAsync(
                    new() { ["correspondent__id"] = alias.Id.ToString(),
                            ["page_size"] = "100", ["page"] = page.ToString() }, ct);
                foreach (var doc in docs.Results)
                {
                    await paperless.UpdateDocumentAsync(doc.Id, new { correspondent = primary.Id }, ct);
                    reassigned++;
                }
                if (docs.Next is null) break;
                page++;
            }

            await paperless.DeleteCorrespondentAsync(alias.Id, ct);
            deleted++;
            log.LogInformation("Korrespondent zusammengelegt: {Alias} → {Primary} ({Docs} Dok.)",
                aliasName, req.PrimaryName, reassigned);
        }

        return Ok(new MergeResult { Success = true, DocumentsReassigned = reassigned, AliasesDeleted = deleted });
    }

    /// <summary>
    /// Rein algorithmische Dublettensuche ohne KI.
    /// Normalisiert alle Namen (Rechtsformen weg, case-insensitive, Whitespace)
    /// und gruppiert dann nach 3 Kriterien:
    ///   1. Einer ist Präfix des anderen → gleicher Stamm, Zusatz ignorierbar
    ///   2. Einer ist Substring des anderen → Kurzname enthalten im Langnamen
    ///   3. Token-Überlappung ≥ 60% → gleiche Wörter, andere Reihenfolge/Zusätze
    /// </summary>

    private static readonly Dictionary<string, string[]> KnownAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FH"]  = ["FACHHOCHSCHULE"],
        ["HS"]  = ["HOCHSCHULE"],
        ["UNI"] = ["UNIVERSITAT", "UNIVERSITAET"],
        ["TH"]  = ["TECHNISCHE HOCHSCHULE"],
        ["TU"]  = ["TECHNISCHE UNIVERSITAT"],
        ["HWK"] = ["HANDWERKSKAMMER"],
        ["IHK"] = ["INDUSTRIE UND HANDELSKAMMER"],
        ["VHS"] = ["VOLKSHOCHSCHULE"],
        ["BG"]  = ["BERUFSGENOSSENSCHAFT"],
        ["PKV"] = ["PRIVATE KRANKENVERSICHERUNG"],
    };

    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "AM","AN","AUF","AUS","BEI","DER","DIE","DES","FUR","FUER",
        "IM","IN","MIT","NACH","UND","VOM","VON","ZU","ZUM","ZUR",
        // Häufige Ortsname-Tokens die alleine nicht bedeutungstragend sind
        "BONN","KOELN","BERLIN","HAMBURG","MUNCHEN","FRANKFURT","RHEIN","SIEG",
        // Verwaltungsbegriffe
        "KREIS","LANDKREIS","STADTKREIS",
    };

    private static List<List<string>> FindSimilarCorrespondentGroups(List<string> names)
    {
        var entries = names.Select(n => (Original: n, Norm: NormalizeCorrespondent(n))).ToList();
        var groups = new List<List<string>>();
        var assigned = new HashSet<int>();

        for (int i = 0; i < entries.Count; i++)
        {
            if (assigned.Contains(i)) continue;
            var (origI, normI) = entries[i];
            if (normI.Length < 2) continue;

            var group = new List<string> { origI };

            for (int j = i + 1; j < entries.Count; j++)
            {
                if (assigned.Contains(j)) continue;
                var (origJ, normJ) = entries[j];
                if (normJ.Length < 2) continue;

                if (AreSimilarCorrespondents(normI, normJ))
                {
                    group.Add(origJ);
                    assigned.Add(j);
                }
            }

            if (group.Count > 1)
            {
                assigned.Add(i);
                groups.Add(group);
            }
        }
        return groups;
    }

    private static bool AreSimilarCorrespondents(string na, string nb)
    {
        if (string.Equals(na, nb, StringComparison.OrdinalIgnoreCase)) return true;

        var ta = CorrespondentTokens(na);
        var tb = CorrespondentTokens(nb);

        // Regel 1: Wort-Präfix – Tokenliste der kürzeren ist Anfang der längeren
        // "TELEKOM" → "TELEKOM DEUTSCHLAND" ✅  "BONN" → "MARATHON BONN" ❌
        if (ta.Count > 0 && tb.Count > 0)
        {
            var (shorter, longer) = ta.Count <= tb.Count ? (ta, tb) : (tb, ta);
            if (shorter.SequenceEqual(longer.Take(shorter.Count), StringComparer.OrdinalIgnoreCase))
            {
                var meaningful = shorter.Any(t => !GenericWords.Contains(t));
                if (meaningful) return true;
            }
        }

        // Regel 2: Gleiche Struktur, erster Token ist Abkürzung/Variante
        // "FH BONN RHEIN SIEG" ↔ "FACHHOCHSCHULE BONN RHEIN SIEG"
        if (ta.Count > 1 && tb.Count > 1)
        {
            int commonSuffix = 0;
            for (int k = 1; k <= Math.Min(ta.Count, tb.Count); k++)
            {
                if (string.Equals(ta[ta.Count - k], tb[tb.Count - k], StringComparison.OrdinalIgnoreCase))
                    commonSuffix++;
                else break;
            }
            if (commonSuffix >= 1)
            {
                var restA = ta.Take(ta.Count - commonSuffix).ToList();
                var restB = tb.Take(tb.Count - commonSuffix).ToList();
                if (restA.Count == 1 && restB.Count == 1 && TokensSimilar(restA[0], restB[0]))
                    return true;
            }
        }

        // Regel 3: Bedeutungsvolle Token-Überlappung ≥ 60%
        if (ta.Count > 0 && tb.Count > 0)
        {
            var sigA = ta.Where(t => !GenericWords.Contains(t)).ToList();
            var sigB = tb.Where(t => !GenericWords.Contains(t)).ToList();
            if (sigA.Count >= 2 && sigB.Count >= 2)
            {
                int overlap = sigA.Count(t => sigB.Any(x => TokensSimilar(t, x)));
                int minSig = Math.Min(sigA.Count, sigB.Count);
                if (minSig > 0 && overlap * 100 / minSig >= 60) return true;
            }
        }

        // Regel 4: Levenshtein bei kurzen Namen (Tippfehler, Umlaute)
        if (na.Length <= 25 && nb.Length <= 25)
        {
            int dist = Levenshtein(na, nb);
            int maxLen = Math.Max(na.Length, nb.Length);
            if (maxLen > 0 && dist * 100 / maxLen <= 15) return true;
        }

        return false;
    }

    private static bool TokensSimilar(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        // Bekannte Abkürzungen (FH = Fachhochschule)
        if (KnownAbbreviations.TryGetValue(a, out var expansionsA) &&
            expansionsA.Any(e => b.StartsWith(e, StringComparison.OrdinalIgnoreCase))) return true;
        if (KnownAbbreviations.TryGetValue(b, out var expansionsB) &&
            expansionsB.Any(e => a.StartsWith(e, StringComparison.OrdinalIgnoreCase))) return true;
        // Endungs-Match: "HOCHSCHULE" ist Endung von "FACHHOCHSCHULE"
        if (a.Length >= 5 && b.EndsWith(a, StringComparison.OrdinalIgnoreCase)) return true;
        if (b.Length >= 5 && a.EndsWith(b, StringComparison.OrdinalIgnoreCase)) return true;
        // Levenshtein für Token-Varianten
        if (a.Length <= 12 && b.Length <= 12)
        {
            int dist = Levenshtein(a, b);
            if (dist * 100 / Math.Max(a.Length, b.Length) <= 20) return true;
        }
        return false;
    }

    private static string NormalizeCorrespondent(string name)
    {
        // Umlaute normalisieren für Vergleiche
        var n = name.Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss")
                    .Replace("Ä", "Ae").Replace("Ö", "Oe").Replace("Ü", "Ue").Trim();
        // Rechtsformen entfernen
        var legalForms = new[] {
            " GmbH & Co. KG", " GmbH & Co KG", " GmbH", " AG", " KG",
            " e.V.", " e. V.", " eV", " SE", " Ltd.", " Ltd", " Inc.", " Inc", " GbR", " mbH"
        };
        foreach (var form in legalForms)
            if (n.EndsWith(form, StringComparison.OrdinalIgnoreCase))
                n = n[..^form.Length].TrimEnd();
        // Sonderzeichen normalisieren
        n = System.Text.RegularExpressions.Regex.Replace(n, @"[.\-_/&]", " ");
        n = System.Text.RegularExpressions.Regex.Replace(n, @"\s+", " ").Trim().ToUpperInvariant();
        return n;
    }

    private static List<string> CorrespondentTokens(string normalized) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                  .Where(t => t.Length > 1)
                  .ToList();

    private static int Levenshtein(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                dp[i, j] = a[i - 1] == b[j - 1] ? dp[i - 1, j - 1]
                    : 1 + Math.Min(dp[i - 1, j - 1], Math.Min(dp[i - 1, j], dp[i, j - 1]));
        return dp[a.Length, b.Length];
    }


    /// <summary>Holt eine Stichprobe der letzten Dokumente (Titel + kurzer Inhalts-Auszug) für KI-Vorschläge.</summary>
    private async Task<(string SampleText, int Count)> BuildDocumentSampleAsync(SettingsDto settings, CancellationToken ct)
    {
        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));

        var docs = await paperless.ListDocumentsAsync(new() { ["page_size"] = "40", ["ordering"] = "-created" }, ct);
        var parts = docs.Results.Select(d =>
        {
            var excerpt = string.IsNullOrWhiteSpace(d.Content) ? "" : d.Content.Length > 300 ? d.Content[..300] : d.Content;
            return $"Titel: {d.Title}\n{excerpt}";
        });

        return (string.Join("\n---\n", parts), docs.Results.Count);
    }

    /// <summary>Startet den Backfill-Job asynchron (Fire-and-Forget) und gibt sofort zurück.</summary>
    [HttpPost("backfill-custom-fields")]
    public async Task<ActionResult> BackfillCustomFields(CancellationToken ct)
    {
        if (backfillJob.IsRunning())
            return Ok(new { started = false, message = "Backfill läuft bereits." });

        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.LlmApiKey))
            return Ok(new { started = false, message = "Bitte zuerst Paperless und den LLM-Provider konfigurieren." });
        if (!settings.EnableCustomFields)
            return Ok(new { started = false, message = "Custom Fields sind in den Einstellungen deaktiviert." });

        // Fire-and-forget – Job läuft im Hintergrund, Frontend pollt /backfill-progress
        _ = Task.Run(async () => await RunBackfillAsync(settings), CancellationToken.None);
        return Ok(new { started = true });
    }

    /// <summary>Fortschritt des laufenden Backfill-Jobs abfragen (für Polling).</summary>
    [HttpGet("backfill-progress")]
    public ActionResult<BackfillProgress> GetBackfillProgress() =>
        Ok(backfillJob.GetProgress());

    private async Task RunBackfillAsync(SettingsDto settings)
    {
        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
        var llm = new LlmClient(httpFactory.CreateClient("llm"), settings.ToLlmConfig());
        var options = settings.ToProcessingOptions();

        int updated = 0, errors = 0;
        try
        {
            var processedTagId = await paperless.GetOrCreateTagAsync(settings.ProcessedTagName);
            // Erst Gesamtzahl ermitteln
            var countPage = await paperless.ListDocumentsAsync(
                new() { ["tags__id__all"] = processedTagId.ToString(), ["page_size"] = "1" });
            backfillJob.Reset(countPage.Count);

            int current = 0, pageNum = 1;
            while (true)
            {
                var page = await paperless.ListDocumentsAsync(
                    new() { ["tags__id__all"] = processedTagId.ToString(), ["page_size"] = "25", ["page"] = pageNum.ToString() });

                foreach (var doc in page.Results)
                {
                    current++;
                    backfillJob.Update(current, doc.Title, updated, errors);

                    if (string.IsNullOrWhiteSpace(doc.Content)) continue;
                    try
                    {
                        var metadata = await DocumentProcessor.ExtractOnlyAsync(paperless, llm, doc, options);
                        if (metadata.IsTaxRelevant is null && metadata.Amount is null
                            && string.IsNullOrEmpty(metadata.FamilyMember)
                            && string.IsNullOrEmpty(metadata.InvoiceNumber)
                            && string.IsNullOrEmpty(metadata.PropertyAddress))
                            continue;

                        await DocumentProcessor.ApplyCustomFieldsOnlyAsync(paperless, doc.Id, metadata, options);
                        updated++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        log.LogWarning("Backfill Dok #{Id} fehlgeschlagen: {Msg}", doc.Id, ex.Message);
                    }
                }
                if (page.Next is null) break;
                pageNum++;
            }

            var result = new BackfillResult { Success = true, Processed = current, Updated = updated, Errors = errors };
            backfillJob.Complete(result);
            log.LogInformation("Backfill abgeschlossen: {C} geprüft, {U} aktualisiert, {E} Fehler", current, updated, errors);
        }
        catch (Exception ex)
        {
            backfillJob.Complete(new BackfillResult { Success = false, Message = ex.Message, Updated = updated, Errors = errors + 1 });
            log.LogError(ex, "Backfill-Job fehlgeschlagen");
        }
    }
}

