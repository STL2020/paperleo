using Microsoft.AspNetCore.Mvc;
using PaperlessAiCore.Api.Services;
using PaperlessAiCore.Core;
using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Api.Controllers;

[ApiController]
[Route("api/archive-analysis")]
public class ArchiveAnalysisController(
    ISettingsService settingsService,
    IHttpClientFactory httpFactory) : ControllerBase
{
    // In-Memory Status (reicht für Single-Instance)
    private static ArchiveAnalysisStatus _status = new();
    private static ArchiveAnalysisResult? _lastResult;
    private static readonly SemaphoreSlim _lock = new(1, 1);

    [HttpGet("status")]
    public ActionResult<ArchiveAnalysisStatus> GetStatus()
    {
        return Ok(_status with { LastResult = _lastResult });
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        if (!await _lock.WaitAsync(0))
            return Conflict(new { message = "Analyse läuft bereits." });

        try
        {
            var settings = await settingsService.GetAsync(ct);

            if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) ||
                string.IsNullOrWhiteSpace(settings.PaperlessApiToken))
                return BadRequest(new { message = "Paperless-Verbindung nicht konfiguriert." });

            if (string.IsNullOrWhiteSpace(settings.LlmApiKey))
                return BadRequest(new { message = "KI-Verbindung nicht konfiguriert." });

            _status = new ArchiveAnalysisStatus { IsRunning = true, Progress = 0, Phase = "Starte …" };

            // Analyse im Hintergrund starten
            _ = Task.Run(async () =>
            {
                try
                {
                    var paperless = new PaperlessClient(
                        httpFactory.CreateClient("paperless"),
                        new PaperlessConnectionConfig(
                            settings.PaperlessUrl.Trim(),
                            settings.PaperlessApiToken.Trim()));

                    var llm = new LlmClient(
                        httpFactory.CreateClient("llm"),
                        new LlmConnectionConfig(
                            settings.LlmModel.Trim(),
                            settings.LlmApiKey.Trim(),
                            string.IsNullOrWhiteSpace(settings.LlmApiBase) ? null : settings.LlmApiBase.Trim()));

                    var progress = new Progress<(int pct, string phase)>(p =>
                    {
                        _status = _status with { Progress = p.pct, Phase = p.phase };
                    });

                    _lastResult = await ArchiveAnalysisService.AnalyzeAsync(
                        paperless, llm, progress, CancellationToken.None);

                    _status = new ArchiveAnalysisStatus
                    {
                        IsRunning = false,
                        Progress = 100,
                        Phase = $"Fertig — {_lastResult.Fields.Count} Felder gefunden",
                        LastResult = _lastResult
                    };
                }
                catch (Exception ex)
                {
                    _lastResult = new ArchiveAnalysisResult
                    {
                        Success = false,
                        Error = ex.Message
                    };
                    _status = new ArchiveAnalysisStatus
                    {
                        IsRunning = false,
                        Progress = 0,
                        Phase = $"Fehler: {ex.Message}"
                    };
                }
                finally
                {
                    _lock.Release();
                }
            }, CancellationToken.None);

            return Accepted(new { message = "Analyse gestartet." });
        }
        catch
        {
            _lock.Release();
            throw;
        }
    }

    [HttpGet("result")]
    public ActionResult<ArchiveAnalysisResult?> GetResult() => Ok(_lastResult);

    [HttpDelete("result")]
    public IActionResult ClearResult()
    {
        _lastResult = null;
        _status = new ArchiveAnalysisStatus();
        return NoContent();
    }
}
