using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Enhancement;

/// <summary>
/// Plugin for enhancing image contrast using several methods.
/// Supports auto-contrast, histogram equalization, and CLAHE (Contrast Limited Adaptive Histogram Equalization).
/// </summary>
public sealed class ContrastPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public ContrastPlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Enhances the contrast of the given image using the specified method. Returns the reference of the generated image.")]
    public Task<string> EnhanceContrast(
        [Description("The reference number of the image to enhance.")] string imageReference,
        [Description("The contrast enhancement method: 'auto' (normalize to 0-255), 'histogram' (histogram equalization), or 'clahe' (adaptive).")] string method = "auto")
    {
        method = method.ToLowerInvariant();
        if (method != "auto" && method != "histogram" && method != "clahe")
            throw new ArgumentException("method must be 'auto', 'histogram', or 'clahe'");

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);

        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        using var enhanced = new Mat();
        switch (method)
        {
            case "auto":
                ApplyAutoContrast(src, enhanced);
                break;
        }
        // Return the reference

        return Task.FromResult(string.Empty);
    }


    private void ApplyAutoContrast(Mat src, Mat dst)
    {
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        Cv2.MinMaxLoc(gray, out double minVal, out double maxVal);

        double alpha = 255.0 / (maxVal - minVal);
        double beta = -minVal * alpha;

        src.ConvertTo(dst, MatType.CV_8UC3, alpha, beta);
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.EnhanceContrast);
    }
}
