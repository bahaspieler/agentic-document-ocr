using OcrAgent.Storage;

namespace OcrAgent.Plugins;

/// <summary>
/// Helper utilities for image processing plugins.
/// </summary>
internal static class PluginHelpers
{
    /// <summary>
    /// Maximum number of outputs to generate for any operation.
    /// This limits resource usage and keeps output manageable.
    /// </summary>
    public const int MaxOutputs = 20;
}
    /// <summary>
    /// Generates a unique reference ID by appending a number suffix if the base reference already exists.
    /// </summary>
    /// <param name="store">The image store to check for existing references.</param>
    /// <param name="baseRef">The base reference ID.</param>
    /// <returns>A unique reference ID for the store.</returns>
    public static string MakeUniqueRef(IImageStore store, string baseRef)
    {
        // TODO: implement unique reference generation
        return baseRef;
    }
