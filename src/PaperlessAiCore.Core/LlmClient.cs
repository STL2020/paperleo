using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PaperlessAiCore.Core;

public class LlmChatMessage
{
    public string Role { get; set; } = "";
    public string? Content { get; set; }
    public List<LlmToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
}

public class LlmToolCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ArgumentsJson { get; set; } = "{}";
}

/// <summary>Token-Verbrauch eines einzelnen LLM-Aufrufs (aus dem "usage"-Feld der Antwort).</summary>
public record LlmUsage(int? PromptTokens, int? CompletionTokens, int? TotalTokens);

public class LlmChatResult
{
    public string? Content { get; set; }
    public List<LlmToolCall> ToolCalls { get; set; } = new();
    public LlmUsage? Usage { get; set; }
}

public record LlmCompletion(string Content, LlmUsage? Usage);

/// <summary>
/// Minimaler Client gegen einen OpenAI-kompatiblen Chat-Completions-Endpunkt
/// inkl. Tool-/Function-Calling. Funktioniert gegen OpenAI, Azure-kompatible
/// Proxys oder lokale Server wie Ollama (OpenAI-kompatible Schnittstelle).
/// Provider/Model/Key kommen aus den admin-konfigurierten Settings, nicht aus ENV.
/// </summary>
public class LlmClient
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly double _temperature;
    private readonly int? _maxTokens;

    public LlmClient(HttpClient http, LlmConnectionConfig config)
    {
        _http = http;
        _model = config.Model.Trim();
        _temperature = config.Temperature;
        _maxTokens = config.MaxTokens;
        var apiBase = string.IsNullOrWhiteSpace(config.ApiBase) ? null : config.ApiBase.Trim();
        var baseUrl = string.IsNullOrWhiteSpace(apiBase) ? "https://api.openai.com/v1" : apiBase!.TrimEnd('/');
        _http.BaseAddress = new Uri(baseUrl + "/");
        var apiKey = config.ApiKey?.Trim();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public async Task<LlmCompletion> CompleteJsonAsync(string systemPrompt, string userPrompt, int? maxTokensOverride = null, CancellationToken ct = default)
    {
        // Bewusst FEST auf Temperature 0 für die strukturierte Metadaten-Extraktion,
        // unabhängig von der in den Settings konfigurierten "Kreativität" - Extraktion
        // muss deterministisch/präzise bleiben. Die konfigurierbare Temperature gilt
        // nur für den Such-Agenten (ChatAsync), wo etwas "Kreativität" bei der Formulierung
        // der Antwort sinnvoll ist.
        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["temperature"] = 0,
            ["response_format"] = new { type = "json_object" },
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
        };
        var effectiveMaxTokens = maxTokensOverride ?? _maxTokens;
        if (effectiveMaxTokens is > 0) body["max_tokens"] = effectiveMaxTokens;

        var resp = await PostAsync(body, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"LLM-Aufruf fehlgeschlagen ({(int)resp.StatusCode}): {raw}");
        }

        using var doc = JsonDocument.Parse(raw);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
               ?? throw new InvalidOperationException("LLM-Antwort enthielt keinen Content.");
        return new LlmCompletion(content, ParseUsage(doc.RootElement));
    }

    public async Task<LlmChatResult> ChatAsync(List<LlmChatMessage> messages, List<object>? tools = null, CancellationToken ct = default)
    {
        var payloadMessages = messages.Select(BuildMessagePayload).ToList();

        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["temperature"] = _temperature,
            ["messages"] = payloadMessages,
        };
        if (_maxTokens is > 0) body["max_tokens"] = _maxTokens;
        if (tools is { Count: > 0 })
        {
            body["tools"] = tools;
            body["tool_choice"] = "auto";
        }

        var resp = await PostAsync(body, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"LLM-Aufruf fehlgeschlagen ({(int)resp.StatusCode}): {raw}");
        }

        using var doc = JsonDocument.Parse(raw);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

        var result = new LlmChatResult
        {
            Content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null,
            Usage = ParseUsage(doc.RootElement),
        };

        if (message.TryGetProperty("tool_calls", out var toolCallsEl) && toolCallsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in toolCallsEl.EnumerateArray())
            {
                result.ToolCalls.Add(new LlmToolCall
                {
                    Id = tc.GetProperty("id").GetString() ?? "",
                    Name = tc.GetProperty("function").GetProperty("name").GetString() ?? "",
                    ArgumentsJson = tc.GetProperty("function").GetProperty("arguments").GetString() ?? "{}",
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Liest das OpenAI-kompatible "usage"-Objekt aus der Antwort, falls vorhanden.
    /// Nicht jeder Provider liefert das zuverlässig (z.B. manche Ollama-Versionen) -
    /// in dem Fall bleibt Usage einfach null, statt einen Fehler zu werfen.
    /// </summary>
    private static LlmUsage? ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        int? Get(string name) => usage.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

        return new LlmUsage(Get("prompt_tokens"), Get("completion_tokens"), Get("total_tokens"));
    }

    /// <summary>
    /// Zentraler Versand-Punkt. Ein Retry bei Timeout (max. 2 Versuche - LLM-Aufrufe
    /// kosten bei Cloud-Providern Geld, daher bewusst zurückhaltender als bei
    /// Paperless). Wirft eine klare, auf den LLM-Provider bezogene Fehlermeldung.
    /// </summary>
    private async Task<HttpResponseMessage> PostAsync(object body, CancellationToken ct)
    {
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await _http.PostAsJsonAsync("chat/completions", body, ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                if (attempt == maxAttempts)
                {
                    throw new TimeoutException(
                        $"LLM-Provider ({_http.BaseAddress}) antwortet nach {maxAttempts} Versuchen nicht innerhalb von {_http.Timeout.TotalSeconds:0}s. " +
                        "Prüfe API-Base-URL, API-Key und ob der Provider (z.B. Gemini) von diesem Rechner aus erreichbar ist.");
                }
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        throw new InvalidOperationException("PostAsync: unerwarteter Kontrollfluss.");
    }

    private static object BuildMessagePayload(LlmChatMessage m)
    {
        if (m.Role == "tool")
        {
            return new { role = "tool", tool_call_id = m.ToolCallId, content = m.Content };
        }

        if (m.ToolCalls is { Count: > 0 })
        {
            return new
            {
                role = m.Role,
                content = m.Content,
                tool_calls = m.ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.Name, arguments = tc.ArgumentsJson },
                }),
            };
        }

        return new { role = m.Role, content = m.Content };
    }
}
