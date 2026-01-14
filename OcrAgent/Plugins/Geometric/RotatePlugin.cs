using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;

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
        if (angleStep <= 0) throw new ArgumentException("angleStep must be > 0");
        if (endAngle < startAngle) throw new ArgumentException("endAngle must be >= startAngle");

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);

        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        int w = src.Width;
        int h = src.Height;
        var center = new Point2f(w / 2.0f, h / 2.0f);
        var results = new List<string>();

        // Return new IDs
        return Task.FromResult(string.Empty);
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.RotateImage);
    }
}