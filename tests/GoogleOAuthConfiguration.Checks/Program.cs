using Microsoft.Extensions.Configuration;
using ParkJomV2.Services;

static IConfiguration Config(params (string Key, string? Value)[] values) =>
    new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

static void Expect(IConfiguration config, params string[] expected)
{
    var actual = GoogleOAuthConfiguration.GetAudiences(config);
    if (!actual.Order().SequenceEqual(expected.Order()))
        throw new Exception("Unexpected configured Google OAuth audiences.");
}

Expect(Config(("GoogleOAuth:ClientId", "web")), "web");
Expect(Config(("GoogleOAuth:ClientId", "web"), ("GoogleOAuth:AdditionalClientIds:0", "native")), "web", "native");
Expect(Config(("GoogleOAuth:ClientId", " web "), ("GoogleOAuth:AdditionalClientIds:0", "web"),
    ("GoogleOAuth:AdditionalClientIds:1", " "), ("GoogleOAuth:AdditionalClientIds:2", "native")), "web", "native");
Expect(Config(("GoogleOAuth:AdditionalClientIds:0", "native")), "native");
foreach (var empty in new[] { Config(), Config(("GoogleOAuth:ClientId", " ")) })
{
    try { GoogleOAuthConfiguration.GetAudiences(empty); }
    catch (InvalidOperationException) { continue; }
    throw new Exception("Missing Google OAuth audiences must fail closed.");
}

var shipped = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath("ParkJomV2/appsettings.json")).Build();
Expect(shipped,
    "917128720686-vrm50eevjakijmkftq8dj4qj7itl8d5m.apps.googleusercontent.com",
    "681911813064-uk2mmra0532rp7gcmq3frfj5h37siq5d.apps.googleusercontent.com");
Console.WriteLine("Google OAuth configuration: web/native audiences, legacy settings, duplicates, blanks and missing configuration passed.");
