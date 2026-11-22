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
    /// Priority waterfall from strongest active Flash/Hydrogen models through high-RPD Flash-Lite and Gemma 4 models.
    /// </summary>
    public List<string> Models { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public string FallbackModel { get; set; } = string.Empty;
    public int MaxToolRounds { get; set; } = 5;
    public double Temperature { get; set; } = 0.2;
}

public sealed record ModelQuotaLimit(int RpmLimit, int RpdLimit);

public class GeminiException : Exception
{
    public int StatusCode { get; }
    public int RawStatusCode { get; }

    public GeminiException(string message, int statusCode = 502, int? rawStatusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        RawStatusCode = rawStatusCode ?? statusCode;
    }
}

/// <summary>
/// REST wrapper over Gemini <c>generateContent</c> with proactive soft-quota tracking and reactive failover:
/// switches automatically across all 10 available rate-limited models when a model reaches RPM/RPD or returns 429/5xx,
/// and strips incompatible <c>thoughtSignature</c> / <c>thought</c> parts before retrying across model families.
/// </summary>
public partial class GeminiClient
{
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MinRpmCooldown = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultRpmCooldown = TimeSpan.FromSeconds(65);
    private static readonly TimeSpan MaxRpmCooldown = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DefaultOverloadCooldown = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan NotFoundCooldown = TimeSpan.FromHours(6);
    private static readonly TimeSpan MaxDailyCooldown = TimeSpan.FromHours(24);
    private static readonly TimeZoneInfo PacificTimeZone = ResolvePacificTimeZone();

    private static readonly ConcurrentDictionary<string, DateTime> CooldownUntilUtc = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, ModelUsageState> UsageByModel = new(StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlyDictionary<string, ModelQuotaLimit> ModelRateLimits =
        new Dictionary<string, ModelQuotaLimit>(StringComparer.OrdinalIgnoreCase)
        {
            ["gemini-4-hydrogen"] = new(5, 20),
            ["gemini-3.8-flash"] = new(5, 20),
            ["gemini-3.6-flash"] = new(5, 20),
            ["gemini-3-flash-preview"] = new(5, 20),
            ["gemini-2.5-flash"] = new(5, 20),
            ["gemini-3.5-flash-lite"] = new(15, 500),
            ["gemini-3.1-flash-lite"] = new(15, 500),
            ["gemini-2.5-flash-lite"] = new(10, 20),
            ["gemma-4-31b-it"] = new(30, 14400),
            ["gemma-4-26b-a4b-it"] = new(30, 14400),
        };

    private static readonly string[] DefaultWaterfall =
    {
        "gemini-4-hydrogen",
        "gemini-3.8-flash",
        "gemini-3.6-flash",
        "gemini-3-flash-preview",
        "gemini-2.5-flash",
        "gemini-3.5-flash-lite",
        "gemini-3.1-flash-lite",
        "gemini-2.5-flash-lite",
        "gemma-4-31b-it",
        "gemma-4-26b-a4b-it",
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
        JsonArray? functionDeclarations,
        CancellationToken ct)
    {
        if (!IsConfigured)
            throw new GeminiException("The AI assistant is not configured on the server.", StatusCodes.Status503ServiceUnavailable);

        var activeContents = contents;
        var hasThoughtArtifacts = ContainsThoughtArtifacts(contents);
        var sanitized = false;
        var payload = BuildGeneratePayload(systemInstruction, activeContents, functionDeclarations, _options.Temperature);
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
            catch (GeminiException ex) when (ex.RawStatusCode == StatusCodes.Status400BadRequest && !sanitized && hasThoughtArtifacts)
            {
                // Switching models mid-conversation can fail if another model rejects a prior thoughtSignature.
                sanitized = true;
                activeContents = SanitizeContentsForModelSwitch(contents);
                payload = BuildGeneratePayload(systemInstruction, activeContents, functionDeclarations, _options.Temperature);
                _logger.LogInformation("Model {Model} rejected thought artifacts (HTTP 400); retrying without thoughtSignature.", model);

                try
                {
                    var retryResult = await SendOnceAsync(model, payload, ct);
                    CooldownUntilUtc.TryRemove(model, out _);
                    return retryResult;
                }
                catch (GeminiException retryEx)
                {
                    lastError = retryEx;
                    _logger.LogWarning("Model {Model} unavailable after sanitizing ({Status}), cascading to next model...", model, retryEx.RawStatusCode);
                }
            }
            catch (GeminiException ex)
            {
                lastError = ex;
                _logger.LogWarning("Model {Model} unavailable ({Status}), cascading to next model...", model, ex.RawStatusCode);
            }
        }

        throw lastError ?? new GeminiException("All AI models are currently busy. Please try again in a moment.");
    }

