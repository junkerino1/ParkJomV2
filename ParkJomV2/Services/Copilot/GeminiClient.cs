using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ParkJomV2.Services.Copilot;

public class GeminiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>
    /// Priority waterfall from best reasoning models (Tier 1: 20 RPD / 5 RPM each)
    /// down to high-quota Flash-Lite models (Tier 2: 500 RPD / 15 RPM each).
    /// Total combined free-tier capacity: 1,060 RPD &amp; 45 RPM.
    /// </summary>
    public List<string> Models { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public string FallbackModel { get; set; } = string.Empty;
    public int MaxToolRounds { get; set; } = 3;
    public double Temperature { get; set; } = 0.2;
}

public class GeminiException : Exception
{
    public int StatusCode { get; }
    public GeminiException(string message, int statusCode = 502) : base(message) => StatusCode = statusCode;
}

/// <summary>
/// REST wrapper over Gemini <c>generateContent</c> with a Quality-First Priority Waterfall:
/// always tries the best available Flash model first, and when a model hits its RPM/RPD quota (429)
/// or high-demand spike (503/timeout), records its exact cooldown and cascades to the next model.
/// </summary>
public partial class GeminiClient
{
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan DefaultRpmCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultRpdCooldown = TimeSpan.FromHours(1);
    private static readonly TimeSpan DefaultOverloadCooldown = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromHours(6);

