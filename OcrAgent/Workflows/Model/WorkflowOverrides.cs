using System.Text.Json.Serialization;

namespace OcrAgent.Workflows.Model;

/// <summary>
/// Optional JSON payload to tweak steps, budget, or seed context before a run.
/// </summary>
public sealed class WorkflowOverrides
{
    /// <summary>Per-step enable flags and argument patches keyed by step id.</summary>
    [JsonPropertyName("steps")]
    public Dictionary<string, StepOverride>? Steps { get; set; }

    /// <summary>Budget fields to merge over the definition default.</summary>
    [JsonPropertyName("budget")]
    public WorkflowBudget? Budget { get; set; }

    /// <summary>Initial string values written into workflow context before steps.</summary>
    [JsonPropertyName("initialContext")]
    public Dictionary<string, string>? InitialContext { get; set; }
}
