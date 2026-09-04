namespace OcrAgent.Mcp.Api;

/// <summary>Authorization policy names shared by REST and MCP route mappings.</summary>
internal static class AuthPolicies
{
    /// <summary>Requires a signed-in user with the configured delegated API scope.</summary>
    public const string Access = "OcrApiAccess";
}
