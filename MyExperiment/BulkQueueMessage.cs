namespace MyExperiment;

/// <summary>Internal queue message for one deterministic child of a bulk request.</summary>
public sealed class BulkQueueMessage
{
    /// <summary>Gets or sets the message contract discriminator.</summary>
    public string Kind { get; set; } = "bulk-item";

    /// <summary>Gets or sets the parent batch identifier.</summary>
    public string BatchId { get; set; } = "";

    /// <summary>Gets or sets the deterministic item identifier.</summary>
    public string ItemId { get; set; } = "";

    /// <summary>Gets or sets the original natural-language batch command.</summary>
    public string Command { get; set; } = "";

    /// <summary>Gets or sets the exact training-container Blob name.</summary>
    public string BlobName { get; set; } = "";

    /// <summary>Gets or sets the validated serialized batch plan.</summary>
    public string PlanJson { get; set; } = "";
}
