using System.Text.Json.Serialization;

namespace OcrAgent.Workflows.Model;

/// <summary>Step, variant, and wall-clock limits for a workflow run.</summary>
public sealed class WorkflowBudget
{
    [JsonPropertyName("maxSteps")]
    public int MaxSteps { get; set; } = 20;

    [JsonPropertyName("maxVariants")]
    public int MaxVariants { get; set; } = 6;

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 120;

    [JsonPropertyName("timeLimitMs")]
    public int? TimeLimitMs { get; set; }
}
