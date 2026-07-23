using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using PaperlessAiCore.Api.Services;
using PaperlessAiCore.Core;
using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Api.Controllers;

public record WebhookDocumentRequest(string Url, string? Prompt);

/// <summary>
/// Webhook-Endpunkt für den direkten Trigger aus Paperless-ngx (Workflow-Action
/// "Webhook" bei "Dokument hinzugefügt"), als schnelle Alternative zum Polling.
///
/// Einrichtung in Paperless-ngx: Workflows -> neuer Workflow -> Trigger "Dokument
/// hinzugefügt" -> Action "Webhook" -> URL: http://&lt;host&gt;:8080/api/webhook/document
/// -> Parameter: url = {doc_url}
/// </summary>
[ApiController]
[Route("api/webhook")]
public partial class WebhookController(
    ISettingsService settingsService,
    IHttpClientFactory httpFactory,
    IActivityLogService activityLog,
    IWriteAuditLog writeAuditLog,
    WorkerStatus status,
    ILogger<WebhookController> log) : ControllerBase
{
    [GeneratedRegex(@"/documents/(\d+)/")]
    private static partial Regex DocumentIdPattern();

    [HttpPost("document")]
    public async Task<IActionResult> Document([FromBody] WebhookDocumentRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Url))
        {
            return BadRequest("Missing document URL");
        }

        var match = DocumentIdPattern().Match(req.Url);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var documentId))
        {
            return Ok(new { message = "Invalid document URL format" });
        }

        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.LlmApiKey))
        {
            return Ok(new { message = "Paperless-AI ist noch nicht konfiguriert (Setup-Wizard)." });
        }

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
        paperless.OnWriteLog = msg => writeAuditLog.Log($"[Webhook #{documentId}, ProcessedTagName='{settings.ProcessedTagName}'] {msg}");
        var llm = new LlmClient(httpFactory.CreateClient("llm"),
            settings.ToLlmConfig());

        try
        {
            var doc = await paperless.GetDocumentAsync(documentId, ct);
            var result = await DocumentProcessor.ProcessAsync(paperless, llm, doc, settings.ProcessedTagName, settings.ToProcessingOptions(), ct);

            await activityLog.AppendAsync(new ProcessedDocumentDto
            {
                DocumentId = result.DocumentId,
                Title = result.Metadata.Title,
                Confidence = result.Metadata.Confidence,
                ProcessedAt = DateTime.UtcNow,
                DocumentType = result.Metadata.DocumentType,
                PromptTokens = result.Metadata.PromptTokens,
                CompletionTokens = result.Metadata.CompletionTokens,
            }, ct);
            status.RecordProcessed();

            log.LogInformation("Webhook: Dokument #{Id} verarbeitet -> '{Title}'", result.DocumentId, result.Metadata.Title);

            return Accepted(new { message = "Document processed", documentId = result.DocumentId, title = result.Metadata.Title });
        }
        catch (MetadataExtractionException ex)
        {
            log.LogError("Webhook: Dokument #{Id}: {Message}", documentId, ex.Message);
            return Ok(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Webhook: unerwarteter Fehler bei Dokument #{Id}", documentId);
            return Ok(new { message = "Internal error" });
        }
    }
}
