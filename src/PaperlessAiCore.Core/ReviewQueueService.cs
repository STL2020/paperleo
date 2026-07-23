using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PaperlessAiCore.Core;

/// <summary>
/// Verwaltet die Review-Queue für Dokumente die unter dem Konfidenz-Schwellwert liegen.
/// Speichert in data/review-queue.jsonl (file-basiert, kein DB nötig).
///
/// Konfidenz-Routing:
///   >= 0.95 → Auto-Archivierung ohne Review
///   0.80 - 0.95 → Normal-Verarbeitung, kein Queue-Eintrag
///   &lt; 0.80 → Review-Queue → manuell prüfen
/// </summary>
public class ReviewQueueService(ILogger<ReviewQueueService> log)
{
    private readonly string _queuePath = Path.Combine("data", "review-queue.jsonl");

    public const double AutoArchiveThreshold = 0.95;
    public const double ReviewQueueThreshold = 0.80;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Entscheidet ob ein Dokument automatisch archiviert, normal verarbeitet oder
    /// in die Review-Queue gestellt wird.
    /// </summary>
    public ConfidenceDecision Decide(double confidence) => confidence switch
    {
        >= AutoArchiveThreshold => ConfidenceDecision.AutoArchive,
        >= ReviewQueueThreshold => ConfidenceDecision.Process,
        _                       => ConfidenceDecision.QueueForReview,
    };

    public async Task EnqueueAsync(ReviewQueueEntry entry, CancellationToken ct = default)
    {
        Directory.CreateDirectory("data");
        var line = JsonSerializer.Serialize(entry, JsonOpts);
        await File.AppendAllTextAsync(_queuePath, line + "\n", ct);
        log.LogInformation("Review-Queue: Dok #{Id} eingereiht (confidence={C:F2}, Grund: {R})",
            entry.DocumentId, entry.Confidence, entry.Reason);
    }

    public async Task<List<ReviewQueueEntry>> GetPendingAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_queuePath)) return new();
        var lines = await File.ReadAllLinesAsync(_queuePath, ct);
        var result = new List<ReviewQueueEntry>();
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<ReviewQueueEntry>(line, JsonOpts);
                if (entry is { IsReviewed: false }) result.Add(entry);
            }
            catch { }
        }
        return result.OrderByDescending(e => e.QueuedAt).ToList();
    }

    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
        => (await GetPendingAsync(ct)).Count;

    public async Task MarkReviewedAsync(int documentId, CancellationToken ct = default)
    {
        if (!File.Exists(_queuePath)) return;
        var lines = await File.ReadAllLinesAsync(_queuePath, ct);
        var updated = lines.Select(line =>
        {
            if (string.IsNullOrWhiteSpace(line)) return line;
            try
            {
                var entry = JsonSerializer.Deserialize<ReviewQueueEntry>(line, JsonOpts);
                if (entry?.DocumentId == documentId)
                {
                    entry.IsReviewed = true;
                    return JsonSerializer.Serialize(entry, JsonOpts);
                }
            }
            catch { }
            return line;
        }).ToArray();
        await File.WriteAllLinesAsync(_queuePath, updated, ct);
        log.LogInformation("Review-Queue: Dok #{Id} als geprüft markiert", documentId);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        if (File.Exists(_queuePath)) File.Delete(_queuePath);
        await Task.CompletedTask;
        log.LogWarning("Review-Queue vollständig geleert");
    }
}

public enum ConfidenceDecision
{
    /// <summary>Confidence ≥ 0.95 → vollautomatisch archivieren</summary>
    AutoArchive,
    /// <summary>Confidence 0.80–0.95 → normal verarbeiten</summary>
    Process,
    /// <summary>Confidence &lt; 0.80 → manuell prüfen</summary>
    QueueForReview,
}
