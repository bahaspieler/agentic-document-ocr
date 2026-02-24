using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Enhancement;

public sealed class InvertPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public InvertPlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Inverts image colors (dark becomes light, light becomes dark). Use for documents with light text on dark backgrounds. 'auto' detects if inversion is needed, 'always' forces inversion, 'smart' analyzes histogram for dark-background documents. Returns image reference.")]
    public Task<string> InvertImage(
        [Description("The reference number of the image to invert.")] string imageReference,
        [Description("Inversion mode: 'auto' (invert if dark-dominant), 'always' (unconditional), 'smart' (histogram analysis for dark-background detection).")] string mode = "auto")
    {
        mode = mode.ToLowerInvariant();

        if (mode is not ("auto" or "always" or "smart"))
            throw new ArgumentException("mode must be 'auto', 'always', or 'smart'");

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);

        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        bool shouldInvert;
        double meanBrightness = Cv2.Mean(gray).Val0;
        double darkPixelRatio = 0;

        switch (mode)
        {
            case "always":
                shouldInvert = true;
                break;

            case "smart":
                (shouldInvert, darkPixelRatio) = AnalyzeDarkBackground(gray);
                break;

            default: // auto
                shouldInvert = meanBrightness < 127;
                break;
        }

        using var output = new Mat();
        if (shouldInvert)
        {
            Cv2.BitwiseNot(src, output);
        }
        else
        {
            src.CopyTo(output);
        }

        Cv2.ImEncode(".png", output, out var pngBytes);

        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "invert",
            ["mode"] = mode,
            ["wasInverted"] = shouldInvert.ToString(),
            ["meanBrightness"] = meanBrightness.ToString("F1")
        };

        if (mode == "smart")
        {
            meta["darkPixelRatio"] = darkPixelRatio.ToString("F3");
        }

        var baseRef = $"{imageReference}_invert_{mode}";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);

        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    private static (bool ShouldInvert, double DarkPixelRatio) AnalyzeDarkBackground(Mat gray)
    {
        double totalPixels = gray.Rows * gray.Cols;

        using var darkMask = new Mat();
        Cv2.Threshold(gray, darkMask, 100, 255, ThresholdTypes.BinaryInv);
        double darkPixels = Cv2.CountNonZero(darkMask);
        double darkRatio = darkPixels / totalPixels;

        using var hist = new Mat();
        int[] histSize = { 256 };
        Rangef[] ranges = { new Rangef(0, 256) };
        Cv2.CalcHist(new[] { gray }, new[] { 0 }, null, hist, 1, histSize, ranges);

        int peakBin = 0;
        float peakVal = 0;
        for (int i = 0; i < 256; i++)
        {
            float val = hist.At<float>(i);
            if (val > peakVal)
            {
                peakVal = val;
                peakBin = i;
            }
        }

        bool isDarkBackground = peakBin < 80 && darkRatio > 0.60;

        return (isDarkBackground, darkRatio);
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield break;
    }
}