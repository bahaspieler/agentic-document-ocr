using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;
namespace OcrAgent.Plugins.Enhancement;

/// <summary>
/// Fixes color casts in document images from lighting.
/// Adjusts white balance to improve OCR results.
/// </summary>
public sealed class ColorCorrectionPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    /// <summary>
    /// Creates the plugin with the image store used to load inputs and save outputs.
    /// </summary>
    /// <param name="store">Image storage implementation.</param>
    public ColorCorrectionPlugin(IImageStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Applies gray-world, white-patch, histogram match, or auto-picked method, then saves a corrected PNG.
    /// </summary>
    /// <param name="imageReference">Input image reference.</param>
    /// <param name="method">auto, gray_world, white_patch, or histogram_match.</param>
    /// <returns>Reference of the saved color-corrected image.</returns>
    [Description("Corrects color casts and white balance in document images. Returns corrected image reference.")]
    public Task<string> CorrectColor(
        [Description("The reference number of the image to correct.")] string imageReference,
        [Description("Color correction method: 'auto' (detect best), 'gray_world' (neutralize color cast), 'white_patch' (normalize to white), 'histogram_match' (equalize channels).")] string method = "auto")
    {
        method = method.ToLowerInvariant();

        if (method is not ("auto" or "gray_world" or "white_patch" or "histogram_match"))
            throw new ArgumentException("method must be 'auto', 'gray_world', 'white_patch', or 'histogram_match'");
        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImDecode(File.ReadAllBytes(inputPath), ImreadModes.Color);

        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");
        string appliedMethod = method;

        if (method == "auto")
        {
            appliedMethod = DetectBestMethod(src);
        }
        using var corrected = new Mat();

        switch (appliedMethod)
        {
            case "gray_world":
                ApplyGrayWorld(src, corrected);
                break;
            case "white_patch":
                ApplyWhitePatch(src, corrected);
                break;
            case "histogram_match":
                ApplyHistogramMatch(src, corrected);
                break;
            default:
                ApplyGrayWorld(src, corrected);
                break;
        }
        Cv2.ImEncode(".png", corrected, out var pngBytes);

        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "color_correction",
            ["method"] = method,
            ["appliedMethod"] = appliedMethod
        };

        var baseRef = $"{imageReference}_color_{appliedMethod}";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);

        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    private static string DetectBestMethod(Mat src)
    {
        var channels = Cv2.Split(src);
        try
        {
            double meanB = Cv2.Mean(channels[0]).Val0;
            double meanG = Cv2.Mean(channels[1]).Val0;
            double meanR = Cv2.Mean(channels[2]).Val0;

            double overallMean = (meanB + meanG + meanR) / 3.0;

            // standard deviation across channel means measures color cast strength
            double variance = ((meanB - overallMean) * (meanB - overallMean) +
                               (meanG - overallMean) * (meanG - overallMean) +
                               (meanR - overallMean) * (meanR - overallMean)) / 3.0;
            double stdDev = Math.Sqrt(variance);

            // strong color cast so gray_world is best
            if (stdDev > 15)
                return "gray_world";

            // brightness skew (one channel much brighter) so white_patch is best
            double maxMean = Math.Max(meanB, Math.Max(meanG, meanR));
            double minMean = Math.Min(meanB, Math.Min(meanG, meanR));
            if (maxMean - minMean > 30)
                return "white_patch";

            return "gray_world";
        }
        finally
        {
            foreach (var ch in channels)
                ch.Dispose();
        }
    }

    private static void ApplyGrayWorld(Mat src, Mat dst)
    {
        var channels = Cv2.Split(src);
        try
        {
            double meanB = Cv2.Mean(channels[0]).Val0;
            double meanG = Cv2.Mean(channels[1]).Val0;
            double meanR = Cv2.Mean(channels[2]).Val0;

            double overallMean = (meanB + meanG + meanR) / 3.0;

            // Scale each channel to match overall mean
            double scaleB = meanB > 0 ? overallMean / meanB : 1.0;
            double scaleG = meanG > 0 ? overallMean / meanG : 1.0;
            double scaleR = meanR > 0 ? overallMean / meanR : 1.0;

            channels[0].ConvertTo(channels[0], MatType.CV_8UC1, scaleB);
            channels[1].ConvertTo(channels[1], MatType.CV_8UC1, scaleG);
            channels[2].ConvertTo(channels[2], MatType.CV_8UC1, scaleR);

            Cv2.Merge(channels, dst);
        }
        finally
        {
            foreach (var ch in channels)
                ch.Dispose();
        }
    }

   private static void ApplyWhitePatch(Mat src, Mat dst)
{
    var channels = Cv2.Split(src);
    try
    {
        double maxB = GetPercentileValue(channels[0], 99);
        double maxG = GetPercentileValue(channels[1], 99);
        double maxR = GetPercentileValue(channels[2], 99);

        double scaleB = maxB > 0 ? 255.0 / maxB : 1.0;
        double scaleG = maxG > 0 ? 255.0 / maxG : 1.0;
        double scaleR = maxR > 0 ? 255.0 / maxR : 1.0;

        channels[0].ConvertTo(channels[0], MatType.CV_8UC1, scaleB);
        channels[1].ConvertTo(channels[1], MatType.CV_8UC1, scaleG);
        channels[2].ConvertTo(channels[2], MatType.CV_8UC1, scaleR);

        Cv2.Merge(channels, dst);
    }
    finally
    {
        foreach (var ch in channels)
            ch.Dispose();
    }
}

private static double GetPercentileValue(Mat channel, double percentile)
{
    using var hist = new Mat();
    int[] histSize = { 256 };
    Rangef[] ranges = { new Rangef(0, 256) };
    Cv2.CalcHist(new[] { channel }, new[] { 0 }, null, hist, 1, histSize, ranges);

    double totalPixels = channel.Rows * channel.Cols;
    double threshold = totalPixels * percentile / 100.0;

    double cumulative = 0;
    for (int i = 0; i < 256; i++)
    {
        cumulative += hist.At<float>(i);
        if (cumulative >= threshold)
            return i;
    }

    return 255;
}

    private static void ApplyHistogramMatch(Mat src, Mat dst)
    {
        using var lab = new Mat();
        Cv2.CvtColor(src, lab, ColorConversionCodes.BGR2Lab);

        var channels = Cv2.Split(lab);
        try
        {
            // equalize the L channel (luminance)
            Cv2.EqualizeHist(channels[0], channels[0]);

            // equalize A and B channels
            using var clahe = Cv2.CreateCLAHE(clipLimit: 1.0, tileGridSize: new Size(8, 8));
            clahe.Apply(channels[1], channels[1]);
            clahe.Apply(channels[2], channels[2]);

            using var merged = new Mat();
            Cv2.Merge(channels, merged);

            Cv2.CvtColor(merged, dst, ColorConversionCodes.Lab2BGR);
        }
        finally
        {
            foreach (var ch in channels)
                ch.Dispose();
        }
    }


    /// <summary>
    /// Registers color correction as an AI-callable tool.
    /// </summary>
    /// <returns>Tool bindings for this plugin.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.CorrectColor);
    }
}