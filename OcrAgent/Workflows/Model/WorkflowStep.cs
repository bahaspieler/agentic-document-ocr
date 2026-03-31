using System.Text.Json.Serialization;

namespace OcrAgent.Workflows.Model;

/// <summary>
/// One workflow step: either a tool invocation or a branch on a context variable.
/// </summary>
public sealed class WorkflowStep
{
    /// <summary>Unique step id within the workflow (override and trace key).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary><c>tool</c> for a single tool; <c>branch</c> for conditional routing.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "tool";

    /// <summary>Registered tool name when <c>type</c> is <c>tool</c>.</summary>
    [JsonPropertyName("tool")]
    public string Tool { get; set; } = "";

    /// <summary>Argument template merged with context and current image reference.</summary>
    [JsonPropertyName("args")]
    public Dictionary<string, object?> Args { get; set; } = new();

    /// <summary>When false, the runner may skip this step.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Optional human-readable note (not used by the engine).</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>When true, the output image reference stays the input reference.</summary>
    [JsonPropertyName("passthrough")]
    public bool Passthrough { get; set; } = false;

    /// <summary>Context keys to set after the tool; <c>$result</c> stores the tool output string.</summary>
    [JsonPropertyName("outputs")]
    public Dictionary<string, string>? Outputs { get; set; }

    /// <summary>
    /// For type=<c>branch</c>: context variable to read, e.g. <c>$context.ocrEngine</c>.
    /// </summary>
    [JsonPropertyName("condition")]
    public string? Condition { get; set; }

    /// <summary>
    /// For type=<c>branch</c>: map from condition string to nested steps; <c>default</c> is fallback.
    /// </summary>
    [JsonPropertyName("branches")]
    public Dictionary<string, List<WorkflowStep>>? Branches { get; set; }
}
