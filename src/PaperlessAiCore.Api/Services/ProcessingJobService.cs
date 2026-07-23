using System.Collections.Concurrent;
using System.Text.Json;
using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Api.Services;

/// <summary>
/// Verwaltet die aktuelle Verarbeitungs-Warteschlange aller Dokument-Jobs.
/// In-Memory (für Live-Updates), persistiert in data/jobs.jsonl.
/// Maximale History: 500 Jobs (ältere werden automatisch bereinigt).
/// </summary>
public class ProcessingJobService
{
    private readonly ConcurrentDictionary<string, ProcessingJobDto> _jobs = new();
    private readonly string _path = Path.Combine("data", "jobs.jsonl");
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const int MaxHistory = 500;

    public ProcessingJobDto Enqueue(string name, int? documentId = null)
    {
        var job = new ProcessingJobDto
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = name,
            DocumentId = documentId,
            Status = "queued",
            IssuedAt = DateTime.UtcNow,
        };
        _jobs[job.Id] = job;
        Trim();
        return job;
    }

    public void MarkRunning(string id)
    {
        if (_jobs.TryGetValue(id, out var j)) j.Status = "running";
    }

    public void MarkDone(string id, string? result = null, double? confidence = null)
    {
        if (_jobs.TryGetValue(id, out var j))
        {
            j.Status = "done";
            j.FinishedAt = DateTime.UtcNow;
            j.Result = result;
            j.Confidence = confidence;
        }
    }

    public void MarkFailed(string id, string error)
    {
        if (_jobs.TryGetValue(id, out var j))
        {
            j.Status = "failed";
            j.FinishedAt = DateTime.UtcNow;
            j.Error = error;
        }
    }

    public List<ProcessingJobDto> GetAll(string? filter = null)
    {
        var all = _jobs.Values
            .OrderByDescending(j => j.IssuedAt)
            .ToList();
        if (!string.IsNullOrEmpty(filter) && filter != "all")
            all = all.Where(j => j.Status == filter).ToList();
        return all;
    }

    public (int queued, int running, int done, int failed) GetCounts()
    {
        var all = _jobs.Values.ToList();
        return (
            all.Count(j => j.Status == "queued"),
            all.Count(j => j.Status == "running"),
            all.Count(j => j.Status == "done"),
            all.Count(j => j.Status == "failed")
        );
    }

    public void Clear()
    {
        _jobs.Clear();
        if (File.Exists(_path)) File.Delete(_path);
    }

    private void Trim()
    {
        var excess = _jobs.Values.OrderBy(j => j.IssuedAt)
            .Take(Math.Max(0, _jobs.Count - MaxHistory)).ToList();
        foreach (var j in excess) _jobs.TryRemove(j.Id, out _);
    }
}
