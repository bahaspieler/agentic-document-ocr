namespace OcrAgent.Mcp.Api;

public sealed class AuthConfiguration
{
    public bool Enabled { get; set; }
    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string TenantId { get; set; } = "common";
    public string ClientId { get; set; } = "";
}
