using Microsoft.AspNetCore.Mvc;
using PaperlessAiCore.Api.Services;
using PaperlessAiCore.Core;
using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Api.Controllers;

[ApiController]
[Route("api/status")]
public class StatusController(ISettingsService settingsService, WorkerStatus workerStatus, IActivityLogService activityLog, BuildCounterService buildCounter) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StatusDto>> Get(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        return Ok(new StatusDto
        {
            IsConfigured = settings.IsConfigured,
            ProMode = LicenseCheck.IsProMode(settings.PremiumLicenseKey),
            AutoModeEnabled = settings.AutoModeEnabled,
            LastPollAt = workerStatus.LastPollAt,
            ProcessedSinceStart = workerStatus.ProcessedSinceStart,
            LastError = workerStatus.LastError,
            BuildNumber = buildCounter.BuildNumber,
        });
    }

    [HttpGet("activity")]
    public async Task<ActionResult<List<ProcessedDocumentDto>>> GetActivity(CancellationToken ct)
    {
        var items = await activityLog.GetRecentAsync(50, ct);
        return Ok(items);
    }
}

// Separater Controller für Lizenz-Aktivierung
[ApiController]
[Route("api/license")]
public class LicenseController(ISettingsService settingsService) : ControllerBase
{
    [HttpPost("activate")]
    public async Task<ActionResult> Activate([FromBody] LicenseActivateRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.LicenseKey))
            return BadRequest(new { success = false, message = "Kein Lizenzschlüssel angegeben." });

        var (success, message) = await LicenseCheck.VerifyWithPayHipAsync(req.LicenseKey);

        if (success)
        {
            var settings = await settingsService.GetAsync(ct);
            settings.PremiumLicenseKey = req.LicenseKey.Trim();
            await settingsService.SaveAsync(settings, ct);
        }

        return Ok(new { success, message });
    }

    [HttpPost("deactivate")]
    public async Task<ActionResult> Deactivate(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        settings.PremiumLicenseKey = "";
        await settingsService.SaveAsync(settings, ct);
        return Ok(new { success = true, message = "Lizenz deaktiviert." });
    }
}

public record LicenseActivateRequest(string LicenseKey);