    /// <summary>
    /// Multimodal vision inference using the same rate-limit-aware Priority Waterfall.
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
                _logger.LogWarning("Vision model {Model} unavailable ({Status}), cascading to next model...", model, ex.RawStatusCode);
            }
        }

        throw lastError ?? new GeminiException("All AI Vision models are currently busy. Please try again in a moment.");
    }

    private List<string> BuildModelWaterfall()
    {
        var now = DateTime.UtcNow;
        var softLimited = GetSoftRateLimitedModels(now);
        return ResolveModelWaterfall(_options, CooldownUntilUtc, softLimited, now);
    }

    /// <summary>
    /// Orders ready candidate models first, then models at their local soft RPM/RPD quota,
    /// followed by hard-cooled models ordered by soonest recovery time.
    /// </summary>
    public static List<string> ResolveModelWaterfall(
        GeminiOptions options,
        IReadOnlyDictionary<string, DateTime>? cooldownsUtc = null,
        IReadOnlySet<string>? softRateLimitedModels = null,
        DateTime? utcNow = null)
    {
        var configured = new List<string>();
        if (options.Models is { Count: > 0 })
            configured.AddRange(options.Models);
        if (!string.IsNullOrWhiteSpace(options.Model))
            configured.Add(options.Model);
        if (!string.IsNullOrWhiteSpace(options.FallbackModel))
            configured.Add(options.FallbackModel);
        configured.AddRange(DefaultWaterfall);

        var distinct = configured
            .Select(m => m?.Trim() ?? string.Empty)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var now = utcNow ?? DateTime.UtcNow;
        bool IsHardCooling(string m) => cooldownsUtc != null && cooldownsUtc.TryGetValue(m, out var until) && until > now;
        bool IsSoftLimited(string m) => softRateLimitedModels != null && softRateLimitedModels.Contains(m);

        var ready = distinct.Where(m => !IsHardCooling(m) && !IsSoftLimited(m)).ToList();
        var softLimited = distinct.Where(m => !IsHardCooling(m) && IsSoftLimited(m)).ToList();
        var cooling = cooldownsUtc == null
            ? new List<string>()
            : distinct.Where(IsHardCooling).OrderBy(m => cooldownsUtc[m]).ToList();

        ready.AddRange(softLimited);
        ready.AddRange(cooling);
        return ready;
    }

    /// <summary>
    /// Strips <c>thought: true</c> parts and <c>thoughtSignature</c> fields so conversation history
    /// produced by one Gemini model can safely replay against another model after rate-limit failover.
    /// </summary>
    public static JsonArray SanitizeContentsForModelSwitch(JsonArray contents)
    {
        var cloned = contents.DeepClone() as JsonArray ?? new JsonArray();
        foreach (var turnNode in cloned)
        {
            if (turnNode is not JsonObject turn || turn["parts"] is not JsonArray parts)
                continue;

            for (var i = parts.Count - 1; i >= 0; i--)
            {
                if (parts[i] is not JsonObject part)
                    continue;

                if (part["thought"] is JsonValue tv && tv.TryGetValue<bool>(out var isThought) && isThought)
                {
                    parts.RemoveAt(i);
                    continue;
                }

                part.Remove("thoughtSignature");
                part.Remove("thought");
            }
        }

        return cloned;
    }

    /// <summary>
    /// Determines how long a failed model should remain on cooldown.
    /// Daily RPD 429s cool until next Pacific midnight + 1 minute; RPM/TPM 429s use retry delay or 65s;
    /// 5xx waits 90s; 404 waits 6h; 400 returns <c>null</c> (not a global model outage).
    /// </summary>
    public static TimeSpan? ComputeCooldown(
        int statusCode,
        string responseBody,
        TimeSpan? retryAfter = null,
        DateTime? utcNow = null)
    {
        if (statusCode == StatusCodes.Status429TooManyRequests)
        {
            var isDailyQuota = responseBody.Contains("PerDay", StringComparison.OrdinalIgnoreCase)
                               || responseBody.Contains("requests per day", StringComparison.OrdinalIgnoreCase)
                               || responseBody.Contains("GenerateRequestsPerDay", StringComparison.OrdinalIgnoreCase)
                               || responseBody.Contains("daily", StringComparison.OrdinalIgnoreCase)
                               || responseBody.Contains("RPD", StringComparison.OrdinalIgnoreCase);

            if (isDailyQuota)
            {
                var utc = DateTime.SpecifyKind(utcNow ?? DateTime.UtcNow, DateTimeKind.Utc);
                var pacificNow = TimeZoneInfo.ConvertTimeFromUtc(utc, PacificTimeZone);
                var nextMidnightPacific = pacificNow.Date.AddDays(1).AddMinutes(1);
                var nextMidnightUtc = TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(nextMidnightPacific, DateTimeKind.Unspecified),
                    PacificTimeZone);
                var delay = nextMidnightUtc - utc;
                if (delay < TimeSpan.FromMinutes(1)) return TimeSpan.FromMinutes(1);
                return delay <= MaxDailyCooldown ? delay : MaxDailyCooldown;
            }

            if (retryAfter is { TotalSeconds: > 0 } headerDelay)
                return ClampRpmCooldown(headerDelay);

            var match = RetryDelayRegex().Match(responseBody);
            if (match.Success &&
                double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                seconds > 0)
            {
                return ClampRpmCooldown(TimeSpan.FromSeconds(seconds));
            }

            return DefaultRpmCooldown;
        }

        if (statusCode is StatusCodes.Status500InternalServerError
            or StatusCodes.Status502BadGateway
            or StatusCodes.Status503ServiceUnavailable
            or StatusCodes.Status504GatewayTimeout)
        {
            return DefaultOverloadCooldown;
        }

        if (statusCode == StatusCodes.Status404NotFound)
        {
            return NotFoundCooldown;
        }

        return null;
    }

    private static TimeSpan ClampRpmCooldown(TimeSpan value)
    {
        if (value < MinRpmCooldown) return MinRpmCooldown;
        return value <= MaxRpmCooldown ? value : MaxRpmCooldown;
    }

    private async Task<JsonObject> SendOnceAsync(string model, string payload, CancellationToken ct)
    {
        RecordModelUsage(model, DateTime.UtcNow);

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
                var retryAfter = ParseRetryAfter(response.Headers.RetryAfter);
                ApplyCooldownFromError(model, statusCode, text, retryAfter);
                _logger.LogWarning("Gemini {Model} returned {Status}: {Body}", model, statusCode, Truncate(text, 400));

                var mappedStatus = statusCode == StatusCodes.Status429TooManyRequests
                    ? StatusCodes.Status429TooManyRequests
                    : StatusCodes.Status502BadGateway;
                throw new GeminiException(
                    mappedStatus == StatusCodes.Status429TooManyRequests
                        ? "All AI model quotas are temporarily busy. Please wait a moment and try again."
                        : "The AI assistant failed to respond.",
                    mappedStatus,
                    rawStatusCode: statusCode);
            }

            return JsonNode.Parse(text) as JsonObject
                   ?? throw new GeminiException("The AI assistant returned an unreadable response.");
        }
    }

    private void ApplyCooldownFromError(string model, int statusCode, string responseBody, TimeSpan? retryAfter)
    {
        var cooldown = ComputeCooldown(statusCode, responseBody, retryAfter, DateTime.UtcNow);
        if (!cooldown.HasValue) return;

        CooldownUntilUtc[model] = DateTime.UtcNow.Add(cooldown.Value);
        _logger.LogInformation("Model {Model} placed on cooldown for {CooldownSeconds:0}s (HTTP {Status})",
            model, cooldown.Value.TotalSeconds, statusCode);
    }

    private static string BuildGeneratePayload(
        string systemInstruction,
        JsonArray contents,
        JsonArray? functionDeclarations,
        double temperature)
    {
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = systemInstruction })
            },
            ["contents"] = contents.DeepClone(),
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = temperature,
                ["maxOutputTokens"] = 2048
            }
        };

        if (functionDeclarations is { Count: > 0 })
        {
            body["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = functionDeclarations.DeepClone() });
        }

        return body.ToJsonString();
    }

    private static bool ContainsThoughtArtifacts(JsonArray contents)
    {
        foreach (var turnNode in contents)
        {
            if (turnNode is not JsonObject turn || turn["parts"] is not JsonArray parts)
                continue;

            foreach (var partNode in parts)
            {
                if (partNode is not JsonObject part) continue;
                if (part.ContainsKey("thoughtSignature") || part.ContainsKey("thought"))
                    return true;
            }
        }
        return false;
    }

    private static TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? header)
    {
        if (header == null) return null;
        if (header.Delta is { TotalSeconds: > 0 } delta) return delta;
        if (header.Date is { } target)
        {
            var remaining = target.UtcDateTime - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero) return remaining;
        }
        return null;
    }

    private static void RecordModelUsage(string model, DateTime utcNow)
    {
        var state = UsageByModel.GetOrAdd(model, _ => new ModelUsageState());
        state.RecordRequest(utcNow, PacificTimeZone);
    }

    private static HashSet<string> GetSoftRateLimitedModels(DateTime utcNow)
    {
        var limited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (model, limit) in ModelRateLimits)
        {
            if (UsageByModel.TryGetValue(model, out var state) &&
                state.IsAtSoftLimit(limit, utcNow, PacificTimeZone))
            {
                limited.Add(model);
            }
        }
        return limited;
    }

    private static TimeZoneInfo ResolvePacificTimeZone()
    {
        foreach (var id in new[] { "America/Los_Angeles", "Pacific Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("PST", TimeSpan.FromHours(-8), "PST", "PST");
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed class ModelUsageState
    {
        private readonly object _sync = new();
        private readonly Queue<DateTime> _recentRequestsUtc = new();
        private DateOnly _pacificDate;
        private int _dailyRequests;

        public void RecordRequest(DateTime utcNow, TimeZoneInfo pacificTz)
        {
            var pacificDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), pacificTz));

            lock (_sync)
            {
                if (_pacificDate != pacificDate)
                {
                    _pacificDate = pacificDate;
                    _dailyRequests = 0;
                }

                _dailyRequests++;
                _recentRequestsUtc.Enqueue(utcNow);
                PruneExpiredLocked(utcNow);
            }
        }

        public bool IsAtSoftLimit(ModelQuotaLimit limit, DateTime utcNow, TimeZoneInfo pacificTz)
        {
            var pacificDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), pacificTz));

            lock (_sync)
            {
                PruneExpiredLocked(utcNow);
                var dailyCount = _pacificDate == pacificDate ? _dailyRequests : 0;
                return _recentRequestsUtc.Count >= limit.RpmLimit || dailyCount >= limit.RpdLimit;
            }
        }

        private void PruneExpiredLocked(DateTime utcNow)
        {
            var cutoff = utcNow.AddSeconds(-60);
            while (_recentRequestsUtc.Count > 0 && _recentRequestsUtc.Peek() <= cutoff)
            {
                _recentRequestsUtc.Dequeue();
            }
        }
    }
}
