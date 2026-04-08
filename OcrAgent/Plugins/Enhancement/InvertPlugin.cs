using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Enhancement;

/// <summary>
/// Inverts image colors (dark becomes light, light becomes dark).
/// Useful for documents with light text on dark backgrounds.
/// </summary>
public sealed class InvertPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    /// <summary>
    /// Creates the plugin with the image store used to load inputs and save outputs.
    /// </summary>
    /// <param name="store">Image storage implementation.</param>
    public InvertPlugin(IImageStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Optionally inverts BGR with BitwiseNot per mode, then saves a PNG with metadata about the decision.
    /// </summary>
    /// <param name="imageReference">Input image reference.</param>
    /// <param name="mode">auto, always, or smart.</param>
    /// <returns>Reference of the saved image (copy or inverted).</returns>
    [Description("Inverts image colors (dark becomes light, light becomes dark). Use for documents with light text on dark backgrounds. 'auto' detects if inversion is needed, 'always' forces inversion, 'smart' analyzes histogram for dark-background documents. Returns image reference.")]
    public Task<string> InvertImage(
        [Description("The reference number of the image to invert.")] string imageReference,
        [Description("Inversion mode: 'auto' (invert if dark-dominant), 'always' (unconditional), 'smart' (histogram analysis for dark-background detection).")] string mode = "auto")
    {
        mode = mode.ToLowerInvariant();

        if (mode is not ("auto" or "always" or "smart"))
            throw new ArgumentException("mode must be 'auto', 'always', or 'smart'");

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImDecode(File.ReadAllBytes(inputPath), ImreadModes.Color);

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

    /// <summary>
    /// Examines the grayscale histogram to determine if a document has a dark background.
    /// Considers both the position of the main histogram peak in the dark region and whether over 60% of pixels are dark.
    /// </summary>
    private static (bool ShouldInvert, double DarkPixelRatio) AnalyzeDarkBackground(Mat gray)
    {
        double totalPixels = gray.Rows * gray.Cols;

        // count pixels below brightness threshold of 100
        using var darkMask = new Mat();
        Cv2.Threshold(gray, darkMask, 100, 255, ThresholdTypes.BinaryInv);
        double darkPixels = Cv2.CountNonZero(darkMask);
        double darkRatio = darkPixels / totalPixels;

        // check histogram peak location
        using var hist = new Mat();
        int[] histSize = { 256 };
        Rangef[] ranges = { new Rangef(0, 256) };
        Cv2.CalcHist(new[] { gray }, new[] { 0 }, null, hist, 1, histSize, ranges);

        // the main histogram peak
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

        // dark background if: main histogram peak is in dark range and dark pixels > 60%
        bool isDarkBackground = peakBin < 80 && darkRatio > 0.60;

        return (isDarkBackground, darkRatio);
    }

    /// <summary>
    /// Registers inversion as an AI-callable tool.
    /// </summary>
    /// <returns>Tool bindings for this plugin.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.InvertImage);
    }
}
