using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.Json;
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

    [Description("Enhances the contrast of the given image. 'auto_lab' applies LAB-based CLAHE with highlight protection (recommended for documents). Returns the reference of the generated image, or the input reference unchanged if contrast is already adequate.")]
    public Task<string> EnhanceContrast(
        [Description("The reference number of the image to enhance.")] string imageReference,
        [Description("The contrast enhancement method: 'auto' (normalize to 0-255), 'histogram' (histogram equalization), 'clahe' (adaptive), or 'auto_lab' (LAB CLAHE with highlight protection).")] string method = "auto",
        [Description("Optional quality profile JSON from AssessQuality. When provided with 'auto_lab', skips if contrast is already adequate.")] string? qualityProfile = null)
    {
        method = method.ToLowerInvariant();
        if (method != "auto" && method != "histogram" && method != "clahe" && method != "auto_lab")
            throw new ArgumentException("method must be 'auto', 'histogram', 'clahe', or 'auto_lab'");

        if (method == "auto_lab" && !string.IsNullOrEmpty(qualityProfile))
        {
            try
            {
                using var doc = JsonDocument.Parse(qualityProfile);
                if (doc.RootElement.TryGetProperty("contrast", out var c) && c.GetDouble() > 55)
                    return Task.FromResult(imageReference);
            }
            catch { }
        }

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

            case "histogram":
                ApplyHistogramEqualization(src, enhanced);
                break;

            case "clahe":
                ApplyCLAHE(src, enhanced);
                break;
            case "auto_lab":
                ApplyAutoLabCLAHE(src, enhanced);
                break;
        }

        Cv2.ImEncode(".png", enhanced, out var pngBytes);

        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "contrast",
            ["method"] = method
        };

        var baseRef = $"{imageReference}_contrast_{method}";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);

        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    private static void ApplyAutoContrast(Mat src, Mat dst)
    {
        // Convert to grayscale for min/max
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        Cv2.MinMaxLoc(gray, out double minVal, out double maxVal);

        // 0-255 range
        double alpha = 255.0 / (maxVal - minVal);
        double beta = -minVal * alpha;

        src.ConvertTo(dst, MatType.CV_8UC3, alpha, beta);
    }

    private static void ApplyHistogramEqualization(Mat src, Mat dst)
    {
        // Convert to YCrCb
        using var ycrcb = new Mat();
        Cv2.CvtColor(src, ycrcb, ColorConversionCodes.BGR2YCrCb);

        // Split channels
        var channels = Cv2.Split(ycrcb);

        // Equalize the Y channel (luminance)
        Cv2.EqualizeHist(channels[0], channels[0]);

        // Merge back
        using var merged = new Mat();
        Cv2.Merge(channels, merged);

        // Convert back to BGR
        Cv2.CvtColor(merged, dst, ColorConversionCodes.YCrCb2BGR);

        // Clean up channels
        foreach (var ch in channels)
            ch.Dispose();
    }

    private static void ApplyCLAHE(Mat src, Mat dst)
    {
        // Convert to LAB
        using var lab = new Mat();
        Cv2.CvtColor(src, lab, ColorConversionCodes.BGR2Lab);
        var channels = Cv2.Split(lab);
        using var clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new Size(8, 8));
        clahe.Apply(channels[0], channels[0]);
        using var merged = new Mat();
        Cv2.Merge(channels, merged);
        Cv2.CvtColor(merged, dst, ColorConversionCodes.Lab2BGR);
        foreach (var ch in channels) ch.Dispose();
    }

    private static void ApplyAutoLabCLAHE(Mat src, Mat dst)
    {
        using var lab = new Mat();
        Cv2.CvtColor(src, lab, ColorConversionCodes.BGR2Lab);
        var channels = Cv2.Split(lab);

        // Clamp L channel at 240 to prevent burning thin text
        Cv2.Min(channels[0], 240, channels[0]);

        using var clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new Size(8, 8));
        clahe.Apply(channels[0], channels[0]);

        using var merged = new Mat();
        Cv2.Merge(channels, merged);
        Cv2.CvtColor(merged, dst, ColorConversionCodes.Lab2BGR);
        foreach (var ch in channels) ch.Dispose();
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.EnhanceContrast);
    }
}
