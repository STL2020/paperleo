using Microsoft.AspNetCore.Mvc;
using PaperlessAiCore.Api.Services;
using PaperlessAiCore.Core;
using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController(ISettingsService settingsService, IHttpClientFactory httpFactory) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SettingsDto>> Get(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        return Ok(settings);
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SettingsDto dto, CancellationToken ct)
    {
        await settingsService.SaveAsync(dto, ct);
        return NoContent();
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var content = await settingsService.ExportRawAsync(ct);
        var filename = $"paperleo-settings-{DateTime.Now:yyyy-MM-dd}.env";
        return File(System.Text.Encoding.UTF8.GetBytes(content), "text/plain", filename);
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var content = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(content))
            return BadRequest(new { success = false, message = "Leere Datei." });
        await settingsService.ImportRawAsync(content, ct);
        return Ok(new { success = true, message = "Einstellungen importiert. Seite neu laden." });
    }

    [HttpPost("test-paperless")]
    public async Task<ActionResult<ConnectionTestResult>> TestPaperless([FromBody] SettingsDto dto, CancellationToken ct)
    {
        try
        {
            var client = new PaperlessClient(httpFactory.CreateClient("paperless"),
                new PaperlessConnectionConfig(dto.PaperlessUrl.Trim(), dto.PaperlessApiToken.Trim()));
            var result = await client.ListDocumentsAsync(new() { ["page_size"] = "1" }, ct);
            return Ok(new ConnectionTestResult
            {
                Success = true,
                Message = "Verbindung erfolgreich.",
                DocumentCount = result.Count,
            });
        }
        catch (Exception ex)
        {
            return Ok(new ConnectionTestResult { Success = false, Message = ex.Message });
        }
    }

    [HttpPost("test-llm")]
    public async Task<ActionResult<ConnectionTestResult>> TestLlm([FromBody] SettingsDto dto, CancellationToken ct)
    {
        try
        {
            var client = new LlmClient(httpFactory.CreateClient("llm"),
                new LlmConnectionConfig(dto.LlmModel.Trim(), dto.LlmApiKey.Trim(),
                    string.IsNullOrWhiteSpace(dto.LlmApiBase) ? null : dto.LlmApiBase.Trim()));
            var result = await client.ChatAsync(new()
            {
                new LlmChatMessage { Role = "user", Content = "Antworte nur mit 'OK'." },
            }, ct: ct);
            return Ok(new ConnectionTestResult { Success = true, Message = result.Content ?? "OK" });
        }
        catch (Exception ex)
        {
            return Ok(new ConnectionTestResult { Success = false, Message = ex.Message });
        }
    }
}
