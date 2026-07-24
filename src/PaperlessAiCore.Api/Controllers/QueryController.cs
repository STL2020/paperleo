using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PaperlessAiCore.Api.Services;
using PaperlessAiCore.Core;
using PaperlessAiCore.Shared;

namespace PaperlessAiCore.Api.Controllers;

[ApiController]
[Route("api/query")]
public class QueryController(
    ISettingsService settingsService,
    IHttpClientFactory httpFactory,
    PaperlessMetadataCache metadataCache) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<QueryResponse>> Post([FromBody] QueryRequest req, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PaperlessUrl) || string.IsNullOrWhiteSpace(settings.LlmApiKey))
        {
            return BadRequest(new { error = "Bitte zuerst Paperless und den LLM-Provider konfigurieren." });
        }

        var paperless = new PaperlessClient(httpFactory.CreateClient("paperless"),
            new PaperlessConnectionConfig(settings.PaperlessUrl, settings.PaperlessApiToken));
        var llm = new LlmClient(httpFactory.CreateClient("llm"), settings.ToLlmConfig());

        // Metadaten immer frisch aus dem Cache (TTL 10 Min)
        var snapshotDto = await metadataCache.GetAsync(paperless, ct);
        var metadata = new PaperlessMetadataSnapshotLike(
            snapshotDto.Tags, snapshotDto.DocumentTypes,
            snapshotDto.Correspondents, snapshotDto.CustomFields);

        var proMode = LicenseCheck.IsProMode(settings.PremiumLicenseKey);
        var tools = new List<object> { Tools.SearchToolSchema };
        if (proMode) tools.Add(Tools.AggregateToolSchema);

        // Erster Such-Versuch mit allen Filtern
        var systemPrompt = Prompts.BuildAgentSystemPrompt(DateTime.Now, metadata);
        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = systemPrompt },
            new() { Role = "user", Content = req.Query },
        };

        var first = await llm.ChatAsync(messages, tools, ct);
        if (first.ToolCalls.Count == 0)
        {
            return Ok(new QueryResponse { Answer = first.Content ?? "" });
        }

        messages.Add(new LlmChatMessage { Role = "assistant", Content = first.Content, ToolCalls = first.ToolCalls });
        object? rawResults = null;
        var zeroResults = false;

        foreach (var call in first.ToolCalls)
        {
            var args = JsonDocument.Parse(call.ArgumentsJson).RootElement;
            object toolResult;

            if (call.Name == "aggregate_costs" && !proMode)
            {
                toolResult = new { error = "Kosten-Aggregation ist Teil von paperLeo PRO." };
            }
            else
            {
                toolResult = call.Name switch
                {
                    "search_documents" => await Tools.RunSearchDocumentsAsync(paperless, args, metadata),
                    "aggregate_costs"  => await Tools.RunAggregateCostsAsync(paperless, args, metadata),
                    _ => new { error = "Unbekanntes Tool" },
                };

                // Fallback erkennen: 0 Treffer bei search_documents
                if (call.Name == "search_documents" && toolResult is System.Text.Json.JsonElement je
                    && je.TryGetProperty("count", out var cnt) && cnt.GetInt32() == 0)
                {
                    zeroResults = true;
                }
                else if (call.Name == "search_documents")
                {
                    var json = JsonSerializer.Serialize(toolResult);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("count", out var c2) && c2.GetInt32() == 0)
                        zeroResults = true;
                }
            }

            rawResults = toolResult;
            messages.Add(new LlmChatMessage { Role = "tool", ToolCallId = call.Id, Content = JsonSerializer.Serialize(toolResult) });
        }

        // Fallback: 0 Treffer → zweiter Versuch mit NUR Volltext (alle strukturierten Filter weglassen)
        if (zeroResults && first.ToolCalls.Any(c => c.Name == "search_documents"))
        {
            var fallbackResult = await Tools.FallbackTextSearchAsync(paperless, req.Query, metadata);
            var fallbackJson = JsonSerializer.Serialize(fallbackResult);
            using var doc = JsonDocument.Parse(fallbackJson);
            var fallbackCount = doc.RootElement.TryGetProperty("count", out var fc) ? fc.GetInt32() : 0;

            if (fallbackCount > 0)
            {
                // Fallback-Ergebnisse als zusätzlichen Kontext mitgeben
                messages.Add(new LlmChatMessage
                {
                    Role = "user",
                    Content = $"Der erste Suchversuch lieferte 0 Treffer. Ich habe eine breitere Volltext-Suche durchgeführt und {fallbackCount} potenzielle Treffer gefunden:\n{fallbackJson}\n\nBitte werte diese aus und erkläre was gefunden wurde."
                });
                rawResults = fallbackResult;
            }
            else
            {
                messages.Add(new LlmChatMessage
                {
                    Role = "user",
                    Content = "Beide Suchversuche lieferten 0 Treffer. Erkläre welche Filter verwendet wurden und schlage konkrete Alternativen vor (z.B. andere Zeiträume, weniger Filter)."
                });
            }
        }

        var final = await llm.ChatAsync(messages, ct: ct);
        return Ok(new QueryResponse { Answer = final.Content ?? "", RawResults = rawResults });
    }
}
