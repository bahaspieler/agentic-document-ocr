namespace OcrAgent.Mcp.Api;

/// <summary>Microsoft Entra settings for optional REST bearer authentication.</summary>
/// <remarks>
/// Authentication is active only when <see cref="Enabled"/> is true and
/// <see cref="ClientId"/> is set.
/// </remarks>
public sealed class AuthConfiguration
{
    /// <summary>Gets or sets whether the serving host should configure bearer authentication.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the Microsoft identity authority base URL.</summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>Gets or sets the tenant accepted by the API.</summary>
    public string TenantId { get; set; } = "common";

    /// <summary>Gets or sets the application registration client identifier.</summary>
    public string ClientId { get; set; } = "";
}
