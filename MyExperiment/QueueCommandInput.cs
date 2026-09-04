namespace MyExperiment;

/// <summary>Local worker input materialized from one received queue message.</summary>
public sealed class QueueCommandInput
{
    /// <summary>Gets or sets the Azure Queue message identifier.</summary>
    public string RequestId { get; set; } = "";

    /// <summary>Gets or sets the natural-language command.</summary>
    public string Command { get; set; } = "";

    /// <summary>Gets or sets the internal message kind.</summary>
    public string Kind { get; set; } = "";

    /// <summary>Gets or sets the parent batch identifier.</summary>
    public string BatchId { get; set; } = "";

    /// <summary>Gets or sets the deterministic child identifier.</summary>
    public string ItemId { get; set; } = "";

    /// <summary>Gets or sets the exact training image Blob name.</summary>
    public string BlobName { get; set; } = "";

    /// <summary>Gets or sets the validated serialized plan.</summary>
    public string PlanJson { get; set; } = "";
}
