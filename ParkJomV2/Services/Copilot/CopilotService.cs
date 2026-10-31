using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ParkJomV2.DTOs;

namespace ParkJomV2.Services.Copilot;

/// <summary>
/// Runs the Gemini function-calling loop for one commuter chat request:
/// model → tool calls → tool results → model … until a final text answer.
/// Includes a 5-minute response cache and compact prompt/history to minimize RPD &amp; token usage.
/// </summary>
public class CopilotService
{
    private const int MaxHistoryTurns = 4;
    private const int MaxTurnChars = 400;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly GeminiClient _gemini;
    private readonly CopilotToolExecutor _tools;
    private readonly IMemoryCache _cache;
    private readonly GeminiOptions _options;
    private readonly ILogger<CopilotService> _logger;

    public CopilotService(
        GeminiClient gemini,
        CopilotToolExecutor tools,
        IMemoryCache cache,
        IOptions<GeminiOptions> options,
        ILogger<CopilotService> logger)
    {
        _gemini = gemini;
        _tools = tools;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CopilotChatResponse> ChatAsync(CopilotChatRequest request, int userId, CancellationToken ct)
    {
        var cacheKey = BuildCacheKey(request);
        if (_cache.TryGetValue<CopilotChatResponse>(cacheKey, out var cached) && cached != null)
        {
            _logger.LogInformation("Copilot cache hit for user={UserId}", userId);
            return cached;
        }

        var stopwatch = Stopwatch.StartNew();
        var contents = BuildContents(request);
        var declarations = CopilotToolExecutor.Declarations();
        var systemPrompt = BuildSystemPrompt(request);
        var toolsUsed = new List<string>();
        var maxRounds = Math.Clamp(_options.MaxToolRounds, 1, 5);

        for (var round = 0; round <= maxRounds; round++)
        {
            var response = await _gemini.GenerateAsync(systemPrompt, contents, declarations, ct);
            var content = response["candidates"]?[0]?["content"] as JsonObject;
            var parts = content?["parts"] as JsonArray;

            if (content == null || parts == null || parts.Count == 0)
            {
                var finish = response["candidates"]?[0]?["finishReason"]?.GetValue<string>() ?? "UNKNOWN";
                _logger.LogWarning("Copilot empty response for user {UserId}, finishReason={Finish}", userId, finish);
                return Finish("Sorry, I couldn't answer that. Could you rephrase your request?", cacheable: false);
            }

            var calls = parts
                .OfType<JsonObject>()
                .Select(p => p["functionCall"] as JsonObject)
                .Where(c => c != null)
                .Cast<JsonObject>()
                .ToList();

            if (calls.Count == 0 || round == maxRounds)
            {
                var text = string.Concat(parts
                    .OfType<JsonObject>()
                    .Where(p => p["thought"]?.GetValue<bool>() != true)
                    .Select(p => p["text"]?.GetValue<string>() ?? string.Empty)).Trim();

                return Finish(
                    string.IsNullOrEmpty(text) ? "I found some options. Take a look at the cards below." : text,
                    cacheable: true);
            }

            // Echo the model turn verbatim (keeps thoughtSignature intact), then answer each call.
            contents.Add(content.DeepClone());
            var responseParts = new JsonArray();
            foreach (var call in calls)
            {
                var name = call["name"]?.GetValue<string>() ?? string.Empty;
                toolsUsed.Add(name);
                var result = await _tools.ExecuteAsync(name, call["args"] as JsonObject, ct);
                var functionResponse = new JsonObject
                {
                    ["name"] = name,
                    ["response"] = new JsonObject { ["result"] = result }
                };
                if (call["id"] is JsonNode id) functionResponse["id"] = id.DeepClone();
                responseParts.Add(new JsonObject { ["functionResponse"] = functionResponse });
            }
            contents.Add(new JsonObject { ["role"] = "user", ["parts"] = responseParts });
        }

        return Finish("Sorry, I couldn't finish planning that. Try a simpler request.", cacheable: false);

        CopilotChatResponse Finish(string reply, bool cacheable)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Copilot user={UserId} tools=[{Tools}] cards={Cards} latencyMs={Latency}",
                userId, string.Join(",", toolsUsed), _tools.Cards.Count, stopwatch.ElapsedMilliseconds);

            var chatResponse = new CopilotChatResponse
            {
                Reply = reply,
                Context = _tools.Context,
                Cards = _tools.Cards,
                ToolsUsed = toolsUsed,
            };

            if (cacheable)
            {
                _cache.Set(cacheKey, chatResponse, CacheTtl);
            }
            return chatResponse;
        }
    }

    private static string BuildCacheKey(CopilotChatRequest request)
    {
        var msg = request.Message.Trim().ToLowerInvariant();
        var latGrid = request.Lat.HasValue ? Math.Round(request.Lat.Value, 2).ToString("0.00") : "-";
        var lngGrid = request.Lng.HasValue ? Math.Round(request.Lng.Value, 2).ToString("0.00") : "-";
        var lastCtx = request.History.LastOrDefault(h => !string.IsNullOrWhiteSpace(h.Context))?.Context ?? "-";
        return $"copilot:v2:{msg}|{latGrid},{lngGrid}|{lastCtx}";
    }

    private static JsonArray BuildContents(CopilotChatRequest request)
    {
        var contents = new JsonArray();
        foreach (var turn in request.History.TakeLast(MaxHistoryTurns))
        {
            var text = turn.Text.Length <= MaxTurnChars ? turn.Text : turn.Text[..MaxTurnChars];
            if (turn.Role == "model" && !string.IsNullOrWhiteSpace(turn.Context))
                text += $"\n[Ctx: {turn.Context}]";

            contents.Add(new JsonObject
            {
                ["role"] = turn.Role == "model" ? "model" : "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = text })
            });
        }

        contents.Add(new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray(new JsonObject { ["text"] = request.Message.Trim() })
        });
        return contents;
    }

    private static string BuildSystemPrompt(CopilotChatRequest request)
    {
        var now = CopilotToolExecutor.NowInKl();
        var loc = request.Lat.HasValue && request.Lng.HasValue
            ? $"{request.Lat.Value:0.####},{request.Lng.Value:0.####}"
            : "unknown";

        return $"""
            You are ParkJom Copilot (Klang Valley park-and-ride assistant). Now: {now:yyyy-MM-dd ddd HH:mm} MYT. User GPS: {loc}.
            - findStationsNear & searchStations already return availableBays; do NOT call getParkingNearStation again for those stations.
            - Only cite bays, prices (RM), walk times & trains from tool output. Never invent data.
            - Bookings are whole days (startDate..endDate). proposeBooking only shows a card; tell the user to tap Review & reserve.
            - No covered/EV data exists; state briefly if asked.
            - Keep replies <= 3 short sentences in the user's language (EN/BM/中文); UI cards show full details.
            """;
    }
}
