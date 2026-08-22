namespace OcrAgent.Storage;

/// <summary>Lists and opens non-image and image artifacts within one recorded run scope.</summary>
public interface IArtifactStore
{
    Task<IReadOnlyList<StoredArtifact>> ListAsync(
        string scope, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(
        string scope, string name, CancellationToken cancellationToken = default);
}

public sealed record StoredArtifact(string Name, long SizeBytes, DateTimeOffset? LastModifiedUtc);

internal static class ArtifactPath
{
    public static string Scope(string value)
    {
        var normalized = value.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Split('/').Any(segment =>
                segment is "." or ".." || segment.Length == 0))
            throw new ArgumentException("Invalid artifact scope.", nameof(value));
        return normalized;
    }

    public static string Name(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value is "." or ".." ||
            value.IndexOfAny(['/', '\\']) >= 0 ||
            Path.GetFileName(value) != value)
            throw new ArgumentException("Invalid artifact name.", nameof(value));
        return value;
    }
}
