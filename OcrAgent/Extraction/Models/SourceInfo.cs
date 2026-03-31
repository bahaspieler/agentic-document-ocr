using System.Text.Json.Serialization;

namespace OcrAgent.Extraction.Models;

/// <summary>
/// Where the receipt came from: storage ref, workflow run, OCR engine, mode.
/// </summary>
public sealed class SourceInfo
{
    /// <summary>Input image key or path in storage.</summary>
    [JsonPropertyName("inputImageRef")]
    public string InputImageRef { get; set; } = "";

    /// <summary>Workflow id if set by the runner.</summary>
    [JsonPropertyName("workflowId")]
    public string? WorkflowId { get; set; }

    /// <summary>Per-run id if set by the runner.</summary>
    [JsonPropertyName("runId")]
    public string? RunId { get; set; }

    /// <summary>OCR engine name from <c>OcrResult</c>.</summary>
    [JsonPropertyName("ocrEngineName")]
    public string OcrEngineName { get; set; } = "";

    /// <summary>Document-level OCR score (engine-specific scale).</summary>
    [JsonPropertyName("ocrConfidence")]
    public double OcrConfidence { get; set; }

    /// <summary>How fields were produced (e.g. rules, hybrid).</summary>
    [JsonPropertyName("extractionMode")]
    public string ExtractionMode { get; set; } = "hybrid";
}
