using Microsoft.Extensions.AI;

namespace OcrAgent.Plugins;

/// <summary>
/// Interface for image preprocessing plugins to be used by the AI agent.
/// Each plugin exposes one or more AI tools that perform specific image transformations.
/// </summary>
public interface IImageProcessingTool
{
    /// <summary>
    /// Returns the AI tools this plugin exposes to the agent.
    /// </summary>
    /// <returns>Sequence of registered tools.</returns>
    IEnumerable<AITool> GetAITools();
}
