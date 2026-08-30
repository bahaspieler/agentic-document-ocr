namespace OcrAgent.Mcp.Api;

/// <summary>Assigns anonymous conversations to the authenticated user.</summary>
public sealed record ClaimConversationsRequest(string[] ConversationIds);
