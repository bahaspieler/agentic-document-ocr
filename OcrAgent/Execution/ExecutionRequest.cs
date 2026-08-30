using System.Text.Json;

namespace OcrAgent.Execution;

/// <summary>Transport-neutral description of one isolated agent, tool, or workflow execution.</summary>
public sealed record ExecutionRequest(
    ExecutionMode Mode,
    string? Name = null,
    string? ImageReference = null,
    string? OverridesJson = null,
    IReadOnlyDictionary<string, JsonElement>? Arguments = null,
    string? Message = null,
    string Source = "queue",
    string? ConversationId = null);
