namespace OcrAgent.Mcp.Api;

/// <summary>References an uploaded image attached to a conversation turn.</summary>
public sealed record ChatAttachment(string Reference, string? Name, string? ContentType);
