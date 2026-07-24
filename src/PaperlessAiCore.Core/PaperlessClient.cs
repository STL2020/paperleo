using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PaperlessAiCore.Core;

/// <summary>
/// Schlanker Client für die Paperless-ngx REST API.
/// Es werden ausschliesslich offizielle API-Endpunkte verwendet (>= API Version 6),
/// keine direkten Datenbankzugriffe. Verbindung wird pro Aufruf aus den
/// admin-konfigurierten Settings gebaut, nicht aus fixen ENV-Variablen.
///
/// Robustheit: GET/PATCH werden bei Timeout/Verbindungsfehlern automatisch mit
/// Exponential Backoff wiederholt (idempotent, daher sicher). POST (Anlegen von
/// Tags/Korrespondenten/Dokumenttypen) wird NICHT blind auf HTTP-Ebene wiederholt
/// (Duplikat-Risiko), sondern die komplette "erst suchen, dann anlegen"-Operation
/// wird bei Fehlern erneut versucht - dabei würde ein zwischenzeitlich trotz
/// Timeout erfolgreich angelegter Eintrag beim erneuten Suchen gefunden und nicht
/// doppelt angelegt.
/// </summary>
public class PaperlessClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private const int MaxIdempotentAttempts = 2;
    private const int MaxCreateAttempts = 2;

    /// <summary>
    /// Wird bei jedem Schreib-Vorgang (PATCH, bulk_edit) mit einer Log-Zeile
    /// aufgerufen (Start/Erfolg/Fehler inkl. Dauer) - der Aufrufer kann das an
    /// eine Datei/einen Logger anhängen, um den Schreibprozess nachvollziehbar
    /// zu machen. Absichtlich ein simples Delegate statt ILogger-Abhängigkeit,
    /// damit Core frei von Api-spezifischer Logging-Infrastruktur bleibt.
    /// </summary>
    public Action<string>? OnWriteLog { get; set; }

    /// <summary>
    /// Paperless-ngx schickt bei jeder Antwort einen "X-Version"-Header mit der
    /// tatsächlichen Server-Version mit. Wird hier gespiegelt, damit das Dashboard
    /// die ECHTE Paperless-Version anzeigen kann, statt sie zu erfinden.
    /// </summary>
    public string? LastKnownServerVersion { get; private set; }

    public PaperlessClient(HttpClient http, PaperlessConnectionConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.BaseUrl) || string.IsNullOrWhiteSpace(config.ApiToken))
        {
            throw new InvalidOperationException("Paperless-URL und API-Token müssen konfiguriert sein.");
        }

        _http = http;
        _http.BaseAddress = new Uri(config.BaseUrl.TrimEnd('/'));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", config.ApiToken);
        _http.DefaultRequestHeaders.Remove("Accept");
        _http.DefaultRequestHeaders.Add("Accept", "application/json; version=6");
    }

    public async Task<PaperlessListResponse<PaperlessDocument>> ListDocumentsAsync(
        Dictionary<string, string>? queryParams = null, CancellationToken ct = default)
    {
        var url = BuildUrl("/api/documents/", queryParams);
        var resp = await SendWithRetryAsync(HttpMethod.Get, url, null, ct);
        await EnsureSuccessAsync(resp, null, ct);
        return (await resp.Content.ReadFromJsonAsync<PaperlessListResponse<PaperlessDocument>>(JsonOpts, ct))!;
    }

    public async Task<PaperlessDocument> GetDocumentAsync(int id, CancellationToken ct = default)
    {
        var resp = await SendWithRetryAsync(HttpMethod.Get, $"/api/documents/{id}/", null, ct);
        await EnsureSuccessAsync(resp, null, ct);
        return (await resp.Content.ReadFromJsonAsync<PaperlessDocument>(JsonOpts, ct))!;
    }

    /// <summary>
    /// Direktes PATCH auf das Dokument - wird bei uns NUR noch für Titel/Datum
    /// genutzt (kleines, schnelles Payload). Tags/Korrespondent/Dokumenttyp laufen
    /// über bulk_edit (siehe unten), da PATCH mit vielen Feldern gleichzeitig bei
    /// großen Paperless-Instanzen spürbar länger dauern kann (Reindexierung).
    /// </summary>
    public async Task UpdateDocumentAsync(int id, object patchBody, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(patchBody);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        OnWriteLog?.Invoke($"PATCH /api/documents/{id}/ START body={json}");
        try
        {
            var resp = await SendWithRetryAsync(HttpMethod.Patch, $"/api/documents/{id}/", json, ct);
            await EnsureSuccessAsync(resp, json, ct);
            OnWriteLog?.Invoke($"PATCH /api/documents/{id}/ OK ({sw.ElapsedMilliseconds}ms)");
        }
        catch (Exception ex)
        {
            OnWriteLog?.Invoke($"PATCH /api/documents/{id}/ FEHLER nach {sw.ElapsedMilliseconds}ms: {ex.Message}");
            throw;
        }
    }

    // ---------- Bulk-Edit (asynchron auf Paperless-Seite - Server antwortet, sobald
    // der Task eingereiht ist, nicht erst wenn er komplett verarbeitet wurde) ----------

    /// <summary>
    /// POST /api/documents/bulk_edit/ - laut Paperless-ngx-Doku werden Bulk-Edit-
    /// Operationen ASYNCHRON verarbeitet. Der HTTP-Request kehrt zurück, sobald der
    /// Task eingereiht wurde, nicht erst nach vollständiger Verarbeitung - dadurch
    /// deutlich weniger anfällig für Timeouts als ein PATCH mit vielen Feldern auf
    /// einer großen/trägen Paperless-Instanz.
    /// </summary>
    public async Task BulkEditAsync(int documentId, string method, object? parameters, CancellationToken ct = default)
    {
        var payload = new { documents = new[] { documentId }, method, parameters = parameters ?? new { } };
        var json = JsonSerializer.Serialize(payload);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        OnWriteLog?.Invoke($"bulk_edit #{documentId} START method={method} params={JsonSerializer.Serialize(parameters)}");
        try
        {
            // bulk_edit ist auf Anwendungsebene idempotent (z.B. "set_correspondent"
            // zweimal mit demselben Wert hat dasselbe Ergebnis) - Retry ist sicher.
            var resp = await SendWithRetryAsync(HttpMethod.Post, "/api/documents/bulk_edit/", json, ct);
            await EnsureSuccessAsync(resp, json, ct);
            OnWriteLog?.Invoke($"bulk_edit #{documentId} OK method={method} ({sw.ElapsedMilliseconds}ms)");
        }
        catch (Exception ex)
        {
            OnWriteLog?.Invoke($"bulk_edit #{documentId} FEHLER method={method} nach {sw.ElapsedMilliseconds}ms: {ex.Message}");
            throw;
        }
    }

    public Task SetCorrespondentBulkAsync(int documentId, int correspondentId, CancellationToken ct = default) =>
        BulkEditAsync(documentId, "set_correspondent", new { correspondent = correspondentId }, ct);

    public Task SetDocumentTypeBulkAsync(int documentId, int documentTypeId, CancellationToken ct = default) =>
        BulkEditAsync(documentId, "set_document_type", new { document_type = documentTypeId }, ct);

    public Task ModifyTagsBulkAsync(int documentId, List<int> addTagIds, CancellationToken ct = default) =>
        BulkEditAsync(documentId, "modify_tags", new { add_tags = addTagIds, remove_tags = Array.Empty<int>() }, ct);

    public async Task<List<PaperlessTag>> ListTagsAsync(CancellationToken ct = default)
    {
        var results = new List<PaperlessTag>();
        string? url = BuildUrl("/api/tags/", new() { ["page_size"] = "200" });
        while (url is not null)
        {
            var resp = await SendWithRetryAsync(HttpMethod.Get, url, null, ct);
            await EnsureSuccessAsync(resp, null, ct);
            var page = (await resp.Content.ReadFromJsonAsync<PaperlessListResponse<PaperlessTag>>(JsonOpts, ct))!;
            results.AddRange(page.Results);
            url = page.Next;
        }
        return results;
    }

    public Task<int> GetOrCreateTagAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Tag-Name ist leer - kann keinen Tag ohne Namen in Paperless anlegen.");
        }

        return RetryCreateAsync(async () =>
        {
            var tags = await ListTagsAsync(ct);
            var existing = tags.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing.Id;

            var created = await PostJsonAsync<PaperlessTag>("/api/tags/", new { name }, ct);
            return created.Id;
        }, ct);
    }

    /// <summary>
    /// Legt einen Tag OHNE vorheriges erneutes Auflisten an - für Batch-Verarbeitung
    /// (mehrere Tags pro Dokument), wenn der Aufrufer die Tag-Liste bereits selbst
    /// einmalig geladen und geprüft hat. Vermeidet unnötige N+1 List-Requests.
    /// </summary>
    public async Task<int> CreateTagAsync(string name, CancellationToken ct = default)
    {
        var created = await RetryCreateAsync(async () =>
        {
            var t = await PostJsonAsync<PaperlessTag>("/api/tags/", new { name }, ct);
            return t.Id;
        }, ct);
        return created;
    }

    public async Task<List<PaperlessUser>> ListUsersAsync(CancellationToken ct = default)
    {
        var results = new List<PaperlessUser>();
        string? url = BuildUrl("/api/users/", new() { ["page_size"] = "100" });
        while (url is not null)
        {
            var resp = await SendWithRetryAsync(HttpMethod.Get, url, null, ct);
            if (!resp.IsSuccessStatusCode) break; // ältere Paperless-Versionen ohne /api/users/
            await EnsureSuccessAsync(resp, null, ct);
            var page = (await resp.Content.ReadFromJsonAsync<PaperlessListResponse<PaperlessUser>>(JsonOpts, ct))!;
            results.AddRange(page.Results.Where(u => u.IsActive));
            url = page.Next;
        }
        return results;
    }

    public async Task<bool> SetDocumentOwnerAsync(int docId, int userId, CancellationToken ct = default)
    {
        try
        {
            await UpdateDocumentAsync(docId, new { owner = userId }, ct);
            return true;
        }
        catch { return false; }
    }

        public async Task<List<PaperlessCorrespondent>> ListCorrespondentsAsync(CancellationToken ct = default)
    {
        var results = new List<PaperlessCorrespondent>();
        string? url = BuildUrl("/api/correspondents/", new() { ["page_size"] = "200" });
        while (url is not null)
        {
            var resp = await SendWithRetryAsync(HttpMethod.Get, url, null, ct);
            await EnsureSuccessAsync(resp, null, ct);
            var page = (await resp.Content.ReadFromJsonAsync<PaperlessListResponse<PaperlessCorrespondent>>(JsonOpts, ct))!;
            results.AddRange(page.Results);
            url = page.Next;
        }
        return results;
    }

    public Task<int> GetOrCreateCorrespondentAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Korrespondenten-Name ist leer - kann keinen Korrespondenten ohne Namen in Paperless anlegen.");
        }

        return RetryCreateAsync(async () =>
        {
            var correspondents = await ListCorrespondentsAsync(ct);
            var existing = correspondents.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing.Id;

            var created = await PostJsonAsync<PaperlessCorrespondent>("/api/correspondents/", new { name }, ct);
            return created.Id;
        }, ct);
    }

    /// <summary>Legt einen Korrespondenten OHNE vorheriges erneutes Auflisten an (Batch-Nutzung).</summary>
    public Task<int> CreateCorrespondentAsync(string name, CancellationToken ct = default) =>
        RetryCreateAsync(async () =>
        {
            var c = await PostJsonAsync<PaperlessCorrespondent>("/api/correspondents/", new { name }, ct);
            return c.Id;
        }, ct);

    // ---------- Dokumenttypen ----------
    // In Paperless-ngx eine eigene Entität (wie Tags/Korrespondenten), nicht nur ein Textfeld.

    public async Task<List<PaperlessDocumentType>> ListDocumentTypesAsync(CancellationToken ct = default)
    {
        var results = new List<PaperlessDocumentType>();
        string? url = BuildUrl("/api/document_types/", new() { ["page_size"] = "200" });
        while (url is not null)
        {
            var resp = await SendWithRetryAsync(HttpMethod.Get, url, null, ct);
            await EnsureSuccessAsync(resp, null, ct);
            var page = (await resp.Content.ReadFromJsonAsync<PaperlessListResponse<PaperlessDocumentType>>(JsonOpts, ct))!;
            results.AddRange(page.Results);
            url = page.Next;
        }
        return results;
    }

    public Task<int> GetOrCreateDocumentTypeAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Dokumenttyp-Name ist leer - kann keinen Dokumenttyp ohne Namen in Paperless anlegen.");
        }

        return RetryCreateAsync(async () =>
        {
            var types = await ListDocumentTypesAsync(ct);
            var existing = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing.Id;

            var created = await PostJsonAsync<PaperlessDocumentType>("/api/document_types/", new { name }, ct);
            return created.Id;
        }, ct);
    }

    /// <summary>Legt einen Dokumenttyp OHNE vorheriges erneutes Auflisten an (Batch-Nutzung).</summary>
    public Task<int> CreateDocumentTypeAsync(string name, CancellationToken ct = default) =>
        RetryCreateAsync(async () =>
        {
            var t = await PostJsonAsync<PaperlessDocumentType>("/api/document_types/", new { name }, ct);
            return t.Id;
        }, ct);

    // ---------- Custom Fields ----------

    /// <summary>Löscht einen Korrespondenten unwiderruflich (für "Vollständiger Reset").</summary>
    public async Task DeleteCorrespondentAsync(int id, CancellationToken ct = default)
    {
        var resp = await SendWithRetryAsync(HttpMethod.Delete, $"/api/correspondents/{id}/", null, ct);
        if (resp.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(resp, null, ct);
        }
    }

    /// <summary>Löscht einen Dokumenttyp unwiderruflich (für "Vollständiger Reset").</summary>
    public async Task DeleteDocumentTypeAsync(int id, CancellationToken ct = default)
    {
        var resp = await SendWithRetryAsync(HttpMethod.Delete, $"/api/document_types/{id}/", null, ct);
        if (resp.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(resp, null, ct);
        }
    }

    /// <summary>Löscht einen Tag unwiderruflich (für "Vollständiger Reset").</summary>
    public async Task DeleteTagAsync(int id, CancellationToken ct = default)
    {
        var resp = await SendWithRetryAsync(HttpMethod.Delete, $"/api/tags/{id}/", null, ct);
        if (resp.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(resp, null, ct);
        }
    }


    public async Task<List<PaperlessCustomField>> ListCustomFieldsAsync(CancellationToken ct = default)
    {
        var results = new List<PaperlessCustomField>();
        string? url = BuildUrl("/api/custom_fields/", new() { ["page_size"] = "200" });
        while (url is not null)
        {
            var resp = await SendWithRetryAsync(HttpMethod.Get, url, null, ct);
            await EnsureSuccessAsync(resp, null, ct);
            var page = (await resp.Content.ReadFromJsonAsync<PaperlessListResponse<PaperlessCustomField>>(JsonOpts, ct))!;
            results.AddRange(page.Results);
            url = page.Next;
        }
        return results;
    }

    /// <summary>
    /// Sucht ein Custom Field per Name (case-insensitive) und gibt es inkl. seines
    /// TATSÄCHLICHEN Datentyps zurück. Existiert es noch nicht, wird es neu als
    /// "string" angelegt - das ist der sicherste Default für neu erstellte Felder.
    /// Bereits existierende Felder (z.B. von dir manuell als "monetary" angelegt)
    /// werden unverändert übernommen, NICHT auf "string" zurückgesetzt - dadurch
    /// weiß der Aufrufer, wie der Wert formatiert werden muss (siehe DocumentProcessor).
    /// </summary>
    public async Task<PaperlessCustomField> FindOrCreateCustomFieldAsync(string name, CancellationToken ct = default)
    {
        var fields = await ListCustomFieldsAsync(ct);
        var existing = fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        return await RetryCreateFieldAsync(name, "string", ct);
    }

    /// <summary>Wie FindOrCreateCustomFieldAsync, legt ein neues Feld aber als "boolean" an.</summary>
    public async Task<PaperlessCustomField> FindOrCreateBooleanCustomFieldAsync(string name, CancellationToken ct = default)
    {
        var fields = await ListCustomFieldsAsync(ct);
        var existing = fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        return await RetryCreateFieldAsync(name, "boolean", ct);
    }

    private async Task<PaperlessCustomField> RetryCreateFieldAsync(string name, string dataType, CancellationToken ct)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxCreateAttempts; attempt++)
        {
            try
            {
                return await PostJsonAsync<PaperlessCustomField>("/api/custom_fields/", new { name, data_type = dataType }, ct);
            }
            catch (Exception ex) when (ex is TimeoutException or HttpRequestException)
            {
                lastError = ex;
                if (attempt < MaxCreateAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                }
            }
        }
        throw lastError!;
    }

    private static string BuildUrl(string path, Dictionary<string, string>? queryParams)
    {
        if (queryParams is null || queryParams.Count == 0) return path;
        var query = string.Join("&", queryParams.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{path}?{query}";
    }

    private async Task<T> PostJsonAsync<T>(string url, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        // POST bewusst NUR EIN Versuch auf HTTP-Ebene (kein Duplikat-Risiko durch
        // blindes Retry) - Wiederholung passiert eine Ebene höher in RetryCreateAsync.
        var resp = await SendOnceAsync(HttpMethod.Post, url, json, ct);
        await EnsureSuccessAsync(resp, json, ct);
        return (await resp.Content.ReadFromJsonAsync<T>(JsonOpts, ct))!;
    }

    /// <summary>
    /// Wiederholt eine komplette "erst suchen, dann ggf. anlegen"-Operation bei
    /// Timeout/Verbindungsfehlern. Sicher gegenüber Duplikaten, weil ein erneuter
    /// Versuch zuerst wieder sucht - ein trotz Timeout erfolgreich angelegter
    /// Eintrag wird beim zweiten Versuch gefunden statt erneut angelegt.
    /// </summary>
    private static async Task<int> RetryCreateAsync(Func<Task<int>> operation, CancellationToken ct)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxCreateAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (ex is TimeoutException or HttpRequestException)
            {
                lastError = ex;
                if (attempt < MaxCreateAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                }
            }
        }
        throw lastError!;
    }

    /// <summary>GET/PATCH sind idempotent - werden bei Timeout/Verbindungsfehlern automatisch mit Backoff wiederholt.</summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpMethod method, string url, string? jsonBody, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxIdempotentAttempts; attempt++)
        {
            try
            {
                return await SendOnceAsync(method, url, jsonBody, ct);
            }
            catch (Exception ex) when (ex is TimeoutException or HttpRequestException)
            {
                if (attempt == MaxIdempotentAttempts)
                {
                    throw new HttpRequestException(
                        $"Paperless-ngx antwortet nach {MaxIdempotentAttempts} Versuchen weiterhin nicht (zuletzt: {ex.Message}).", ex);
                }
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }
        }

        // Unerreichbar (die Schleife kehrt entweder per return zurück oder wirft im letzten Versuch), nur für den Compiler.
        throw new InvalidOperationException("SendWithRetryAsync: unerwarteter Kontrollfluss.");
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string url, string? jsonBody, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (jsonBody is not null)
        {
            request.Content = BuildJsonContent(jsonBody);
        }

        try
        {
            var resp = await _http.SendAsync(request, ct);
            if (resp.Headers.TryGetValues("X-Version", out var versions))
            {
                LastKnownServerVersion = versions.FirstOrDefault();
            }
            return resp;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Paperless-ngx antwortet nicht innerhalb von {_http.Timeout.TotalSeconds:0}s bei {method} {_http.BaseAddress}{url.TrimStart('/')}.");
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            // Nicht Paperless hat abgebrochen, sondern der AUFRUFER (z.B. Browser-Tab
            // geschlossen/neu geladen, oder das Timeout des aufrufenden HTTP-Clients
            // wurde überschritten). Klar benennen statt rohen Stacktrace durchzureichen -
            // das spart bei der nächsten Fehlersuche viel Zeit.
            throw new OperationCanceledException(
                $"Die Anfrage an Paperless-ngx ({method} {url.TrimStart('/')}) wurde vom Aufrufer abgebrochen " +
                "(z.B. Browser-Tab geschlossen/neu geladen oder Client-Timeout überschritten) - nicht durch einen " +
                "Paperless-Verbindungsfehler.", ct);
        }
    }

    private static StringContent BuildJsonContent(string json)
    {
        // Bewusst OHNE "charset"-Parameter im Content-Type: .NETs Standard-JSON-Versand
        // setzt "application/json; charset=utf-8", was bei manchen Django-REST-Framework-
        // Konfigurationen (Basis von Paperless-ngx) dazu führte, dass der Body gar nicht
        // erst geparst wurde. Reines "application/json" ist der sicherste Nenner.
        var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    /// <summary>
    /// Wirft bei Fehlern eine aussagekräftige Exception inkl. HTTP-Methode, URL,
    /// Statuscode, dem GESENDETEN Body (falls bekannt) UND dem Response-Body
    /// (Paperless liefert bei 400 meist ein JSON mit den genauen
    /// Validierungsfehlern pro Feld).
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, string? sentBody, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;

        var responseBody = await resp.Content.ReadAsStringAsync(ct);
        var method = resp.RequestMessage?.Method.Method ?? "?";
        var uri = resp.RequestMessage?.RequestUri?.ToString() ?? "?";
        var sentInfo = sentBody is not null ? $" | Gesendet: {sentBody}" : "";
        throw new HttpRequestException(
            $"Paperless-API-Fehler {(int)resp.StatusCode} bei {method} {uri}: {responseBody}{sentInfo}");
    }
}
