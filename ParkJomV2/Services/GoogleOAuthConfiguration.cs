using Microsoft.Extensions.Configuration;

namespace ParkJomV2.Services;

internal static class GoogleOAuthConfiguration
{
    internal static string[] GetAudiences(IConfiguration configuration)
    {
        var audiences = (configuration.GetSection("GoogleOAuth:AdditionalClientIds").Get<string[]>() ?? [])
            .Append(configuration["GoogleOAuth:ClientId"])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // An empty audience list would stop checking which app a Google token belongs to.
        if (audiences.Length == 0)
            throw new InvalidOperationException("No Google OAuth client IDs are configured.");

        return audiences;
    }
}
