using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.Json;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Segmentation;

public sealed class BinarizePlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public BinarizePlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Converts image to binary (black/white) for OCR. 'auto' analyzes the image and selects the best method. 'sauvola' is best for uneven illumination.")]
    public Task<string> BinarizeImage(
        [Description("The reference number of the image to binarize.")] string imageReference,
        [Description("Binarization method: 'auto' (recommended), 'otsu', 'adaptive_mean', 'adaptive_gaussian', or 'sauvola'.")] string method = "otsu",
        [Description("Block size for adaptive/sauvola methods (must be odd, e.g. 11, 15, 51).")] int blockSize = 11,
        [Description("Constant subtracted from threshold (0-20). For sauvola, controls k parameter (0.1-0.5, default 0.2).")] int constant = 2,
        [Description("Invert colors for dark backgrounds.")] bool invertForDarkBackground = false,
        [Description("Optional quality profile JSON from AssessQuality. Used by 'auto' to select the best method.")] string? qualityProfile = null)
    {
        method = method.ToLowerInvariant();
        if (method != "auto" && method != "otsu" && method != "adaptive_mean"
            && method != "adaptive_gaussian" && method != "sauvola")
            throw new ArgumentException("method must be 'auto', 'otsu', 'adaptive_mean', 'adaptive_gaussian', or 'sauvola'");

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        if (method == "auto")
            method = SelectBestMethod(gray, qualityProfile);

        if (method is "adaptive_mean" or "adaptive_gaussian" or "sauvola")
        {
            if (blockSize < 3 || blockSize % 2 == 0)
                throw new ArgumentException("blockSize must be an odd number >= 3 for adaptive/sauvola methods");
        }

        using var binary = new Mat();
        switch (method)
        {
            case "otsu":
                Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                break;
            case "adaptive_mean":
                Cv2.AdaptiveThreshold(gray, binary, 255, AdaptiveThresholdTypes.MeanC,
                    ThresholdTypes.Binary, blockSize, constant);
                break;
            case "adaptive_gaussian":
                Cv2.AdaptiveThreshold(gray, binary, 255, AdaptiveThresholdTypes.GaussianC,
                    ThresholdTypes.Binary, blockSize, constant);
                break;
            case "sauvola":
                ApplySauvola(gray, binary, blockSize > 11 ? blockSize : 51, 0.2, 128);
                break;
        }

        if (invertForDarkBackground)
            Cv2.BitwiseNot(binary, binary);

        Cv2.ImEncode(".png", binary, out var pngBytes);

        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "binarize",
            ["method"] = method,
            ["blockSize"] = blockSize.ToString(),
            ["constant"] = constant.ToString(),
            ["inverted"] = invertForDarkBackground.ToString()
        };

        var baseRef = $"{imageReference}_binarize_{method}";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);
        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    private static string SelectBestMethod(Mat gray, string? qualityProfile)
    {
        double contrast = 0;
        double uniformity = 1.0;

        if (!string.IsNullOrEmpty(qualityProfile))
        {
            try
            {
                using var doc = JsonDocument.Parse(qualityProfile);
                if (doc.RootElement.TryGetProperty("contrast", out var c))
                    contrast = c.GetDouble();
                if (doc.RootElement.TryGetProperty("illuminationUniformity", out var u))
                    uniformity = u.GetDouble();

                if (contrast < 20) return "adaptive_gaussian";
                if (uniformity < 0.75) return "sauvola";
                return "otsu";
            }
            catch { }
        }

        // Inline analysis: check histogram bimodality via Otsu's between-class variance
        Cv2.MeanStdDev(gray, out _, out var stddev);
        contrast = stddev[0];

        if (contrast < 20) return "adaptive_gaussian";

        double otsuThresh = Cv2.Threshold(gray, new Mat(), 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        double ratio = otsuThresh / 255.0;

        // Strong bimodal: Otsu threshold is well-centered
        if (ratio > 0.25 && ratio < 0.75 && contrast > 40)
            return "otsu";

        return "sauvola";
    }

    private static void ApplySauvola(Mat gray, Mat binary, int windowSize, double k, double R)
    {
        if (windowSize % 2 == 0) windowSize++;

        using var grayF = new Mat();
        gray.ConvertTo(grayF, MatType.CV_64F);

        using var mean = new Mat();
        Cv2.BoxFilter(grayF, mean, MatType.CV_64F, new Size(windowSize, windowSize));

        using var sqr = new Mat();
        Cv2.Multiply(grayF, grayF, sqr);

        using var meanSqr = new Mat();
        Cv2.BoxFilter(sqr, meanSqr, MatType.CV_64F, new Size(windowSize, windowSize));

        // variance = E[X^2] - E[X]^2
        using var meanSquared = new Mat();
        Cv2.Multiply(mean, mean, meanSquared);

        using var variance = new Mat();
        Cv2.Subtract(meanSqr, meanSquared, variance);
        Cv2.Max(variance, 0, variance);

        using var stddev = new Mat();
        Cv2.Sqrt(variance, stddev);

        // T(x,y) = mean * (1 + k * (std / R - 1))
        using var threshold = new Mat();
        using var stdOverR = new Mat();
        stddev.ConvertTo(stdOverR, MatType.CV_64F, 1.0 / R);

        using var factor = new Mat();
        Cv2.Subtract(stdOverR, Scalar.All(1.0), factor);
        Cv2.Multiply(factor, Scalar.All(k), factor);
        Cv2.Add(factor, Scalar.All(1.0), factor);
        Cv2.Multiply(mean, factor, threshold);

        binary.Create(gray.Size(), MatType.CV_8UC1);
        for (int y = 0; y < gray.Rows; y++)
        {
            for (int x = 0; x < gray.Cols; x++)
            {
                double pixVal = grayF.At<double>(y, x);
                double thresh = threshold.At<double>(y, x);
                binary.Set(y, x, pixVal > thresh ? (byte)255 : (byte)0);
            }
        }
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.BinarizeImage);
    }
}
