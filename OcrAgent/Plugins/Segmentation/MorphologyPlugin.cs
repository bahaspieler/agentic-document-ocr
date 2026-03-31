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

    /// <summary>
    /// Creates a plugin bound to the given image store.
    /// </summary>
    /// <param name="store">Store for input and output images.</param>
    public MorphologyPlugin(IImageStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Runs erode, dilate, open, close, gradient, or connected-component speckle removal and saves a PNG.
    /// </summary>
    /// <param name="imageReference">Source image id.</param>
    /// <param name="operation">Morphology op or remove_speckles.</param>
    /// <param name="kernelSize">Odd kernel size for morphology ops.</param>
    /// <param name="iterations">Repeat count for morphology ops.</param>
    /// <param name="kernelShape">rect, cross, or ellipse.</param>
    /// <param name="minArea">Minimum component area when operation is remove_speckles.</param>
    /// <returns>New image reference id.</returns>
    [Description("Applies morphological operations to clean up images. 'close' fills gaps, 'open' removes noise, 'remove_speckles' removes small isolated components by area. Returns processed image reference.")]
    public Task<string> ApplyMorphology(
        [Description("The reference number of the image to process.")] string imageReference,
        [Description("Morphological operation: 'erode', 'dilate', 'open', 'close', 'gradient', or 'remove_speckles'.")] string operation = "close",
        [Description("Kernel size (odd, 3-21). Larger = stronger effect. Ignored for 'remove_speckles'.")] int kernelSize = 3,
        [Description("Number of iterations (1-10). Ignored for 'remove_speckles'.")] int iterations = 1,
        [Description("Kernel shape: 'rect', 'cross', or 'ellipse'. Ignored for 'remove_speckles'.")] string kernelShape = "rect",
        [Description("Minimum component area in pixels for 'remove_speckles'. Components smaller than this are removed.")] int minArea = 20)
    {
        operation = operation.ToLowerInvariant();
        kernelShape = kernelShape.ToLowerInvariant();

        if (operation is not ("erode" or "dilate" or "open" or "close" or "gradient" or "remove_speckles"))
            throw new ArgumentException("operation must be 'erode', 'dilate', 'open', 'close', 'gradient', or 'remove_speckles'");

        if (operation != "remove_speckles")
        {
            if (kernelSize < 3 || kernelSize > 21 || kernelSize % 2 == 0)
                throw new ArgumentException("kernelSize must be an odd number between 3 and 21");
            if (iterations < 1 || iterations > 10)
                throw new ArgumentException("iterations must be between 1 and 10");
            if (kernelShape is not ("rect" or "cross" or "ellipse"))
                throw new ArgumentException("kernelShape must be 'rect', 'cross', or 'ellipse'");
        }

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        using var result = new Mat();

        if (operation == "remove_speckles")
        {
            ApplyRemoveSpeckles(src, result, minArea);
        }
        else
        {
            var shape = kernelShape switch
            {
                "cross" => MorphShapes.Cross,
                "ellipse" => MorphShapes.Ellipse,
                _ => MorphShapes.Rect
            };

            using var kernel = Cv2.GetStructuringElement(shape, new Size(kernelSize, kernelSize));
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
        if (operation == "remove_speckles")
            meta["minArea"] = minArea.ToString();

        var baseRef = $"{imageReference}_morphology_{operation}";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);
        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    /// <summary>
    /// Otsu-binarizes (unless already binary), drops connected components below minArea, then copies or masks color output.
    /// </summary>
    private static void ApplyRemoveSpeckles(Mat src, Mat dst, int minArea, bool assumeBinary = false)
    {
        using var gray = new Mat();
        if (src.Channels() == 1)
            src.CopyTo(gray);
        else
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        using var binary = new Mat();
        if (assumeBinary)
            gray.CopyTo(binary);
        else
            Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        int numLabels = Cv2.ConnectedComponentsWithStats(binary, labels, stats, centroids);

        var smallLabels = new HashSet<int>();
        for (int i = 1; i < numLabels; i++)
        {
            if (stats.At<int>(i, 4) < minArea)
                smallLabels.Add(i);
        }

        using var mask = new Mat(binary.Size(), MatType.CV_8UC1, Scalar.All(255));
        for (int y = 0; y < labels.Rows; y++)
            for (int x = 0; x < labels.Cols; x++)
                if (smallLabels.Contains(labels.At<int>(y, x)))
                    mask.Set(y, x, (byte)0);

        if (src.Channels() == 1)
        {
            Cv2.BitwiseAnd(src, mask, dst);
        }
        else
        {
            src.CopyTo(dst);
            using var maskInv = new Mat();
            Cv2.BitwiseNot(mask, maskInv);
            using var white = new Mat(src.Size(), src.Type(), Scalar.White);
            white.CopyTo(dst, maskInv);
        }
    }

    /// <summary>
    /// Exposes morphology as an agent tool.
    /// </summary>
    /// <returns>Single wrapped function.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.ApplyMorphology);
    }
}
