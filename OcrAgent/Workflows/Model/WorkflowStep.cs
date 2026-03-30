using System.Text.Json.Serialization;

namespace OcrAgent.Workflows.Model;

public sealed class WorkflowStep
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>"tool" for normal steps, "branch" for conditional routing.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "tool";

    [JsonPropertyName("tool")]
    public string Tool { get; set; } = "";

    [JsonPropertyName("args")]
    public Dictionary<string, object?> Args { get; set; } = new();

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("passthrough")]
    public bool Passthrough { get; set; } = false;

    [JsonPropertyName("outputs")]
    public Dictionary<string, string>? Outputs { get; set; }

    /// <summary>
    /// For type="branch": context variable to evaluate, e.g. "$context.ocrEngine".
    /// </summary>
    [JsonPropertyName("condition")]
    public string? Condition { get; set; }

    /// <summary>
    /// For type="branch": mapping of case values to step lists.
    /// A key of "default" is used as fallback.
    /// </summary>
    [JsonPropertyName("branches")]
    public Dictionary<string, List<WorkflowStep>>? Branches { get; set; }
}
