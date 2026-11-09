using System.Text.Json;

namespace ParkJomV2.Services;

public sealed class Sms360Service
{
    private const string DefaultEndpoint = "https://sms.360.my/gw/bulk360/v3_0/send.php";
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<Sms360Service> _logger;

    public Sms360Service(HttpClient httpClient, IConfiguration configuration, ILogger<Sms360Service> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Sms360SendResult> SendOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken)
    {
        var appKey = _configuration["Sms360:AppKey"];
        var appSecret = _configuration["Sms360:AppSecret"];
        if (string.IsNullOrWhiteSpace(appKey) || string.IsNullOrWhiteSpace(appSecret))
        {
            return Sms360SendResult.Failed(
                "SMS360_CONFIGURATION_MISSING",
                "SMS sending is not configured. Check the Azure settings Sms360__AppKey and Sms360__AppSecret.");
        }

        var endpoint = _configuration["Sms360:BaseUrl"];
        if (string.IsNullOrWhiteSpace(endpoint)) endpoint = DefaultEndpoint;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) || endpointUri.Scheme != Uri.UriSchemeHttps)
            return Sms360SendResult.Failed("SMS360_CONFIGURATION_INVALID", "SMS360:BaseUrl must be a valid HTTPS URL.");
        var message = $"ParkJom verification code: {otp}. It expires in 5 minutes. Do not share this code.";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["user"] = appKey,
                    ["pass"] = appSecret,
                    ["to"] = phoneNumber,
                    ["text"] = message
                })
            };

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("SMS360 returned HTTP status {StatusCode}.", (int)response.StatusCode);
                return Sms360SendResult.Failed("SMS360_HTTP_ERROR", $"SMS provider returned HTTP {(int)response.StatusCode}.");
            }

            using var json = JsonDocument.Parse(responseText);
            var root = json.RootElement;
            var providerCode = ReadString(root, "code");
            var description = ReadString(root, "desc") ?? "No description returned by SMS provider.";
            if (!string.Equals(providerCode, "200", StringComparison.Ordinal))
            {
                _logger.LogWarning("SMS360 rejected an SMS request with provider code {ProviderCode}.", providerCode ?? "missing");
                return Sms360SendResult.Failed(
                    $"SMS360_{providerCode ?? "INVALID_RESPONSE"}",
                    Limit(description, 300));
            }

            return Sms360SendResult.Succeeded(ReadString(root, "ref"));
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "SMS360 returned a response that could not be parsed.");
            return Sms360SendResult.Failed("SMS360_INVALID_RESPONSE", "The SMS provider returned an unreadable response. Check the application logs.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Could not reach SMS360.");
            return Sms360SendResult.Failed("SMS360_CONNECTION_FAILED", "Could not connect to SMS360. Check outbound HTTPS access and the provider endpoint.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "SMS360 request timed out.");
            return Sms360SendResult.Failed("SMS360_TIMEOUT", "SMS360 did not respond before the request timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while sending an OTP through SMS360.");
            return Sms360SendResult.Failed("SMS360_SEND_FAILED", $"SMS sending failed ({ex.GetType().Name}). Check application logs for details.");
        }
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string Limit(string value, int maximumLength)
        => value.Length <= maximumLength ? value : value[..maximumLength];
}

public sealed class Sms360SendResult
{
    public bool Success { get; private init; }
    public string? ProviderMessageId { get; private init; }
    public string? ErrorCode { get; private init; }
    public string? ErrorDescription { get; private init; }

    public static Sms360SendResult Succeeded(string? providerMessageId)
        => new() { Success = true, ProviderMessageId = providerMessageId };

    public static Sms360SendResult Failed(string errorCode, string errorDescription)
        => new() { Success = false, ErrorCode = errorCode, ErrorDescription = errorDescription };
}
