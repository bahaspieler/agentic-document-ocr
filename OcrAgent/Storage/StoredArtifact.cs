namespace OcrAgent.Storage;

/// <summary>Metadata for one stored run artifact.</summary>
/// <param name="Name">Artifact file name.</param>
/// <param name="SizeBytes">Artifact size in bytes.</param>
/// <param name="LastModifiedUtc">Last modification time, when available.</param>
public sealed record StoredArtifact(string Name, long SizeBytes, DateTimeOffset? LastModifiedUtc);
