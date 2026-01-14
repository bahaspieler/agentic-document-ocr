using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;

namespace OcrAgent.Plugins.Geometric;

public sealed class RotatePlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public RotatePlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Rotates the given image by specified angles. It returns the comma-separated list of references of generated images.")]
    public Task<string> RotateImage(
        [Description("The reference number of the image to rotate.")] string imageReference,
        [Description("The starting angle in degrees (0-360).")] int startAngle,
        [Description("The ending angle in degrees (0-360).")] int endAngle,
        [Description("The angle step in degrees.")] int angleStep)
    {
        // TODO: inputs validation (step > 0 and start <= end)

        // TODO: Get the original image using imageReference

        // TODO: Start a loop to create each rotation variant:
        //   - Calculate rotation for the current angle.
        //   - Adjust the canvas size to handle corners.
        //   - Save the new image.
        //   - New reference IDs.

        // Return new IDs
        return Task.FromResult(string.Empty);
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.RotateImage);
    }
}