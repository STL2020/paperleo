using PaperlessAiCore.Shared;
using System.IO.Compression;
using System.Text.Json;

namespace PaperlessAiCore.Core;

/// <summary>
/// Erstellt ein vollständiges Backup einer Paperless-ngx Instanz:
/// - Alle Dokumente als PDF
/// - Alle Metadaten (Tags, Korrespondenten, Dokumenttypen, Custom Fields) als JSON
/// - Restore-Funktion spielt alle Metadaten zurück
/// </summary>
public static class PaperlessBackupService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // ── BACKUP ────────────────────────────────────────────────────────────────

    public static async Task<BackupResult> CreateBackupAsync(
        PaperlessClient paperless,
        IProgress<(int pct, string phase)>? progress = null,
        CancellationToken ct = default)
    {
        var result = new BackupResult();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"paperleo-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        try
        {
            progress?.Report((2, "Lade Metadaten …"));

            // ── 1) Metadaten laden ──────────────────────────────────────
            var tags         = await paperless.ListTagsAsync(ct);
            var correspondents = await paperless.ListCorrespondentsAsync(ct);
            var docTypes     = await paperless.ListDocumentTypesAsync(ct);
            var customFields = await paperless.ListCustomFieldsAsync(ct);

            await File.WriteAllTextAsync(Path.Combine(tmpDir, "tags.json"),
                JsonSerializer.Serialize(tags, JsonOpts), ct);
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "correspondents.json"),
                JsonSerializer.Serialize(correspondents, JsonOpts), ct);
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "document_types.json"),
                JsonSerializer.Serialize(docTypes, JsonOpts), ct);
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "custom_fields.json"),
                JsonSerializer.Serialize(customFields, JsonOpts), ct);

            progress?.Report((10, "Lade Dokumentenliste …"));

            // ── 2) Alle Dokumente laden (paginiert) ─────────────────────
            var allDocs = new List<PaperlessDocument>();
            var page    = 1;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var resp = await paperless.ListDocumentsAsync(new()
                {
                    ["page"]      = page.ToString(),
                    ["page_size"] = "100"
                }, ct);
                allDocs.AddRange(resp.Results);
                if (string.IsNullOrEmpty(resp.Next)) break;
                page++;
            }

            result.TotalDocuments = allDocs.Count;
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "documents_meta.json"),
                JsonSerializer.Serialize(allDocs, JsonOpts), ct);

            progress?.Report((15, $"{allDocs.Count} Dokumente gefunden — starte PDF-Download …"));

            // ── 3) PDF-Downloads ─────────────────────────────────────────
            var docsDir = Path.Combine(tmpDir, "documents");
            Directory.CreateDirectory(docsDir);

            int done = 0;
            var failed = new List<int>();

            foreach (var doc in allDocs)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var pdfBytes = await paperless.DownloadDocumentAsync(doc.Id, ct);

                    // Dateiname: ID_Korrespondent_Titel.pdf (sanitized)
                    var safe = string.Concat(
                        $"{doc.Id:D6}_",
                        SanitizeFilename(doc.Correspondent?.ToString() ?? ""),
                        "_",
                        SanitizeFilename(doc.Title ?? "untitled")
                    );
                    if (safe.Length > 200) safe = safe[..200];
                    await File.WriteAllBytesAsync(Path.Combine(docsDir, $"{safe}.pdf"), pdfBytes, ct);
                }
                catch
                {
                    failed.Add(doc.Id);
                }

                done++;
                var pct = 15 + (int)(done * 80.0 / allDocs.Count);
                if (done % 10 == 0 || done == allDocs.Count)
                    progress?.Report((pct, $"PDF {done}/{allDocs.Count} …"));
            }

            result.DownloadedDocuments = done - failed.Count;
            result.FailedDocumentIds   = failed;

            // ── 4) Backup-Manifest ────────────────────────────────────────
            var manifest = new BackupManifest
            {
                CreatedAt        = DateTime.UtcNow,
                PaperlessUrl     = "",
                TotalDocuments   = allDocs.Count,
                DownloadedPdfs   = result.DownloadedDocuments,
                FailedIds        = failed,
                TagCount         = tags.Count,
                CorrespondentCount = correspondents.Count,
                DocumentTypeCount  = docTypes.Count,
                CustomFieldCount   = customFields.Count,
                PaperLeoVersion  = "7.1.0"
            };
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "manifest.json"),
                JsonSerializer.Serialize(manifest, JsonOpts), ct);

            progress?.Report((96, "Erstelle ZIP …"));

            // ── 5) ZIP packen ─────────────────────────────────────────────
            var zipPath = Path.Combine(Path.GetTempPath(),
                $"paperless-backup-{DateTime.Now:yyyy-MM-dd_HHmm}.zip");
            ZipFile.CreateFromDirectory(tmpDir, zipPath, CompressionLevel.Optimal, false);

            result.ZipPath   = zipPath;
            result.ZipSizeMb = new FileInfo(zipPath).Length / 1_048_576.0;
            result.Success   = true;

            progress?.Report((100, $"Fertig — {result.DownloadedDocuments} PDFs, {result.ZipSizeMb:F1} MB"));
            return result;
        }
        finally
        {
            // Temp-Verzeichnis aufräumen
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    // ── RESTORE (Metadaten) ───────────────────────────────────────────────────

    public static async Task<RestoreResult> RestoreMetadataAsync(
        PaperlessClient paperless,
        Stream zipStream,
        IProgress<(int pct, string phase)>? progress = null,
        CancellationToken ct = default)
    {
        var result = new RestoreResult();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"paperleo-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        try
        {
            progress?.Report((5, "Entpacke ZIP …"));
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read))
                zip.ExtractToDirectory(tmpDir);

            // Manifest prüfen
            var manifestPath = Path.Combine(tmpDir, "manifest.json");
            if (!File.Exists(manifestPath))
                return new RestoreResult { Success = false, Error = "Kein gültiges paperLeo-Backup (manifest.json fehlt)." };

            var manifest = JsonSerializer.Deserialize<BackupManifest>(
                await File.ReadAllTextAsync(manifestPath, ct), JsonOpts)!;
            result.ManifestInfo = $"Backup vom {manifest.CreatedAt:dd.MM.yyyy HH:mm} · {manifest.TotalDocuments} Dokumente";
            _ = manifest; // used above

            progress?.Report((10, "Stelle Tags wieder her …"));

            // ── Tags ─────────────────────────────────────────────────────
            if (File.Exists(Path.Combine(tmpDir, "tags.json")))
            {
                var tags = JsonSerializer.Deserialize<List<PaperlessTag>>(
                    await File.ReadAllTextAsync(Path.Combine(tmpDir, "tags.json"), ct), JsonOpts)!;
                foreach (var tag in tags)
                {
                    ct.ThrowIfCancellationRequested();
                    try { await paperless.GetOrCreateTagAsync(tag.Name, ct); result.TagsRestored++; }
                    catch { result.Errors.Add($"Tag '{tag.Name}': Fehler"); }
                }
            }

            progress?.Report((30, "Stelle Korrespondenten wieder her …"));

            // ── Korrespondenten ───────────────────────────────────────────
            if (File.Exists(Path.Combine(tmpDir, "correspondents.json")))
            {
                var corrs = JsonSerializer.Deserialize<List<PaperlessCorrespondent>>(
                    await File.ReadAllTextAsync(Path.Combine(tmpDir, "correspondents.json"), ct), JsonOpts)!;
                foreach (var corr in corrs)
                {
                    ct.ThrowIfCancellationRequested();
                    try { await paperless.GetOrCreateCorrespondentAsync(corr.Name, ct); result.CorrespondentsRestored++; }
                    catch { result.Errors.Add($"Korrespondent '{corr.Name}': Fehler"); }
                }
            }

            progress?.Report((55, "Stelle Dokumenttypen wieder her …"));

            // ── Dokumenttypen ─────────────────────────────────────────────
            if (File.Exists(Path.Combine(tmpDir, "document_types.json")))
            {
                var types = JsonSerializer.Deserialize<List<PaperlessDocumentType>>(
                    await File.ReadAllTextAsync(Path.Combine(tmpDir, "document_types.json"), ct), JsonOpts)!;
                foreach (var t in types)
                {
                    ct.ThrowIfCancellationRequested();
                    try { await paperless.GetOrCreateDocumentTypeAsync(t.Name, ct); result.DocumentTypesRestored++; }
                    catch { result.Errors.Add($"Dokumenttyp '{t.Name}': Fehler"); }
                }
            }

            progress?.Report((75, "Stelle Custom Fields wieder her …"));

            // ── Custom Fields ─────────────────────────────────────────────
            if (File.Exists(Path.Combine(tmpDir, "custom_fields.json")))
            {
                var fields = JsonSerializer.Deserialize<List<PaperlessCustomField>>(
                    await File.ReadAllTextAsync(Path.Combine(tmpDir, "custom_fields.json"), ct), JsonOpts)!;
                foreach (var f in fields)
                {
                    ct.ThrowIfCancellationRequested();
                    try { await paperless.FindOrCreateCustomFieldAsync(f.Name, ct); result.CustomFieldsRestored++; }
                    catch { result.Errors.Add($"Custom Field '{f.Name}': Fehler"); }
                }
            }

            progress?.Report((100, "Wiederherstellung abgeschlossen."));
            result.Success = true;
            return result;
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    // ── FULL RESTORE (Metadaten + PDFs) ──────────────────────────────────────────

    public static async Task<RestoreResult> RestoreFullAsync(
        PaperlessClient paperless,
        Stream zipStream,
        IProgress<(int pct, string phase)>? progress = null,
        CancellationToken ct = default)
    {
        // Schritt 1: Metadaten wiederherstellen
        var metaResult = await RestoreMetadataAsync(paperless, zipStream, progress, ct);
        if (!metaResult.Success) return metaResult;

        return metaResult; // PDF-Upload separat via RestorePdfsAsync
    }

    /// <summary>
    /// Spielt alle PDFs aus dem Backup-ZIP über die Paperless-ngx API zurück.
    /// Nutzt POST /api/documents/post_document/ — den nativen Paperless-Import-Endpunkt.
    /// Paperless verarbeitet die Dokumente asynchron in der eigenen Queue.
    /// </summary>
    public static async Task<RestoreResult> RestorePdfsAsync(
        PaperlessClient paperless,
        Stream zipStream,
        IProgress<(int pct, string phase)>? progress = null,
        CancellationToken ct = default)
    {
        var result = new RestoreResult();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"paperleo-restore-pdf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        try
        {
            progress?.Report((5, "Entpacke ZIP …"));
            using (var zip = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read))
                zip.ExtractToDirectory(tmpDir);

            var docsDir = Path.Combine(tmpDir, "documents");
            if (!Directory.Exists(docsDir))
                return new RestoreResult { Success = false, Error = "Keine PDFs im Backup gefunden." };

            var pdfs = Directory.GetFiles(docsDir, "*.pdf");
            int done = 0;

            foreach (var pdf in pdfs)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var bytes    = await File.ReadAllBytesAsync(pdf, ct);
                    var filename = Path.GetFileName(pdf);

                    // Titel aus Dateiname extrahieren (Format: 000001_Korrespondent_Titel.pdf)
                    var parts = filename.Replace(".pdf", "").Split('_', 3);
                    var title = parts.Length >= 3 ? parts[2].Replace('_', ' ') : filename;

                    await paperless.UploadDocumentAsync(bytes, filename, title, ct);
                    result.TagsRestored++; // missbrauche als Upload-Counter
                }
                catch { result.Errors.Add($"PDF '{Path.GetFileName(pdf)}': Upload fehlgeschlagen"); }

                done++;
                var pct = 5 + (int)(done * 90.0 / pdfs.Length);
                if (done % 5 == 0 || done == pdfs.Length)
                    progress?.Report((pct, $"PDF {done}/{pdfs.Length} hochgeladen …"));
            }

            progress?.Report((100, $"{result.TagsRestored} PDFs erfolgreich hochgeladen."));
            result.Success = true;
            result.ManifestInfo = $"{result.TagsRestored}/{pdfs.Length} PDFs hochgeladen";
            return result;
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    // ── Hilfsmethoden ─────────────────────────────────────────────────────────

    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Where(c => !invalid.Contains(c)))
                     .Replace(' ', '_')
                     .Trim('_');
    }
}
