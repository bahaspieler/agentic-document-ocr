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
        if (mode is not ("on_white" or "mask_only"))
            throw new ArgumentException("mode must be 'on_white' or 'mask_only'");

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

        using var cleanedMask = CleanupMask(mask, fillHoles, debugPath);

        double coverage = Cv2.CountNonZero(cleanedMask) / (double)(src.Width * src.Height);

        using var finalMask = ApplyFeathering(cleanedMask, feather, debugPath);

        byte[] outputBytes;
        if (mode == "mask_only")
        {
            Cv2.ImEncode(".png", cleanedMask, out outputBytes);
        }
        else
        {
            using var result = ApplyMaskOnWhite(src, finalMask, feather > 0);
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
        using var normalized = NormalizeForSegmentation(resized);

        using var filtered = new Mat();
        Cv2.BilateralFilter(normalized, filtered, 9, 75, 75);

        (double lo, double hi) = sensitivity switch
        {
            "aggressive" => (25.0, 90.0),
            "conservative" => (80.0, 200.0),
            _ => (40.0, 120.0)
        };

        using var edges = new Mat();
        Cv2.Canny(filtered, edges, lo, hi);

        using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(7, 7));
        Cv2.Dilate(edges, edges, k, iterations: 3);
        Cv2.MorphologyEx(edges, edges, MorphTypes.Close, k, iterations: 3);

        Cv2.FindContours(edges, out var contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0) return null;

        var mask = FillLargestContour(contours, resized.Width, resized.Height);
        if (mask == null) return null;

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

    /// <summary>
    /// Builds a binary mask by fitting a MinAreaRect to the largest contour
    /// and merging nearby smaller contours that are within the largest contour.
    /// </summary>
    private static Mat? FillLargestContour(Point[][] contours, int imgW, int imgH)
    {
        double imgArea = (double)imgW * imgH;

        var ranked = contours
            .Select(c => (Contour: c, Area: Cv2.ContourArea(c)))
            .Where(c =>
            {
                double ratio = c.Area / imgArea;
                return ratio >= MinMaskCoverage && ratio <= MaxMaskCoverage;
            })
            .OrderByDescending(c => c.Area)
            .ToList();

        if (ranked.Count == 0) return null;

        var largest = ranked[0];
        var minRect = Cv2.MinAreaRect(largest.Contour);

        // expand by 2% to avoid clipping document edges
        minRect.Size = new Size2f(
            minRect.Size.Width * 1.02f,
            minRect.Size.Height * 1.02f);

        var boxPts = Cv2.BoxPoints(minRect)
            .Select(p => new Point(
                Math.Clamp((int)p.X, 0, imgW - 1),
                Math.Clamp((int)p.Y, 0, imgH - 1)))
            .ToArray();

        var mask = new Mat(imgH, imgW, MatType.CV_8UC1, Scalar.All(0));
        Cv2.FillConvexPoly(mask, boxPts, Scalar.White);

        var mainBounds = Cv2.BoundingRect(boxPts);
        var expanded = ExpandRect(mainBounds, imgW, imgH, 0.03);

        foreach (var (contour, area) in ranked.Skip(1))
        {
            double ratio = area / imgArea;
            if (ratio < 0.01) continue;

            var cRect = Cv2.BoundingRect(contour);
            if (RectsOverlap(expanded, cRect))
            {
                var subRect = Cv2.MinAreaRect(contour);
                subRect.Size = new Size2f(
                    subRect.Size.Width * 1.02f,
                    subRect.Size.Height * 1.02f);
                var subPts = Cv2.BoxPoints(subRect)
                    .Select(p => new Point(
                        Math.Clamp((int)p.X, 0, imgW - 1),
                        Math.Clamp((int)p.Y, 0, imgH - 1)))
                    .ToArray();
                Cv2.FillConvexPoly(mask, subPts, Scalar.White);
            }
        }

        return mask;
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
    /// Cleans the mask: fill holes, morphological close, keep largest component.
    /// </summary>
    private static Mat CleanupMask(Mat rawMask, bool fillHoles, string? debugPath)
    {
        var mask = rawMask.Clone();

        if (fillHoles)
        {
            using var inverted = new Mat();
            Cv2.BitwiseNot(mask, inverted);
            Cv2.FindContours(inverted, out var holeContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            int h = mask.Rows, w = mask.Cols;
            foreach (var contour in holeContours)
            {
                bool touchesBorder = contour.Any(p =>
                    p.X <= 1 || p.Y <= 1 || p.X >= w - 2 || p.Y >= h - 2);
                if (!touchesBorder)
                    Cv2.DrawContours(mask, new[] { contour }, 0, Scalar.White, -1);
            }
        }

        using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, k, iterations: 1);

        KeepLargestComponent(mask);

        if (debugPath != null)
            SaveDebugImage(mask, debugPath, "50_cleaned_mask");

        return mask;
    }

    /// <summary>
    /// Keeps only the largest connected foreground component.
    /// </summary>
    private static void KeepLargestComponent(Mat mask)
    {
        var labels = new Mat();
        var stats = new Mat();
        var centroids = new Mat();
        int numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids);

        if (numLabels <= 2) return; // 0=background, 1=largest component

        int largestLabel = 1;
        int largestArea = 0;
        for (int i = 1; i < numLabels; i++)
        {
            int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
            if (area > largestArea)
            {
                largestArea = area;
                largestLabel = i;
            }
        }

        mask.SetTo(Scalar.All(0));
        for (int r = 0; r < labels.Rows; r++)
        {
            for (int c = 0; c < labels.Cols; c++)
            {
                if (labels.At<int>(r, c) == largestLabel)
                    mask.Set(r, c, (byte)255);
            }
        }

        labels.Dispose();
        stats.Dispose();
        centroids.Dispose();
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
    /// Applies the mask to produce a white-background output.
    /// Uses alpha blending when the mask is feathered.
    /// </summary>
    private static Mat ApplyMaskOnWhite(Mat src, Mat mask, bool isFeathered)
    {
        if (!isFeathered)
        {
            var output = new Mat(src.Size(), src.Type(), Scalar.White);
            src.CopyTo(output, mask);
            return output;
        }

        // alpha-blend for feathered masks
        using var srcFloat = new Mat();
        src.ConvertTo(srcFloat, MatType.CV_32FC3, 1.0 / 255.0);

        using var alpha1c = new Mat();
        mask.ConvertTo(alpha1c, MatType.CV_32FC1, 1.0 / 255.0);

        using var alpha3c = new Mat();
        Cv2.Merge(new[] { alpha1c, alpha1c, alpha1c }, alpha3c);

        using var invAlpha = new Mat();
        using var ones3c = new Mat(alpha3c.Size(), alpha3c.Type(), new Scalar(1.0, 1.0, 1.0));
        Cv2.Subtract(ones3c, alpha3c, invAlpha);

        using var whiteFloat = new Mat(src.Size(), MatType.CV_32FC3, new Scalar(1.0, 1.0, 1.0));

        using var fg = new Mat();
        Cv2.Multiply(srcFloat, alpha3c, fg);

        using var bg = new Mat();
        Cv2.Multiply(whiteFloat, invAlpha, bg);

        using var blended = new Mat();
        Cv2.Add(fg, bg, blended);

        var result = new Mat();
        blended.ConvertTo(result, MatType.CV_8UC3, 255.0);
        return result;
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

    /// <summary>
    /// Debug visualization with the mask overlaid on the source image.
    /// </summary>
    private static void SaveDebugMaskOverlay(Mat src, Mat mask, string debugPath, string name)
    {
        using var vis = src.Clone();

        using var scaledMask = mask.Width != src.Width || mask.Height != src.Height
            ? new Mat()
            : mask.Clone();

        if (mask.Width != src.Width || mask.Height != src.Height)
            Cv2.Resize(mask, scaledMask, src.Size(), 0, 0, InterpolationFlags.Nearest);

        // green overlay on masked region
        using var overlay = new Mat(src.Size(), src.Type(), new Scalar(0, 180, 0));
        overlay.CopyTo(vis, scaledMask);
        Cv2.AddWeighted(src, 0.6, vis, 0.4, 0, vis);

        double coverage = Cv2.CountNonZero(scaledMask) / (double)(src.Width * src.Height);
        Cv2.PutText(vis, $"Coverage: {coverage:P0}", new Point(10, 30),
            HersheyFonts.HersheySimplex, 0.8, new Scalar(0, 255, 255), 2);

        var filePath = Path.Combine(debugPath, $"{name}.png");
        Cv2.ImWrite(filePath, vis);
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.RemoveDocumentBackground);
    }
}
