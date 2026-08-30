namespace MyExperiment;

/// <summary>Identifies a queue image in Azure Blob Storage and local temporary storage.</summary>
/// <param name="Reference">Image reference used by OCR tools.</param>
/// <param name="BlobName">Source name in the training container.</param>
/// <param name="LocalPath">Temporary path containing the downloaded image.</param>
public sealed record ResolvedQueueImage(
    string Reference,
    string BlobName,
    string LocalPath);
