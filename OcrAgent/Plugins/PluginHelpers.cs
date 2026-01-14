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
