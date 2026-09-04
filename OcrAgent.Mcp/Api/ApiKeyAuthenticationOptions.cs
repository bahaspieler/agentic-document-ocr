using Microsoft.AspNetCore.Authentication;

namespace OcrAgent.Mcp.Api;

/// <summary>Configures named API-key hashes for the custom authentication handler.</summary>
internal sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>Header used by direct clients that authenticate with a static key.</summary>
    public const string HeaderName = "X-API-Key";

    /// <summary>Gets or sets key names mapped to lowercase SHA-256 hashes.</summary>
    public IReadOnlyDictionary<string, string> KeyHashes { get; set; } =
        new Dictionary<string, string>();
}
