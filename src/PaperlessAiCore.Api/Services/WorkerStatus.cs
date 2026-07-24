namespace PaperlessAiCore.Api.Services;

/// <summary>Thread-sicherer In-Memory-Status des Ingest-Workers für die UI-Statusanzeige.</summary>
public class WorkerStatus
{
    private readonly object _lock = new();

    public DateTime? LastPollAt { get; private set; }
    public int ProcessedSinceStart { get; private set; }
    public string? LastError { get; private set; }

    public void RecordPoll()
    {
        lock (_lock) { LastPollAt = DateTime.UtcNow; }
    }

    public void RecordProcessed()
    {
        lock (_lock) { ProcessedSinceStart++; }
    }

    public void RecordError(string message)
    {
        lock (_lock) { LastError = message; }
    }

    public void ClearError()
    {
        lock (_lock) { LastError = null; }
    }
}
