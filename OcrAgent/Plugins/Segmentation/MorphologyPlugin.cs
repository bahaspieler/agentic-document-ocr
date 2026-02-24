using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Segmentation;

/// <summary>
/// Plugin for morphological operations on images.
/// Useful as a post-binarization cleanup step to fill gaps in characters (close),
/// remove noise specks (open), or detect edges (gradient).
/// </summary>
public sealed class MorphologyPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public MorphologyPlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Applies morphological operations to clean up images (especially after binarization). 'close' fills gaps in broken characters, 'open' removes noise specks, 'gradient' extracts edges. Returns processed image reference.")]
    public Task<string> ApplyMorphology(
        [Description("The reference number of the image to process.")] string imageReference,
        [Description("Morphological operation: 'erode', 'dilate', 'open' (remove noise), 'close' (fill gaps), 'gradient' (edge outline).")] string operation = "close",
        [Description("Kernel size (must be odd, 3-21). Larger = stronger effect.")] int kernelSize = 3,
        [Description("Number of iterations (1-10). More iterations = stronger effect.")] int iterations = 1,
        [Description("Kernel shape: 'rect' (sharp edges), 'cross' (directional), 'ellipse' (smooth/round).")] string kernelShape = "rect")
    {
        operation = operation.ToLowerInvariant();
        kernelShape = kernelShape.ToLowerInvariant();

        if (operation is not ("erode" or "dilate" or "open" or "close" or "gradient"))
            throw new ArgumentException("operation must be 'erode', 'dilate', 'open', 'close', or 'gradient'");

        if (kernelSize < 3 || kernelSize > 21 || kernelSize % 2 == 0)
            throw new ArgumentException("kernelSize must be an odd number between 3 and 21");

        if (iterations < 1 || iterations > 10)
            throw new ArgumentException("iterations must be between 1 and 10");

        if (kernelShape is not ("rect" or "cross" or "ellipse"))
            throw new ArgumentException("kernelShape must be 'rect', 'cross', or 'ellipse'");

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);

        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        var shape = kernelShape switch
        {
            "cross" => MorphShapes.Cross,
            "ellipse" => MorphShapes.Ellipse,
            _ => MorphShapes.Rect
        };

        using var kernel = Cv2.GetStructuringElement(shape, new Size(kernelSize, kernelSize));
        using var result = new Mat();

        switch (operation)
        {
            case "erode":
                Cv2.Erode(src, result, kernel, iterations: iterations);
                break;
            case "dilate":
                Cv2.Dilate(src, result, kernel, iterations: iterations);
                break;
            case "open":
                Cv2.MorphologyEx(src, result, MorphTypes.Open, kernel, iterations: iterations);
                break;
            case "close":
                Cv2.MorphologyEx(src, result, MorphTypes.Close, kernel, iterations: iterations);
                break;
            case "gradient":
                Cv2.MorphologyEx(src, result, MorphTypes.Gradient, kernel, iterations: iterations);
                break;
        }

        Cv2.ImEncode(".png", result, out var pngBytes);

        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "morphology",
            ["operation"] = operation,
            ["kernelSize"] = kernelSize.ToString(),
            ["iterations"] = iterations.ToString(),
            ["kernelShape"] = kernelShape
        };

        var baseRef = $"{imageReference}_morphology_{operation}";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);

        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.ApplyMorphology);
    }
}
