using System.Diagnostics;

namespace PaperlessAiCore.Api.Services;

public record ResourceSample(DateTime Timestamp, double CpuPercent, double MemoryMb);

/// <summary>
/// Sampelt alle 5s CPU- und RAM-Nutzung DES EIGENEN .NET-Prozesses (nicht von
/// Paperless-ngx oder dem Host-System - dazu haben wir keinen Zugriff). Hält ein
/// rollierendes Fenster der letzten 60 Werte (5 Minuten) für die Dashboard-Grafik.
/// CPU-% wird aus der Differenz der Prozessorzeit zwischen zwei Samples berechnet
/// (bezogen auf die Anzahl CPU-Kerne), nicht aus einer einzelnen Momentaufnahme.
/// </summary>
public class ProcessMetricsService : BackgroundService
{
    private const int MaxSamples = 60;
    private readonly List<ResourceSample> _samples = new();
    private readonly object _lock = new();

    private TimeSpan _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
    private DateTime _lastSampleTime = DateTime.UtcNow;

    public List<ResourceSample> GetRecentSamples()
    {
        lock (_lock)
        {
            return _samples.ToList();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Sample();
            }
            catch
            {
                // Ressourcen-Monitoring darf die App niemals zum Absturz bringen.
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private void Sample()
    {
        var process = Process.GetCurrentProcess();
        var now = DateTime.UtcNow;
        var currentCpuTime = process.TotalProcessorTime;

        var wallClockDelta = (now - _lastSampleTime).TotalMilliseconds;
        var cpuDelta = (currentCpuTime - _lastCpuTime).TotalMilliseconds;
        var cpuPercent = wallClockDelta > 0
            ? Math.Round(cpuDelta / (Environment.ProcessorCount * wallClockDelta) * 100, 1)
            : 0;

        var memoryMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1);

        _lastCpuTime = currentCpuTime;
        _lastSampleTime = now;

        lock (_lock)
        {
            _samples.Add(new ResourceSample(now, Math.Clamp(cpuPercent, 0, 100), memoryMb));
            while (_samples.Count > MaxSamples)
            {
                _samples.RemoveAt(0);
            }
        }
    }
}
