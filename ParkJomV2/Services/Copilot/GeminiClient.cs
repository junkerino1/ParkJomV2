using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace ParkJomV2.Services.Copilot;

public class GeminiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-3-flash-preview";
    public string FallbackModel { get; set; } = "gemini-3.5-flash-lite";
    public int MaxToolRounds { get; set; } = 5;
    public double Temperature { get; set; } = 0.3;
}

public class GeminiException : Exception
{
    public int StatusCode { get; }
    public GeminiException(string message, int statusCode = 502) : base(message) => StatusCode = statusCode;
}

/// <summary>
/// Thin REST wrapper over the Gemini <c>generateContent</c> endpoint.
/// Uses <see cref="JsonNode"/> so model turns (including opaque <c>thoughtSignature</c>
/// fields required by function calling) can be echoed back verbatim.
/// </summary>
public class GeminiClient
{
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(18);

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
                ["maxOutputTokens"] = 1024
            }
        };

        var payload = body.ToJsonString();
        var modelsToTry = new List<string> { PrimaryModel(_options.Model) };
        if (!string.IsNullOrWhiteSpace(_options.FallbackModel) &&
            !string.Equals(modelsToTry[0], _options.FallbackModel, StringComparison.OrdinalIgnoreCase))
        {
            modelsToTry.Add(_options.FallbackModel.Trim());
        }

        GeminiException? lastError = null;
        foreach (var model in modelsToTry)
        {
            try
            {
                return await SendOnceAsync(model, payload, ct);
            }
            catch (GeminiException ex)
            {
                lastError = ex;
                _logger.LogWarning("Gemini model {Model} failed ({Status}): {Message}", model, ex.StatusCode, ex.Message);
            }
        }

        throw lastError ?? new GeminiException("The AI assistant failed to respond.");
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
            throw new GeminiException("The AI assistant timed out. Please try again.", StatusCodes.Status504GatewayTimeout);
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
                _logger.LogWarning("Gemini {Model} returned {Status}: {Body}", model, (int)response.StatusCode, Truncate(text, 800));
                var status = (int)response.StatusCode == 429
                    ? StatusCodes.Status429TooManyRequests
                    : StatusCodes.Status502BadGateway;
                throw new GeminiException(
                    status == 429 ? "The AI assistant is busy. Please try again shortly." : "The AI assistant failed to respond.",
                    status);
            }

            return JsonNode.Parse(text) as JsonObject
                   ?? throw new GeminiException("The AI assistant returned an unreadable response.");
        }
    }

    /// <summary>Map legacy/slow aliases to the fast Flash model that supports function calling.</summary>
    private static string PrimaryModel(string configured)
    {
        var trimmed = configured?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed) ||
            trimmed.Equals("gemini-flash-latest", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("gemini-2.5-flash", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("gemini-3.8-flash", StringComparison.OrdinalIgnoreCase))
        {
            return "gemini-3-flash-preview";
        }
        return trimmed;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
