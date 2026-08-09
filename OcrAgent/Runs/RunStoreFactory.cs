using System.Diagnostics;

namespace OcrAgent.Runs;

/// <summary>
/// Builds <see cref="IRunStore"/> from <see cref="RunStoreConfiguration"/> (no store, or Azure Table).
/// </summary>
public static class RunStoreFactory
{
    /// <summary>Selects the implementation named by the configuration and validates its settings.</summary>
    /// <param name="config">Provider name and options.</param>
    /// <returns>Configured store instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
    /// <exception cref="ArgumentException">Unknown <see cref="RunStoreConfiguration.Provider"/>.</exception>
    public static IRunStore Create(RunStoreConfiguration config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        var provider = config.Provider.ToLowerInvariant();

        return provider switch
        {
            "none" or "" => NullRunStore.Instance,
            "azuretable" => CreateAzureTableStore(config),
            _ => throw new ArgumentException($"Unknown run store provider: {config.Provider}. Must be 'none' or 'azuretable'.")
        };
    }

    /// <summary>
    /// Builds the store but degrades to <see cref="NullRunStore"/> instead of throwing.
    /// </summary>
    /// <remarks>
    /// Hosts use this at start-up: an unreachable table should cost the operator their run history,
    /// not the ability to process images.
    /// </remarks>
    /// <param name="config">Provider name and options.</param>
    /// <returns>Configured store, or the no-op store when it could not be built.</returns>
    public static IRunStore CreateOrNull(RunStoreConfiguration config)
    {
        try
        {
            return Create(config);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WARN] Run store disabled: {ex.Message}");
            Console.Error.WriteLine($"Warning: run recording is disabled ({ex.Message})");
            return NullRunStore.Instance;
        }
    }

    private static IRunStore CreateAzureTableStore(RunStoreConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            config.ConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? "";
        }

        config.Validate();
        return new AzureTableRunStore(config);
    }
}
