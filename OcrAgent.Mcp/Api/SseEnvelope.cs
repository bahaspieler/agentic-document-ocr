namespace OcrAgent.Mcp.Api;

/// <summary>Pairs a server-sent event name with its serialized payload.</summary>
internal sealed record SseEnvelope(string Event, object Data);