    private static readonly ConcurrentDictionary<string, DateTime> CooldownUntilUtc = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] DefaultWaterfall =
    {
        "gemini-3-flash-preview",
        "gemini-3.8-flash",
        "gemini-3.6-flash",
        "gemini-3.5-flash-lite",
        "gemini-3.1-flash-lite",
    };

    [GeneratedRegex(@"""retryDelay""\s*:\s*""(\d+(?:\.\d+)?)s""", RegexOptions.IgnoreCase)]
    private static partial Regex RetryDelayRegex();

    private readonly HttpClient _http;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiClient> _logger;

    public GeminiClient(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<JsonObject> GenerateAsync(
        string systemInstruction,
        JsonArray contents,
        JsonArray functionDeclarations,
        CancellationToken ct)
    {
        if (!IsConfigured)
            throw new GeminiException("The AI assistant is not configured on the server.", StatusCodes.Status503ServiceUnavailable);

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = systemInstruction })
            },
            ["contents"] = contents.DeepClone(),
            ["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = functionDeclarations.DeepClone() }),
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = _options.Temperature,
                ["maxOutputTokens"] = 800
            }
        };

        var payload = body.ToJsonString();
        var modelsToTry = BuildModelWaterfall();

        GeminiException? lastError = null;
        foreach (var model in modelsToTry)
        {
            try
            {
                var result = await SendOnceAsync(model, payload, ct);
                CooldownUntilUtc.TryRemove(model, out _);
                return result;
            }
            catch (GeminiException ex)
            {
                lastError = ex;
                _logger.LogWarning("Model {Model} unavailable ({Status}), cascading to next model...", model, ex.StatusCode);
            }
        }

        throw lastError ?? new GeminiException("All AI models are currently busy. Please try again in a moment.");
    }

    /// <summary>
    /// Multimodal vision inference using the same Quality-First Priority Waterfall.
    /// Sends a single compressed image (<c>inlineData</c>) + prompt and requests structured JSON output.
    /// </summary>
    public async Task<(JsonObject Payload, string ModelUsed)> GenerateVisionJsonAsync(
        string prompt,
        string base64Data,
        string mimeType,
        CancellationToken ct)
    {
        if (!IsConfigured)
            throw new GeminiException("The AI Vision service is not configured on the server.", StatusCodes.Status503ServiceUnavailable);

        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(
                    new JsonObject
                    {
                        ["inlineData"] = new JsonObject
                        {
                            ["mimeType"] = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType,
                            ["data"] = base64Data
                        }
                    },
                    new JsonObject
                    {
                        ["text"] = prompt
                    })
            }),
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = 0.1,
                ["maxOutputTokens"] = 1024,
                ["responseMimeType"] = "application/json"
            }
        };

        var payload = body.ToJsonString();
        var modelsToTry = BuildModelWaterfall();

        GeminiException? lastError = null;
        foreach (var model in modelsToTry)
        {
            try
            {
                var result = await SendOnceAsync(model, payload, ct);
                CooldownUntilUtc.TryRemove(model, out _);
                return (result, model);
            }
            catch (GeminiException ex)
            {
                lastError = ex;
                _logger.LogWarning("Vision model {Model} unavailable ({Status}), cascading to next model...", model, ex.StatusCode);
            }
        }

        throw lastError ?? new GeminiException("All AI Vision models are currently busy. Please try again in a moment.");
    }

    private List<string> BuildModelWaterfall()
    {
        var configured = new List<string>();
        if (_options.Models is { Count: > 0 })
            configured.AddRange(_options.Models);
        if (!string.IsNullOrWhiteSpace(_options.Model))
            configured.Add(_options.Model);
        if (!string.IsNullOrWhiteSpace(_options.FallbackModel))
            configured.Add(_options.FallbackModel);
        configured.AddRange(DefaultWaterfall);

        var distinct = configured
            .Select(m => m?.Trim() ?? string.Empty)
            .Where(m => !string.IsNullOrWhiteSpace(m) &&
                        !m.Equals("gemini-flash-latest", StringComparison.OrdinalIgnoreCase) &&
                        !m.Equals("gemini-2.5-flash", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var now = DateTime.UtcNow;
        // Ready models first (in quality priority order), followed by cooled-down models ordered by soonest recovery.
        var ready = distinct.Where(m => !CooldownUntilUtc.TryGetValue(m, out var until) || until <= now).ToList();
        var cooling = distinct
            .Where(m => CooldownUntilUtc.TryGetValue(m, out var until) && until > now)
            .OrderBy(m => CooldownUntilUtc[m])
            .ToList();

        ready.AddRange(cooling);
        return ready;
    }

    private async Task<JsonObject> SendOnceAsync(string model, string payload, CancellationToken ct)
    {
        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attemptCts.CancelAfter(PerAttemptTimeout);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
        request.Headers.Add("x-goog-api-key", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        string text;
        try
        {
            response = await _http.SendAsync(request, attemptCts.Token);
            text = await response.Content.ReadAsStringAsync(attemptCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            CooldownUntilUtc[model] = DateTime.UtcNow.Add(DefaultOverloadCooldown);
            throw new GeminiException($"Model {model} timed out.", StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error calling Gemini model {Model}", model);
            throw new GeminiException("Could not reach the AI service. Please check your connection.", StatusCodes.Status502BadGateway);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                ApplyCooldownFromError(model, statusCode, text);
                _logger.LogWarning("Gemini {Model} returned {Status}: {Body}", model, statusCode, Truncate(text, 400));

                var mappedStatus = statusCode == 429
                    ? StatusCodes.Status429TooManyRequests
                    : StatusCodes.Status502BadGateway;
                throw new GeminiException(
                    mappedStatus == 429
                        ? "All AI model quotas are temporarily busy. Please wait a moment and try again."
                        : "The AI assistant failed to respond.",
                    mappedStatus);
            }

            return JsonNode.Parse(text) as JsonObject
                   ?? throw new GeminiException("The AI assistant returned an unreadable response.");
        }
    }

    private void ApplyCooldownFromError(string model, int statusCode, string responseBody)
    {
        TimeSpan cooldown;
        if (statusCode == 429)
        {
            var isDailyQuota = responseBody.Contains("PerDay", StringComparison.OrdinalIgnoreCase)
                               || responseBody.Contains("requests per day", StringComparison.OrdinalIgnoreCase);
            cooldown = isDailyQuota ? DefaultRpdCooldown : DefaultRpmCooldown;

            var match = RetryDelayRegex().Match(responseBody);
            if (match.Success &&
                double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                seconds > 0)
            {
                var parsed = TimeSpan.FromSeconds(seconds);
                cooldown = parsed <= MaxCooldown ? parsed : MaxCooldown;
            }
        }
        else if (statusCode is 503 or 500 or 502 or 504 or 404)
        {
            cooldown = statusCode == 404 ? MaxCooldown : DefaultOverloadCooldown;
        }
        else
        {
            return;
        }

        CooldownUntilUtc[model] = DateTime.UtcNow.Add(cooldown);
        _logger.LogInformation("Model {Model} placed on cooldown for {CooldownSeconds:0}s (HTTP {Status})",
            model, cooldown.TotalSeconds, statusCode);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
