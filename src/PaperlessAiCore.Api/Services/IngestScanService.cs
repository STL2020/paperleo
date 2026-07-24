using PaperlessAiCore.Core;
using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Api.Services;

public interface IIngestScanService
{
    Task<int> RunScanAsync(SettingsDto settings, CancellationToken ct = default);
}

/// <summary>
/// Ein kompletter Scan-Durchlauf: unverarbeitete Dokumente in Paperless finden und
/// per DocumentProcessor abarbeiten. Wird sowohl vom automatischen Hintergrund-Polling
/// (IngestWorker) als auch vom manuellen "Jetzt scannen"-Button im Dashboard genutzt.
/// Alle Jobs werden im ProcessingJobService registriert → sichtbar in /jobs.
/// </summary>
public class IngestScanService(
    ILogger<IngestScanService> log,
    IHttpClientFactory httpFactory,
    IActivityLogService activityLog,
    IWriteAuditLog writeAuditLog,
    WorkerStatus status,
    ProcessingJobService jobService) : IIngestScanService
{
    public async Task<int> RunScanAsync(SettingsDto settings, CancellationToken ct = default)
    {
        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
        paperless.OnWriteLog = msg => writeAuditLog.Log($"[Scan] {msg}");
        var llm = new LlmClient(httpFactory.CreateClient("llm"), settings.ToLlmConfig());

        // Direkt nur unverarbeitete Dokumente abfragen – kein clientseitiges Filtern mehr.
        // Paperless unterstützt tags__id__none={id} → gibt nur Dokumente zurück die diesen
        // Tag NICHT haben. Das reduziert die Scan-Phase von ~2 Minuten auf unter 1 Sekunde.
        var processedTagId = await paperless.GetOrCreateTagAsync(settings.ProcessedTagName, ct);
        var unprocessed = new List<PaperlessDocument>();
        var queryParams = new Dictionary<string, string>
        {
            ["tags__id__none"] = processedTagId.ToString(),
            ["ordering"] = "-created",
            ["page_size"] = "200",
        };
        var pageNumber = 1;
        const int SafetyPageLimit = 200;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            queryParams["page"] = pageNumber.ToString();
            var page = await paperless.ListDocumentsAsync(queryParams, ct);
            unprocessed.AddRange(page.Results);
            if (page.Next is null || pageNumber >= SafetyPageLimit) break;
            pageNumber++;
        }

        log.LogInformation("{Count} unverarbeitete Dokumente gefunden.", unprocessed.Count);

        // Alle Dokumente als "queued" registrieren – sofort in /jobs sichtbar
        var jobMap = new Dictionary<int, string>(); // docId → jobId
        foreach (var doc in unprocessed)
        {
            var docTitle = !string.IsNullOrWhiteSpace(doc.Title) ? doc.Title : $"Dokument #{doc.Id}";
            var job = jobService.Enqueue(docTitle, doc.Id);
            jobMap[doc.Id] = job.Id;
        }

        var processedCount = 0;
        foreach (var doc in unprocessed)
        {
            ct.ThrowIfCancellationRequested();
            var jobId = jobMap.GetValueOrDefault(doc.Id, "");
            jobService.MarkRunning(jobId);

            try
            {
                var result = await DocumentProcessor.ProcessAsync(
                    paperless, llm, doc,
                    settings.ProcessedTagName,
                    settings.ToProcessingOptions(), ct);

                await activityLog.AppendAsync(new ProcessedDocumentDto
                {
                    DocumentId  = result.DocumentId,
                    Title       = result.Metadata.Title,
                    Confidence  = result.Metadata.Confidence,
                    ProcessedAt = DateTime.UtcNow,
                    DocumentType    = result.Metadata.DocumentType,
                    PromptTokens    = result.Metadata.PromptTokens,
                    CompletionTokens = result.Metadata.CompletionTokens,
                }, ct);

                status.RecordProcessed();
                processedCount++;

                // Confidence-Routing: < 0.80 → Review-Queue-Hinweis im Ergebnis
                var confidence = result.Metadata.Confidence;
                var decision = ReviewQueueService.AutoArchiveThreshold <= confidence ? "Auto-Archiv"
                    : ReviewQueueService.ReviewQueueThreshold <= confidence ? "Normal"
                    : "Review empfohlen";

                jobService.MarkDone(jobId,
                    result: $"{result.Metadata.Correspondent} · {result.Metadata.DocumentType} · {decision}",
                    confidence: confidence);

                log.LogInformation("Dok #{Id} → '{Title}' (Konfidenz {C:0.00}, {D})",
                    result.DocumentId, result.Metadata.Title, confidence, decision);
            }
            catch (MetadataExtractionException ex)
            {
                jobService.MarkFailed(jobId, ex.Message);
                log.LogWarning("Dokument #{Id}: {Message}", doc.Id, ex.Message);
                // Kein OCR-Text → dauerhaft überspringen damit das Dok nicht ewig wiederholt wird
                await MarkAsProcessedAsync(paperless, doc.Id, processedTagId, ct);
            }
            catch (Exception ex)
            {
                var msg = ex.Message.Length > 200 ? ex.Message[..200] + "…" : ex.Message;
                jobService.MarkFailed(jobId, msg);
                log.LogError(ex, "Dokument #{Id}: Verarbeitung fehlgeschlagen - {Message}", doc.Id, ex.Message);
                // Bei 500-Fehlern: Verarbeitungs-Tag trotzdem setzen damit das Dok nicht in der nächsten
                // Runde erneut verarbeitet wird und eine Endlosschleife entsteht.
                if (ex.Message.Contains("500") || ex.Message.Contains("API-Fehler"))
                    await MarkAsProcessedAsync(paperless, doc.Id, processedTagId, ct);
            }
        }

        return processedCount;
    }

    /// <summary>
    /// Setzt den Verarbeitungs-Tag auf ein Dokument das nicht verarbeitet werden konnte
    /// (kein OCR, 500-Fehler) damit es in der nächsten Runde NICHT erneut versucht wird.
    /// </summary>
    private static async Task MarkAsProcessedAsync(PaperlessClient paperless, int docId, int processedTagId, CancellationToken ct)
    {
        try
        {
            var doc = await paperless.GetDocumentAsync(docId, ct);
            var tags = doc.Tags.Contains(processedTagId) ? doc.Tags : doc.Tags.Append(processedTagId).ToList();
            await paperless.UpdateDocumentAsync(docId, new { tags }, ct);
        }
        catch (Exception ex)
        {
            // Loggen aber nicht weiterwerfen – das Dokument bleibt dann eben offen
            Console.WriteLine($"[WARN] Dok #{docId}: Konnte Verarbeitungs-Tag nach Fehler nicht setzen: {ex.Message}");
        }
    }
}
