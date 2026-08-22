using OcrAgent.Storage;

namespace OcrAgent.Plugins;

/// <summary>
/// Helper utilities for image processing plugins.
/// </summary>
internal static class PluginHelpers
{
    /// <summary>
    /// Generates a unique reference ID by appending a number suffix if the base reference already exists.
    /// </summary>
    /// <remarks>
    /// Plugins build the base from the reference they were handed, which carries the scope it was read
    /// from whenever it came from an earlier step. The name being chosen here is for a write, and a
    /// write lands in the store's active scope rather than wherever the input happened to live, so the
    /// scope is dropped first: otherwise a call on a workflow output would test one location for a free
    /// name and then write to another.
    /// </remarks>
    /// <param name="store">The image store to check for existing references.</param>
    /// <param name="baseRef">The base reference ID, which may name a scope.</param>
    /// <returns>A reference unused in the store's active write scope.</returns>
    public static string MakeUniqueRef(IImageStore store, string baseRef)
    {
        baseRef = ImageReferenceGenerator.Split(baseRef).Leaf;

        var candidate = baseRef;
        int i = 2;

        while (store.Exists(candidate))
        {
            candidate = $"{baseRef}_{i:D2}";
            i++;
        }

        return candidate;
    }

    /// <summary>
    /// Maximum number of outputs to generate for any operation.
    /// This limits resource usage and keeps output manageable.
    /// </summary>
    public const int MaxOutputs = 20;
}
