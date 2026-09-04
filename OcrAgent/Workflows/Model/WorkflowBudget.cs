using System.Text.Json.Serialization;

namespace OcrAgent.Workflows.Model;

/// <summary>Step, variant, and wall-clock limits for a workflow run.</summary>
public sealed class WorkflowBudget
{
    /// <summary>Gets or sets the maximum number of workflow steps.</summary>
    [JsonPropertyName("maxSteps")]
    public int MaxSteps { get; set; } = 20;

    /// <summary>Gets or sets the maximum number of image variants.</summary>
    [JsonPropertyName("maxVariants")]
    public int MaxVariants { get; set; } = 6;

    /// <summary>Gets or sets the workflow timeout in seconds.</summary>
    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Gets or sets the optional run time limit in milliseconds.</summary>
    [JsonPropertyName("timeLimitMs")]
    public int? TimeLimitMs { get; set; }
}
