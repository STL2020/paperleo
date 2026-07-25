using Microsoft.AspNetCore.Mvc;
using PaperlessAiCore.Api.Services;
using PaperlessAiCore.Core;
using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Api.Controllers;

[ApiController]
[Route("api/backup")]
public class BackupController(
    ISettingsService settingsService,
    IHttpClientFactory httpFactory) : ControllerBase
{
    // In-Memory Status
    private static BackupStatusDto _status = new();
    private static readonly SemaphoreSlim _lock = new(1, 1);

    [HttpGet("status")]
    public ActionResult<BackupStatusDto> GetStatus() => Ok(_status);

    // ── Backup starten ────────────────────────────────────────────────────────
    [HttpPost("start")]
    public async Task<IActionResult> StartBackup(CancellationToken ct)
    {
        if (!await _lock.WaitAsync(0))
            return Conflict(new { message = "Backup läuft bereits." });

        try
        {
            var settings = await settingsService.GetAsync(ct);

            if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) ||
                string.IsNullOrWhiteSpace(settings.PaperlessApiToken))
                return BadRequest(new { message = "Paperless-Verbindung nicht konfiguriert." });

            _status = new BackupStatusDto { IsRunning = true, Progress = 0, Phase = "Starte Backup …" };

            _ = Task.Run(async () =>
            {
                try
                {
                    var paperless = new PaperlessClient(
                        httpFactory.CreateClient("paperless"),
                        new PaperlessConnectionConfig(
                            settings.PaperlessUrl.Trim(),
                            settings.PaperlessApiToken.Trim()));

                    var progress = new Progress<(int pct, string phase)>(p =>
                        _status = new BackupStatusDto { IsRunning = _status.IsRunning, Progress = p.pct, Phase = p.phase, LastResult = _status.LastResult });

                    var result = await PaperlessBackupService.CreateBackupAsync(
                        paperless, progress, CancellationToken.None);

                    _status = new BackupStatusDto
                    {
                        IsRunning = false,
                        Progress  = 100,
                        Phase     = result.Success
                            ? $"Fertig — {result.DownloadedDocuments} PDFs, {result.ZipSizeMb:F1} MB"
                            : $"Fehler: {result.Error}",
                        LastResult = result
                    };
                }
                catch (Exception ex)
                {
                    _status = new BackupStatusDto
                    {
                        IsRunning = false,
                        Progress  = 0,
                        Phase     = $"Fehler: {ex.Message}",
                        LastResult = new BackupResult { Success = false, Error = ex.Message }
                    };
                }
                finally { _lock.Release(); }
            }, CancellationToken.None);

            return Accepted(new { message = "Backup gestartet." });
        }
        catch
        {
            _lock.Release();
            throw;
        }
    }

    // ── ZIP herunterladen ─────────────────────────────────────────────────────
    [HttpGet("download")]
    public IActionResult DownloadZip()
    {
        var zipPath = _status.LastResult?.ZipPath;
        if (string.IsNullOrEmpty(zipPath) || !System.IO.File.Exists(zipPath))
            return NotFound(new { message = "Kein Backup vorhanden oder ZIP wurde bereits gelöscht." });

        var filename = Path.GetFileName(zipPath);
        var stream   = System.IO.File.OpenRead(zipPath);
        return File(stream, "application/zip", filename);
    }

    // ── Restore: Metadaten aus ZIP ────────────────────────────────────────────
    [HttpPost("restore")]
    public async Task<ActionResult<RestoreResult>> RestoreMetadata(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);

        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) ||
            string.IsNullOrWhiteSpace(settings.PaperlessApiToken))
            return BadRequest(new RestoreResult { Success = false, Error = "Paperless-Verbindung nicht konfiguriert." });

        var paperless = new PaperlessClient(
            httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(
                settings.PaperlessUrl.Trim(),
                settings.PaperlessApiToken.Trim()));

        var progress = new Progress<(int pct, string phase)>(p =>
            _status = _status with { Progress = p.pct, Phase = p.phase });

        _status = _status with { IsRunning = true, Phase = "Wiederherstellung läuft …" };

        try
        {
            var result = await PaperlessBackupService.RestoreMetadataAsync(
                paperless, Request.Body, progress, ct);

            _status = _status with
            {
                IsRunning = false,
                Progress  = 100,
                Phase     = result.Success ? "Wiederherstellung abgeschlossen." : $"Fehler: {result.Error}"
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _status = new BackupStatusDto { IsRunning = false, Progress = 0, Phase = $"Fehler: {ex.Message}", LastResult = _status.LastResult };
            return Ok(new RestoreResult { Success = false, Error = ex.Message });
        }
    }

    // ── Status zurücksetzen ───────────────────────────────────────────────────
    [HttpDelete("status")]
    public IActionResult ResetStatus()
    {
        // ZIP-Datei aufräumen falls vorhanden
        var zipPath = _status.LastResult?.ZipPath;
        if (!string.IsNullOrEmpty(zipPath) && System.IO.File.Exists(zipPath))
        {
            try { System.IO.File.Delete(zipPath); } catch { }
        }
        _status = new BackupStatusDto();
        return NoContent();
    }
}
