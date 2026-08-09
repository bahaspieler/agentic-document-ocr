namespace OcrAgent.Runs;

/// <summary>
/// What one executed job left behind: which tool or workflow ran, on what, how it ended, and where
/// the artifacts are.
/// </summary>
/// <remarks>
/// One record covers both execution modes. A standalone tool call fills <see cref="Name"/> with the
/// tool name and leaves <see cref="TraceJson"/> null; a workflow fills it with the workflow id and
/// carries its step trace instead of writing a row per step.
/// <para>
/// The timing and identity fields deliberately use the names from the course template's
/// <c>IExperimentResult</c>, so the queue-driven path can write this same shape later rather than a
/// second table.
/// </para>
/// </remarks>
public sealed class RunRecord
{
    /// <summary>Time-sortable id from <see cref="RunId.NewId"/>; unique per job.</summary>
    public string RunId { get; set; } = "";

    /// <summary><c>tool</c> for a single plugin call, <c>workflow</c> for a pipeline run.</summary>
    public string Mode { get; set; } = "";

    /// <summary>Tool name or workflow id, depending on <see cref="Mode"/>.</summary>
    public string Name { get; set; } = "";

    /// <summary>Workflow definition version; null for a single tool call.</summary>
    public string? Version { get; set; }

    /// <summary>Entry point that started the job: <c>cli</c>, <c>chat</c>, or <c>mcp</c>.</summary>
    public string Source { get; set; } = "";

    /// <summary>Team or deployment the run belongs to; the table partition key.</summary>
    public string GroupId { get; set; } = "";

    /// <summary>Image reference the job started from.</summary>
    public string InputReference { get; set; } = "";

    /// <summary>Image reference the job produced, empty when it produced no image.</summary>
    public string OutputReference { get; set; } = "";

    /// <summary>Directory holding the artifacts, relative to the image store root.</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>Steps executed; 1 for a single tool call.</summary>
    public int StepCount { get; set; }

    /// <summary>False when the job threw or a workflow reported an error.</summary>
    public bool Success { get; set; }

    /// <summary>Failure message when <see cref="Success"/> is false.</summary>
    public string? Error { get; set; }

    /// <summary>When the job started.</summary>
    public DateTimeOffset StartTimeUtc { get; set; }

    /// <summary>When the job finished, successfully or not.</summary>
    public DateTimeOffset EndTimeUtc { get; set; }

    /// <summary>Wall-clock duration in milliseconds.</summary>
    public long DurationMs { get; set; }

    /// <summary>Image store backing the run: <c>local</c>, <c>sftp</c>, or <c>azureblob</c>.</summary>
    public string? StorageProvider { get; set; }

    /// <summary>Arguments the job was called with, minus the image reference.</summary>
    public string? ArgumentsJson { get; set; }

    /// <summary>Per-step trace for a workflow run; null for a single tool call.</summary>
    public string? TraceJson { get; set; }
}
