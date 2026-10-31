using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ParkJomV2.DTOs;

namespace ParkJomV2.Services.Copilot;

/// <summary>
/// Runs the Gemini function-calling loop for one commuter chat request:
/// model → tool calls → tool results → model … until a final text answer.
/// </summary>
public class CopilotService
{
    private const int MaxHistoryTurns = 10;

    private readonly GeminiClient _gemini;
    private readonly CopilotToolExecutor _tools;
    private readonly GeminiOptions _options;
    private readonly ILogger<CopilotService> _logger;

    public CopilotService(
        GeminiClient gemini,
        CopilotToolExecutor tools,
        IOptions<GeminiOptions> options,
        ILogger<CopilotService> logger)
    {
        _gemini = gemini;
        _tools = tools;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CopilotChatResponse> ChatAsync(CopilotChatRequest request, int userId, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var contents = BuildContents(request);
        var declarations = CopilotToolExecutor.Declarations();
        var systemPrompt = BuildSystemPrompt(request);
        var toolsUsed = new List<string>();
        var maxRounds = Math.Clamp(_options.MaxToolRounds, 1, 8);

        for (var round = 0; round <= maxRounds; round++)
        {
            var response = await _gemini.GenerateAsync(systemPrompt, contents, declarations, ct);
            var content = response["candidates"]?[0]?["content"] as JsonObject;
            var parts = content?["parts"] as JsonArray;

            if (content == null || parts == null || parts.Count == 0)
            {
                var finish = response["candidates"]?[0]?["finishReason"]?.GetValue<string>() ?? "UNKNOWN";
                _logger.LogWarning("Copilot empty response for user {UserId}, finishReason={Finish}", userId, finish);
                return Finish("Sorry, I couldn't answer that. Could you rephrase your request?");
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

                return Finish(string.IsNullOrEmpty(text)
                    ? "I found some options. Take a look at the cards below."
                    : text);
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

        return Finish("Sorry, I couldn't finish planning that. Try a simpler request.");

        CopilotChatResponse Finish(string reply)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Copilot user={UserId} tools=[{Tools}] cards={Cards} latencyMs={Latency}",
                userId, string.Join(",", toolsUsed), _tools.Cards.Count, stopwatch.ElapsedMilliseconds);

            return new CopilotChatResponse
            {
                Reply = reply,
                Context = _tools.Context,
                Cards = _tools.Cards,
                ToolsUsed = toolsUsed,
            };
        }
    }

    private static JsonArray BuildContents(CopilotChatRequest request)
    {
        var contents = new JsonArray();
        foreach (var turn in request.History.TakeLast(MaxHistoryTurns))
        {
            var text = turn.Text;
            if (turn.Role == "model" && !string.IsNullOrWhiteSpace(turn.Context))
                text += $"\n\n[Context from this turn: {turn.Context}]";

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
        var location = request.Lat.HasValue && request.Lng.HasValue
            ? $"{request.Lat.Value:0.######}, {request.Lng.Value:0.######} (use this for 'near me' / origin when not stated)"
            : "unknown (ask for, or geocode, the starting area when needed)";

        return $"""
            You are ParkJom Copilot, a park-and-ride assistant for commuters in the Klang Valley, Malaysia.
            ParkJom lets commuters book private parking bays near rail stations, then continue by train.

            Current date/time (Asia/Kuala_Lumpur): {now:yyyy-MM-dd (dddd) HH:mm}.
            User location: {location}.

            How to plan a trip:
            1. Resolve the origin and destination with geocodePlace (or the user location).
            2. Find stations near the origin (findStationsNear). Prefer a station on a line that reaches, or connects
               easily to, the destination; consider stations near the destination too.
            3. Call getParkingNearStation for the best 1-2 candidate stations.
            4. If the user mentions a departure or arrival time, use getStationDepartures and estimate when to leave.
            5. When the user wants to reserve, call proposeBooking. Resolve "today"/"tomorrow"/"Monday" into yyyy-MM-dd.

            Rules:
            - Only mention bays, prices, stations and train times returned by tools. Never invent data.
            - Bookings are for whole days (startDate to endDate inclusive). Times of day do not affect the booking.
            - proposeBooking never books or charges. Tell the user to tap Reserve on the card to confirm.
            - ParkJom has no "covered/sheltered" or EV-charging data. If asked, say so briefly and continue.
            - Train departures are frequency-based estimates; say "about" when quoting them.
            - Prices are in RM. Mention walk time from bay to station and the daily rate.
            - Cards with full details are shown under your reply, so keep text short: at most 4 sentences or a short list.
            - Reply in the user's language (English, Bahasa Melayu, or Chinese).
            - Politely decline requests unrelated to parking, transit, or ParkJom.
            """;
    }
}
