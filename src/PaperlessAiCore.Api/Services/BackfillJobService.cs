using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Api.Services;

/// <summary>
/// Singleton: hält den Zustand des aktuell laufenden (oder zuletzt abgeschlossenen)
/// Backfill-Jobs im Arbeitsspeicher. Der Controller startet den Job als Fire-and-Forget
/// Task und gibt sofort zurück; das Frontend pollt /api/dashboard/backfill-progress.
/// </summary>
public class BackfillJobService
{
    private readonly object _lock = new();
    private BackfillProgress _progress = new() { IsComplete = true };

    public BackfillProgress GetProgress()
    {
        lock (_lock)
        {
            return new BackfillProgress
            {
                IsRunning = _progress.IsRunning,
                Total = _progress.Total,
                Current = _progress.Current,
                Updated = _progress.Updated,
                Errors = _progress.Errors,
                CurrentDocTitle = _progress.CurrentDocTitle,
                IsComplete = _progress.IsComplete,
                FinalResult = _progress.FinalResult,
            };
        }
    }

    public bool IsRunning()
    {
        lock (_lock) return _progress.IsRunning;
    }

    public void Reset(int total)
    {
        lock (_lock)
        {
            _progress = new BackfillProgress
            {
                IsRunning = true,
                Total = total,
                Current = 0,
                Updated = 0,
                Errors = 0,
                IsComplete = false,
            };
        }
    }

    public void Update(int current, string? currentDocTitle, int updated, int errors)
    {
        lock (_lock)
        {
            _progress.Current = current;
            _progress.CurrentDocTitle = currentDocTitle;
            _progress.Updated = updated;
            _progress.Errors = errors;
        }
    }

    public void Complete(BackfillResult result)
    {
        lock (_lock)
        {
            _progress.IsRunning = false;
            _progress.IsComplete = true;
            _progress.FinalResult = result;
            _progress.CurrentDocTitle = null;
        }
    }
}
