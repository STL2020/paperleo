using System.Text.Json;
using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Api.Services;

public interface IActivityLogService
{
    Task AppendAsync(ProcessedDocumentDto entry, CancellationToken ct = default);
    Task<List<ProcessedDocumentDto>> GetRecentAsync(int count, CancellationToken ct = default);
}

/// <summary>
/// Speichert verarbeitete Dokumente als Append-only JSON-Lines-Datei
/// (Standard: data/activity.jsonl), statt einer SQLite-Tabelle. Für den
/// Anwendungsfall (Anzeige der letzten N Einträge im Aktivität-Tab) völlig
/// ausreichend und braucht keine Datenbank.
/// </summary>
public class FileActivityLogService : IActivityLogService
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileActivityLogService(IConfiguration config)
    {
        var configuredPath = config["ActivityLogPath"]
            ?? Environment.GetEnvironmentVariable("ACTIVITY_LOG_PATH")
            ?? "data/activity.jsonl";
        _path = Path.GetFullPath(configuredPath);

        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public async Task AppendAsync(ProcessedDocumentDto entry, CancellationToken ct = default)
    {
        var line = JsonSerializer.Serialize(entry);
        await _lock.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(_path, line + "\n", ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<ProcessedDocumentDto>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return new();

        await _lock.WaitAsync(ct);
        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(_path, ct);
        }
        finally
        {
            _lock.Release();
        }

        var result = new List<ProcessedDocumentDto>();
        foreach (var line in lines.Reverse())
        {
            if (result.Count >= count) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var entry = JsonSerializer.Deserialize<ProcessedDocumentDto>(line);
                if (entry is not null) result.Add(entry);
            }
            catch (JsonException)
            {
                // Beschädigte Zeile (z.B. durch abgebrochenen Schreibvorgang) überspringen.
            }
        }

        return result;
    }
}
