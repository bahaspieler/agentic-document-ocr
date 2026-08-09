namespace OcrAgent.Configuration;

/// <summary>
/// Loads a <c>.env</c> file into environment variables before configuration is built.
/// </summary>
/// <remarks>
/// Connection strings and model keys stay out of the committed configuration, and a developer should
/// not have to export them by hand for every shell. The file is searched for from the working
/// directory upwards, then from the application directory upwards, so it can sit once at the
/// solution root and serve the console app, the MCP server, and anything launched from a build
/// output folder.
/// <para>
/// Values already present in the environment win, which keeps a container's real settings ahead of a
/// stray file.
/// </para>
/// </remarks>
public static class EnvFile
{
    /// <summary>Finds and applies the nearest <c>.env</c> file.</summary>
    /// <returns>Path of the file that was applied, or null when none was found.</returns>
    public static string? Load()
    {
        foreach (var origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var path = Find(origin);
            if (path == null)
                continue;

            Apply(path);
            return path;
        }

        return null;
    }

    private static string? Find(string startDirectory)
    {
        var directory = startDirectory;

        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(directory, ".env");
            if (File.Exists(candidate))
                return candidate;

            directory = Directory.GetParent(directory)?.FullName ?? "";
        }

        return null;
    }

    private static void Apply(string path)
    {
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim();

            // Quoting a value is common in .env files and is not part of the value itself.
            if (value.Length > 1 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
