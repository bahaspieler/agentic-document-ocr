namespace MyExperiment;

/// <summary>One supported image Blob selected from a queue dataset folder.</summary>
/// <param name="BlobName">Full Blob name below the training container.</param>
/// <param name="FileName">Leaf file name used for local processing.</param>
public sealed record QueueImage(string BlobName, string FileName);
