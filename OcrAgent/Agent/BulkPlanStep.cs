using System.Text.Json;

namespace OcrAgent.Agent;

/// <summary>One validated tool or workflow operation selected for an image batch.</summary>
public sealed record BulkPlanStep(
    string Mode,
    string Name,
    IReadOnlyDictionary<string, JsonElement>? Arguments = null);
