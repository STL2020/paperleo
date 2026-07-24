using System.Collections.Concurrent;

namespace PaperlessAiCore.Api.Services;

public record LogEntry(DateTime Timestamp, string Level, string Category, string Message);

/// <summary>
/// Bewahrt die letzten N Log-Einträge im Speicher auf, für den Log-Viewer im
/// Admin-Dashboard (PRO). Bewusst keine Datei/DB - reicht für "was ist gerade
/// passiert", muss nicht über Neustarts hinweg persistieren.
/// </summary>
public class InMemoryLogStore
{
    private const int MaxEntries = 300;
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public void Add(LogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _)) { }
    }

    public List<LogEntry> GetRecent(int count) =>
        _entries.Reverse().Take(count).ToList();
}
