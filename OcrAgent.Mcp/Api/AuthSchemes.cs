namespace OcrAgent.Mcp.Api;

/// <summary>Authentication scheme names used by the serving host.</summary>
internal static class AuthSchemes
{
    /// <summary>Selects JWT or API-key validation from the request headers.</summary>
    public const string Composite = "OcrComposite";

    /// <summary>Validates Auth0 access tokens.</summary>
    public const string Jwt = "Auth0Bearer";

    /// <summary>Validates long-lived named API keys.</summary>
    public const string ApiKey = "OcrApiKey";
}
