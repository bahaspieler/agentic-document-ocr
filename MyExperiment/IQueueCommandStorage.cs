using System.Threading.Tasks;

namespace MyExperiment;

/// <summary>Resolves the training image named by a natural-language queue command.</summary>
public interface IQueueCommandStorage
{
    /// <summary>Finds and downloads the image reference contained in a command.</summary>
    /// <param name="command">Queue command that names a training image.</param>
    /// <returns>The resolved reference, Blob name, and temporary local path.</returns>
    Task<ResolvedQueueImage> ResolveImageAsync(string command);
}
