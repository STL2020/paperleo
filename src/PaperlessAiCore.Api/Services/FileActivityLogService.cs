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

        // Lese nur die letzten N Zeilen von Ende der Datei (Tail-Read)
        // Vermeidet komplettes Einlesen bei großen Dateien (10k+ Einträge)
        var tailLines = await ReadTailLinesAsync(_path, count * 2, ct); // *2 für leer/fehlerhafte Zeilen

        var result = new List<ProcessedDocumentDto>(count);
        foreach (var line in tailLines) // already in reverse order
        {
            if (result.Count >= count) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<ProcessedDocumentDto>(line);
                if (entry is not null) result.Add(entry);
            }
            catch (JsonException) { }
        }
        return result;
    }

    /// <summary>Liest die letzten N Zeilen aus einer Datei ohne alles einzulesen.</summary>
    private async Task<List<string>> ReadTailLinesAsync(string path, int maxLines, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length == 0) return new();

            // Fuer kleine Dateien: direkt alles lesen (schnell)
            if (fileInfo.Length < 512 * 1024) // < 512 KB
            {
                var all = await File.ReadAllLinesAsync(path, ct);
                return all.Reverse().Take(maxLines).ToList();
            }

            // Fuer grosse Dateien: rueckwaerts zeilenweise lesen
            var result = new List<string>(maxLines);
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long pos = fs.Length - 1;
            var buffer = new System.Collections.Generic.List<byte>();
            const byte NEWLINE = 10;   // 

            const byte CR      = 13;   // 

            while (pos >= 0 && result.Count < maxLines)
            {
                fs.Seek(pos, SeekOrigin.Begin);
                int b = fs.ReadByte();
                pos--;

                if (b == NEWLINE || pos < 0)
                {
                    if (buffer.Count > 0)
                    {
                        buffer.Reverse();
                        var line = System.Text.Encoding.UTF8.GetString(buffer.ToArray()).Trim();
                        if (!string.IsNullOrWhiteSpace(line))
                            result.Add(line);
                        buffer.Clear();
                    }
                }
                else if (b != CR)
                {
                    buffer.Add((byte)b);
                }
            }
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }
}