namespace OcrAgent.Agent;

/// <summary>Artifact metadata emitted while an agent request is running.</summary>
public sealed record AgentArtifact(
    string RunId,
    string Name,
    long SizeBytes,
    DateTimeOffset? LastModifiedUtc,
    string ContentType,
    bool Previewable);
