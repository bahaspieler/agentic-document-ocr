using System.Text.Json.Serialization;

namespace OcrAgent.Workflows.Model;

public sealed class WorkflowDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("tags")]
    public string[] Tags { get; set; } = [];

    [JsonPropertyName("budget")]
    public WorkflowBudget Budget { get; set; } = new();

    [JsonPropertyName("steps")]
    public List<WorkflowStep> Steps { get; set; } = new();
}

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
