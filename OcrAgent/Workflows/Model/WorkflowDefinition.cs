using System.Text.Json.Serialization;

namespace OcrAgent.Workflows.Model;

/// <summary>
/// JSON workflow definition: metadata, budget, and ordered steps.
/// </summary>
public sealed class WorkflowDefinition
{
    /// <summary>Stable workflow id (file name / catalog key).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Semantic version string.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>Short description for catalogs and UIs.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>Optional classification tags.</summary>
    [JsonPropertyName("tags")]
    public string[] Tags { get; set; } = [];

    /// <summary>Execution limits for this workflow.</summary>
    [JsonPropertyName("budget")]
    public WorkflowBudget Budget { get; set; } = new();

    /// <summary>Steps executed in definition order (tool and branch steps).</summary>
    [JsonPropertyName("steps")]
    public List<WorkflowStep> Steps { get; set; } = new();
}
