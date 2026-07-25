using PaperlessAiCore.Core;

namespace PaperlessAiCore.Api.Services;

public record PaperlessMetadataSnapshot(
    List<PaperlessTag> Tags,
    List<PaperlessDocumentType> DocumentTypes,
    List<PaperlessCorrespondent> Correspondents,
    List<PaperlessCustomField> CustomFields);

/// <summary>
/// Cacht die "Schema-Discovery"-Daten (Tags/Dokumenttypen/Korrespondenten/Custom
/// Fields) einer Paperless-Instanz für kurze Zeit (10 Minuten), damit der Such-Agent
/// nicht bei JEDER Anfrage alle vier Listen neu von Paperless abrufen muss. Bewusst
/// ohne Kenntnis konkreter Feldnamen ("Steuer" o.ä.) - komplett instanz-agnostisch,
/// jede Paperless-Installation liefert einfach ihre eigenen Listen.
/// </summary>
public class PaperlessMetadataCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private PaperlessMetadataSnapshot? _snapshot;
    private DateTime _lastRefresh = DateTime.MinValue;

    public async Task<PaperlessMetadataSnapshot> GetAsync(PaperlessClient client, CancellationToken ct = default)
    {
        if (_snapshot is not null && DateTime.UtcNow - _lastRefresh < Ttl)
        {
            return _snapshot;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_snapshot is not null && DateTime.UtcNow - _lastRefresh < Ttl)
            {
                return _snapshot;
            }

            var tags = await client.ListTagsAsync(ct);
            var docTypes = await client.ListDocumentTypesAsync(ct);
            var correspondents = await client.ListCorrespondentsAsync(ct);

            List<PaperlessCustomField> customFields;
            try
            {
                customFields = await client.ListCustomFieldsAsync(ct);
            }
            catch
            {
                // Ältere Paperless-Versionen ohne Custom-Fields-Endpunkt o.ä. - lieber
                // ohne Custom-Field-Kontext weitermachen als die ganze Suche scheitern zu lassen.
                customFields = new();
            }

            _snapshot = new PaperlessMetadataSnapshot(tags, docTypes, correspondents, customFields);
            _lastRefresh = DateTime.UtcNow;
            return _snapshot;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Erzwingt beim nächsten Aufruf ein frisches Neuladen (z.B. nach einem Reset).</summary>
    public void Invalidate() => _lastRefresh = DateTime.MinValue;
}
