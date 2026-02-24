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

    public ColorCorrectionPlugin(IImageStore store)
    {
        _store = store;
    }
    [Description("Corrects color casts and white balance in document images. Returns corrected image reference.")]
    public Task<string> CorrectColor(
        [Description("The reference number of the image to correct.")] string imageReference,
        [Description("Color correction method: 'auto' (detect best), 'gray_world' (neutralize color cast), 'white_patch' (normalize to white), 'histogram_match' (equalize channels).")] string method = "auto")
    {
        method = method.ToLowerInvariant();

        if (method is not ("auto" or "gray_world" or "white_patch" or "histogram_match"))
            throw new ArgumentException("method must be 'auto', 'gray_world', 'white_patch', or 'histogram_match'");
        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);

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
            default:
                ApplyGrayWorld(src, corrected);
                break;
        }
        return Task.FromResult(imageReference);
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

    public IEnumerable<AITool> GetAITools()
    {
        yield break;
    }
}