namespace UnitTests.Plugins.Enhancement;

/// <summary>Temporary-directory lifetime used by denoise tests.</summary>
internal sealed class DenoiseTempFolder : IDisposable
{
    /// <summary>Creates an isolated temporary directory.</summary>
    public DenoiseTempFolder()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"DenoiseTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    /// <summary>Gets the temporary directory path.</summary>
    public string Path { get; }

    /// <summary>Removes the temporary directory and its contents.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Test cleanup is best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Test cleanup is best effort.
        }
    }
}
