using Microsoft.AspNetCore.Mvc;
using PaperlessAiCore.Api.Services;
using PaperlessAiCore.Core;
using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Api.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController(
    ISettingsService settingsService,
    IHttpClientFactory httpFactory,
    IActivityLogService activityLog,
    IWriteAuditLog writeAuditLog,
    WorkerStatus status,
    ILogger<DocumentsController> log) : ControllerBase
{
    /// <summary>Dokumentliste aus Paperless für die Auswahl auf der "Dokument testen"-Seite.</summary>
    [HttpGet]
    public async Task<ActionResult<List<DocumentSummaryDto>>> List([FromQuery] string? query, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.PaperlessApiToken))
        {
            return Ok(new List<DocumentSummaryDto>());
        }

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));

        var queryParams = new Dictionary<string, string> { ["page_size"] = "25", ["ordering"] = "-created" };
        if (!string.IsNullOrWhiteSpace(query))
        {
            queryParams["query"] = query;
        }

        var data = await paperless.ListDocumentsAsync(queryParams, ct);
        var results = data.Results.Select(d => new DocumentSummaryDto
        {
            Id = d.Id,
            Title = d.Title,
            Created = d.Created,
        }).ToList();

        return Ok(results);
    }

    /// <summary>
    /// Proxy für die native PDF-Vorschau: Paperless-ngx verlangt einen Token-Header,
    /// den ein &lt;iframe src="..."&gt; im Browser nicht mitschicken kann. Diese Route
    /// holt das PDF serverseitig (mit dem gespeicherten Token) und reicht es unverändert
    /// durch - das Frontend kann so einfach &lt;iframe src="/api/documents/{id}/preview"&gt;
    /// verwenden, ganz ohne eigene Auth-Logik im Browser.
    /// </summary>
    [HttpGet("{id:int}/preview")]
    public async Task<IActionResult> Preview(int id, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.PaperlessApiToken))
        {
            return NotFound();
        }

        var client = httpFactory.CreateClient("paperless");
        client.BaseAddress = new Uri(settings.PaperlessUrl.TrimEnd('/'));
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Token", settings.PaperlessApiToken);

        HttpResponseMessage upstream;
        try
        {
            upstream = await client.GetAsync($"/api/documents/{id}/preview/", HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "PDF-Vorschau für Dokument #{Id} fehlgeschlagen", id);
            return StatusCode(502);
        }

        if (!upstream.IsSuccessStatusCode)
        {
            return StatusCode((int)upstream.StatusCode);
        }

        var contentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/pdf";
        var stream = await upstream.Content.ReadAsStreamAsync(ct);
        return File(stream, contentType);
    }

    /// <summary>Vollständige Detailansicht (Namen statt IDs aufgelöst) für die Vorschau-Drawer.</summary>
    [HttpGet("{id:int}/detail")]
    public async Task<ActionResult<DocumentDetailDto>> Detail(int id, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.PaperlessApiToken))
        {
            return BadRequest(new { error = "Paperless ist noch nicht konfiguriert." });
        }

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));

        try
        {
            var doc = await paperless.GetDocumentAsync(id, ct);

            string? correspondentName = null;
            if (doc.Correspondent is not null)
            {
                var correspondents = await paperless.ListCorrespondentsAsync(ct);
                correspondentName = correspondents.FirstOrDefault(c => c.Id == doc.Correspondent)?.Name;
            }

            string? documentTypeName = null;
            if (doc.DocumentType is not null)
            {
                var types = await paperless.ListDocumentTypesAsync(ct);
                documentTypeName = types.FirstOrDefault(t => t.Id == doc.DocumentType)?.Name;
            }

            var allTags = doc.Tags.Count > 0 ? await paperless.ListTagsAsync(ct) : new List<PaperlessTag>();
            var tagNames = allTags.Where(t => doc.Tags.Contains(t.Id)).Select(t => t.Name).ToList();

            var excerpt = string.IsNullOrWhiteSpace(doc.Content) ? null
                : doc.Content.Length > 600 ? doc.Content[..600] + "…" : doc.Content;

            return Ok(new DocumentDetailDto
            {
                Id = doc.Id,
                Title = doc.Title,
                Created = doc.Created,
                Correspondent = correspondentName,
                DocumentType = documentTypeName,
                Tags = tagNames,
                ContentExcerpt = excerpt,
            });
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Detailansicht für Dokument #{Id} fehlgeschlagen", id);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// NUR Vorschau: LLM-Extraktion ohne jeden Schreibzugriff auf Paperless.
    /// Zeigt, was die KI vorschlagen würde. Erst /apply schreibt tatsächlich.
    /// </summary>
    [HttpPost("{id:int}/process")]
    public async Task<ActionResult<ProcessDocumentResultDto>> Process(int id, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) ||
            string.IsNullOrWhiteSpace(settings.PaperlessApiToken) ||
            string.IsNullOrWhiteSpace(settings.LlmApiKey))
        {
            return BadRequest(new { error = "Bitte zuerst im Setup Paperless und den LLM-Provider konfigurieren." });
        }

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
        var llm = new LlmClient(httpFactory.CreateClient("llm"),
            settings.ToLlmConfig());

        try
        {
            var doc = await paperless.GetDocumentAsync(id, ct);
            var metadata = await DocumentProcessor.ExtractOnlyAsync(paperless, llm, doc, settings.ToProcessingOptions(), ct);

            log.LogInformation("Vorschau: Dokument #{Id} -> '{Title}' (nicht geschrieben)", id, metadata.Title);

            return Ok(new ProcessDocumentResultDto
            {
                DocumentId = id,
                Title = metadata.Title,
                Correspondent = metadata.Correspondent,
                DocumentType = metadata.DocumentType,
                InvoiceNumber = metadata.InvoiceNumber,
                ReceiptNumber = metadata.ReceiptNumber,
                Tags = metadata.Tags,
                Amount = metadata.Amount,
                Date = metadata.Date,
                Confidence = metadata.Confidence,
                PromptTokens = metadata.PromptTokens,
                CompletionTokens = metadata.CompletionTokens,
            });
        }
        catch (MetadataExtractionException ex)
        {
            log.LogWarning("Vorschau Dokument #{Id} fehlgeschlagen: {Message}", id, ex.Message);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Schreibt eine zuvor per /process erzeugte (ggf. vom Nutzer geprüfte) Vorschau
    /// tatsächlich nach Paperless - ohne erneuten LLM-Aufruf.
    /// </summary>
    [HttpPost("{id:int}/apply")]
    public async Task<ActionResult<ProcessDocumentResultDto>> Apply(int id, [FromBody] ProcessDocumentResultDto preview, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.PaperlessApiToken))
        {
            return BadRequest(new { error = "Bitte zuerst im Setup Paperless konfigurieren." });
        }

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
        paperless.OnWriteLog = msg => writeAuditLog.Log($"[Apply #{id}, ProcessedTagName='{settings.ProcessedTagName}'] {msg}");

        try
        {
            var doc = await paperless.GetDocumentAsync(id, ct);
            var metadata = new ExtractedMetadata
            {
                Title = preview.Title,
                Correspondent = preview.Correspondent,
                DocumentType = preview.DocumentType,
                InvoiceNumber = preview.InvoiceNumber,
                ReceiptNumber = preview.ReceiptNumber,
                Tags = preview.Tags,
                Amount = preview.Amount,
                Date = preview.Date,
                Confidence = preview.Confidence,
                PromptTokens = preview.PromptTokens,
                CompletionTokens = preview.CompletionTokens,
            };

            var result = await DocumentProcessor.ApplyAsync(paperless, doc, settings.ProcessedTagName, settings.ToProcessingOptions(), metadata, ct);

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

            log.LogInformation("Übernommen: Dokument #{Id} -> '{Title}'", result.DocumentId, result.Metadata.Title);

            return Ok(preview);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Übernehmen für Dokument #{Id} fehlgeschlagen", id);
            return BadRequest(new { error = ex.Message });
        }
    }
}
