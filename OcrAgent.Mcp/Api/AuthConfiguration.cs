namespace OcrAgent.Mcp.Api;

/// <summary>Auth0 and API-key settings for HTTP authentication.</summary>
/// <remarks>
/// HTTP authentication is optional for local development. When enabled, every setting required to
/// validate tokens and advertise MCP protected-resource metadata must be present.
/// </remarks>
public sealed class AuthConfiguration
{
    /// <summary>Gets or sets whether REST and HTTP MCP requests require authentication.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the Auth0 tenant domain without a scheme.</summary>
    public string Domain { get; set; } = "";

    /// <summary>Gets or sets the Auth0 single-page application client identifier.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Gets or sets the Auth0 API identifier expected in access-token audiences.</summary>
    public string Audience { get; set; } = "";

    /// <summary>Gets or sets the delegated permission required by REST and MCP endpoints.</summary>
    public string ScopeName { get; set; } = "access:ocr";

    /// <summary>Gets or sets the canonical public URI of the protected HTTP MCP resource.</summary>
    public string ResourceUri { get; set; } = "";

    /// <summary>Gets or sets named SHA-256 hashes for long-lived API keys.</summary>
    public Dictionary<string, string> ApiKeys { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the Auth0 OAuth authorization-server issuer.</summary>
    public string AuthorizationServer =>
        $"https://{Domain.Trim().TrimEnd('/')}/";

    /// <summary>Rejects an enabled configuration that would leave authentication incomplete.</summary>
    /// <exception cref="InvalidOperationException">A required setting is absent or invalid.</exception>
    public void Validate()
    {
        if (!Enabled)
            return;

        Require(nameof(Domain), Domain);
        Require(nameof(ClientId), ClientId);
        Require(nameof(Audience), Audience);
        Require(nameof(ScopeName), ScopeName);
        Require(nameof(ResourceUri), ResourceUri);

        if (Domain.Contains("://", StringComparison.Ordinal) ||
            !Uri.CheckHostName(Domain.Trim().TrimEnd('/')).Equals(UriHostNameType.Dns))
        {
            throw new InvalidOperationException(
                "Auth:Domain must be an Auth0 DNS hostname without an https:// prefix.");
        }

        if (!Uri.TryCreate(ResourceUri, UriKind.Absolute, out var resource) ||
            resource.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException(
                "Auth:ResourceUri must be an absolute HTTP or HTTPS URI.");
        }
        if (!string.Equals(
                Audience.TrimEnd('/'),
                ResourceUri.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Auth:Audience must equal Auth:ResourceUri for MCP resource-parameter compatibility.");
        }

        foreach (var (name, hash) in ApiKeys)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                hash.Length != 64 ||
                !hash.All(Uri.IsHexDigit))
            {
                throw new InvalidOperationException(
                    "Each Auth:ApiKeys entry must have a name and a 64-character SHA-256 hex hash.");
            }
        }
    }

    private static void Require(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Auth:{name} is required when authentication is enabled.");
    }
}
