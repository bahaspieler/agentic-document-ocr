using System.Text.Json.Serialization;

namespace OcrAgent.Workflows.Model;

public sealed class WorkflowOverrides
{
    [JsonPropertyName("steps")]
    public Dictionary<string, StepOverride>? Steps { get; set; }

    [JsonPropertyName("budget")]
    public WorkflowBudget? Budget { get; set; }

    [JsonPropertyName("initialContext")]
    public Dictionary<string, string>? InitialContext { get; set; }
}

public sealed class StepOverride
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("args")]
    public Dictionary<string, object?>? Args { get; set; }
}
