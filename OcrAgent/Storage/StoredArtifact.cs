namespace OcrAgent.Storage;

/// <summary>Metadata for one stored run artifact.</summary>
public sealed record StoredArtifact(string Name, long SizeBytes, DateTimeOffset? LastModifiedUtc);
