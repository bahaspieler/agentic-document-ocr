using System.Text.Json.Serialization;

namespace OcrAgent.Workflows.Model;

/// <summary>Optional enable flag and argument patch for one workflow step.</summary>
public sealed class StepOverride
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("args")]
    public Dictionary<string, object?>? Args { get; set; }
}
