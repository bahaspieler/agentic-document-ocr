using System.Text.Json.Serialization;

namespace OcrAgent.Workflows.Model;

/// <summary>Optional enable flag and argument patch for one workflow step.</summary>
public sealed class StepOverride
{
    /// <summary>Gets or sets whether the workflow step is enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Gets or sets argument values that replace the step defaults.</summary>
    [JsonPropertyName("args")]
    public Dictionary<string, object?>? Args { get; set; }
}
