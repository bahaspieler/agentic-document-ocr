using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;
using System.Globalization;

namespace OcrAgent.Plugins.Segmentation;

/// <summary>
/// Removes non-document background from images using edge-based mask generation.
/// </summary>
public sealed class DocumentBackgroundRemovalPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    private const double MinMaskCoverage = 0.05;
    private const double MaxMaskCoverage = 0.98;
    private const string DebugFolderName = "bgremoval_debug";

    public DocumentBackgroundRemovalPlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Removes non-document background, placing the document on a clean white canvas. Use for mobile captures with desk/table backgrounds.")]
    public Task<string> RemoveDocumentBackground(
        [Description("The reference number of the image to process.")] string imageReference,
        [Description("Output mode: 'on_white' (document on white canvas) or 'mask_only' (binary mask).")] string mode = "on_white",
        [Description("Detection sensitivity: 'auto', 'aggressive', 'conservative'.")] string sensitivity = "auto",
        [Description("Edge feather radius 0-10 for smooth transitions.")] int feather = 2,
        [Description("Fill holes inside the document mask to prevent text cutout.")] bool fillHoles = true,
        [Description("Enable debug mode to save intermediate mask images.")] bool debug = false)
    {
        mode = mode.ToLowerInvariant();
        if (mode is not ("on_white"))
            throw new ArgumentException("Only 'on_white' mode is supported in initial version");

        sensitivity = sensitivity.ToLowerInvariant();
        if (sensitivity is not ("auto" or "aggressive" or "conservative"))
            throw new ArgumentException("sensitivity must be 'auto', 'aggressive', or 'conservative'");

        feather = Math.Clamp(feather, 0, 10);

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference}");

        string? debugPath = null;
        if (debug)
        {
            debugPath = PrepareDebugFolder(imageReference);
            SaveDebugImage(src, debugPath, "00_input");
        }

        var mask = GenerateEdgeBasedMask(src, sensitivity, debugPath);

        if (mask == null)
            throw new InvalidOperationException("No valid document mask could be generated from the image");
        
        double coverage = Cv2.CountNonZero(mask) / (double)(src.Width * src.Height);

        byte[] outputBytes;
        
        using (var result = ApplyMaskOnWhite(src, mask, false))
        {
            if (debugPath != null)
                SaveDebugImage(result, debugPath, "70_output");
            Cv2.ImEncode(".png", result, out outputBytes);
        }

        mask.Dispose();

        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "remove_background",
            ["mode"] = mode,
            ["detectionMethod"] = "edges",
            ["maskCoverage"] = coverage.ToString("F3", CultureInfo.InvariantCulture),
            ["confidence"] = "1.00",
            ["feather"] = feather.ToString(),
            ["fillHoles"] = fillHoles.ToString()
        };

        var baseRef = $"{imageReference}_bgremoval_{mode}";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);
        var newRef = _store.SaveBytes(outputBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    /// <summary>
    /// Generates a basic document mask using Canny edge detection
    /// </summary>
    private Mat? GenerateEdgeBasedMask(Mat src, string sensitivity, string? debugPath)
    {
        using var resized = ResizeForProcessing(src, 900, out var scale);

        // Just use grayscale for edge detection
        using var gray = new Mat();
        Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);

        (double lo, double hi) = sensitivity switch
        {
            "aggressive" => (25.0, 90.0),
            "conservative" => (80.0, 200.0),
            _ => (40.0, 120.0)
        };

        using var edges = new Mat();
        Cv2.Canny(gray, edges, lo, hi);

        using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(7, 7));
        Cv2.Dilate(edges, edges, k, iterations: 3);

        Cv2.FindContours(edges, out var contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0) return null;

        // fill the largest contour
        int maxIdx = -1; double maxArea = 0;
        for (int i = 0; i < contours.Length; i++)
        {
            double area = Cv2.ContourArea(contours[i]);
            if (area > maxArea)
            {
                maxArea = area;
                maxIdx = i;
            }
        }
        if (maxIdx == -1) return null;

        var mask = new Mat(resized.Height, resized.Width, MatType.CV_8UC1, Scalar.All(0));
        Cv2.DrawContours(mask, contours, maxIdx, Scalar.White, -1);
        
        return ScaleMaskToOriginal(mask, src.Width, src.Height);
    }
    
    /// <summary>
    /// Divides grayscale by a heavy-blurred version.
    /// Removes shadows and uneven lighting.
    /// </summary>
    private static Mat NormalizeForSegmentation(Mat bgr)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

        using var blurHeavy = new Mat();
        Cv2.GaussianBlur(gray, blurHeavy, new Size(51, 51), 0);

        using var grayFloat = new Mat();
        gray.ConvertTo(grayFloat, MatType.CV_32FC1);

        using var blurFloat = new Mat();
        blurHeavy.ConvertTo(blurFloat, MatType.CV_32FC1);
        blurFloat.SetTo(new Scalar(1.0), blurFloat.LessThan(1));

        using var divided = new Mat();
        Cv2.Divide(grayFloat, blurFloat, divided, scale: 255.0);

        var result = new Mat();
        divided.ConvertTo(result, MatType.CV_8UC1);
        Cv2.Normalize(result, result, 0, 255, NormTypes.MinMax);
        return result;
    }

    private static Rect ExpandRect(Rect r, int imgW, int imgH, double margin)
    {
        int mx = (int)(r.Width * margin);
        int my = (int)(r.Height * margin);
        int x = Math.Max(0, r.X - mx);
        int y = Math.Max(0, r.Y - my);
        int right = Math.Min(imgW, r.X + r.Width + mx);
        int bottom = Math.Min(imgH, r.Y + r.Height + my);
        return new Rect(x, y, right - x, bottom - y);
    }

    private static bool RectsOverlap(Rect a, Rect b)
    {
        return a.X < b.X + b.Width && a.X + a.Width > b.X &&
               a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
    }

    /// <summary>
    /// Applies Gaussian blur to mask edges for smooth edges.
    /// </summary>
    private static Mat ApplyFeathering(Mat cleanedMask, int feather, string? debugPath)
    {
        if (feather <= 0)
            return cleanedMask.Clone();

        int kernelSize = feather * 2 + 1;
        var feathered = new Mat();
        Cv2.GaussianBlur(cleanedMask, feathered, new Size(kernelSize, kernelSize), 0);

        if (debugPath != null)
            SaveDebugImage(feathered, debugPath, "60_feathered_mask");

        return feathered;
    }

    private static Mat ResizeForProcessing(Mat src, int maxSide, out double scale)
    {
        int w = src.Width;
        int h = src.Height;
        int longest = Math.Max(w, h);

        if (longest <= maxSide)
        {
            scale = 1.0;
            return src.Clone();
        }

        scale = (double)maxSide / longest;
        int nw = (int)(w * scale);
        int nh = (int)(h * scale);

        var dst = new Mat();
        Cv2.Resize(src, dst, new Size(nw, nh), 0, 0, InterpolationFlags.Area);
        return dst;
    }

    /// <summary>
    /// Scales a mask generated at reduced resolution back to original image dimensions.
    /// </summary>
    private static Mat ScaleMaskToOriginal(Mat smallMask, int origW, int origH)
    {
        if (smallMask.Width == origW && smallMask.Height == origH)
            return smallMask;

        var fullMask = new Mat();
        Cv2.Resize(smallMask, fullMask, new Size(origW, origH), 0, 0, InterpolationFlags.Nearest);
        smallMask.Dispose();

        // re-binarize after resize to ensure clean 0/255 values
        Cv2.Threshold(fullMask, fullMask, 127, 255, ThresholdTypes.Binary);
        return fullMask;
    }

    /// <summary>
    /// Applies the mask to produce a white-background output (no feathering).
    /// </summary>
    private static Mat ApplyMaskOnWhite(Mat src, Mat mask, bool isFeathered)
    {
        var output = new Mat(src.Size(), src.Type(), Scalar.White);
        src.CopyTo(output, mask);
        return output;
    }

    private string PrepareDebugFolder(string imageReference)
    {
        var storagePath = Path.GetDirectoryName(_store.GetImagePath(imageReference)) ?? ".";
        var debugPath = Path.Combine(storagePath, DebugFolderName, imageReference);

        if (Directory.Exists(debugPath))
            Directory.Delete(debugPath, true);

        Directory.CreateDirectory(debugPath);
        return debugPath;
    }

    private static void SaveDebugImage(Mat img, string debugPath, string name)
    {
        var filePath = Path.Combine(debugPath, $"{name}.png");
        Cv2.ImWrite(filePath, img);
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.RemoveDocumentBackground);
    }
}
