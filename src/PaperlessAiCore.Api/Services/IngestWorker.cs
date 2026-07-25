namespace PaperlessAiCore.Api.Services;

/// <summary>
/// Pollt Paperless-ngx regelmässig nach neuen, unverarbeiteten Dokumenten und lässt
/// sie über IngestScanService verarbeiten. Reine Ablaufsteuerung (Intervall, Automode-
/// Check) - die eigentliche Scan-Logik liegt in IngestScanService, damit der manuelle
/// "Jetzt scannen"-Button im Dashboard exakt denselben Code nutzen kann.
/// </summary>
public class IngestWorker(
    ILogger<IngestWorker> log,
    ISettingsService settingsService,
    IIngestScanService scanService,
    WorkerStatus status) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Kurze Anlaufverzögerung, damit die App vollständig hochgefahren ist.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = await settingsService.GetAsync(stoppingToken);

            if (!settings.AutoModeEnabled ||
                string.IsNullOrWhiteSpace(settings.PaperlessUrl) ||
                string.IsNullOrWhiteSpace(settings.PaperlessApiToken) ||
                string.IsNullOrWhiteSpace(settings.LlmApiKey))
            {
                // Noch nicht konfiguriert oder Automode deaktiviert -> einfach warten.
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                continue;
            }

            try
            {
                await scanService.RunScanAsync(settings, stoppingToken);
                status.RecordPoll();
                status.ClearError();
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Fehler im Polling-Zyklus");
                status.RecordError(ex.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(settings.PollIntervalSeconds, 10)), stoppingToken);
        }
    }
}
