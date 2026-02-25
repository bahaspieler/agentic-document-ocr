using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;
using System.Globalization;

namespace OcrAgent.Plugins.Segmentation;

/// <summary>
/// Removes non-document background from images using multi-strategy mask generation.
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

        var (winnerMask, winnerScore, winnerMethod) = GenerateMasksMultiStrategy(src, sensitivity, debugPath);

        if (winnerMask == null)
            throw new InvalidOperationException("No valid document mask could be generated from the image");

        using var cleanedMask = CleanupMask(winnerMask, fillHoles, debugPath);

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

        winnerMask.Dispose();

        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "remove_background",
            ["mode"] = mode,
            ["detectionMethod"] = winnerMethod,
            ["maskCoverage"] = coverage.ToString("F3", CultureInfo.InvariantCulture),
            ["confidence"] = winnerScore.ToString("F2", CultureInfo.InvariantCulture),
            ["feather"] = feather.ToString(),
            ["fillHoles"] = fillHoles.ToString()
        };

        var baseRef = $"{imageReference}_bgremoval_{mode}";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);
        var newRef = _store.SaveBytes(outputBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
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
    /// Analyzes image contrast to determine if brightness-based methods might work.
    /// Low ratio = light background (stone, wood) where Otsu/HSV might fail.
    /// High ratio = dark background (dark blue, black) where Otsu/HSV might work.
    /// </summary>
    private static double AnalyzeBackgroundContrast(Mat src)
    {
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        Cv2.MeanStdDev(gray, out var mean, out var stddev);
        double grayMean = mean[0];
        double grayStd = stddev[0];

        return grayStd / Math.Max(grayMean, 1.0);
    }
    
    /// <summary>
    /// Generates a document mask using multiple strategies.
    /// </summary>
    private (Mat? Mask, double Score, string Method) GenerateMasksMultiStrategy(
        Mat src, string sensitivity, string? debugPath)
    {
        double contrastRatio = AnalyzeBackgroundContrast(src);
        var candidates = new List<(Mat Mask, double Score, string Method)>();

        if (debugPath != null)
        {
            using var normVis = NormalizeForSegmentation(src);
            SaveDebugImage(normVis, debugPath, "05_normalized");
        }

        var otsuMask = GenerateOtsuPaperMask(src, sensitivity, debugPath);
        if (otsuMask != null)
        {
            double score = CalculateMaskQuality(otsuMask, src.Width, src.Height, contrastRatio, "otsu");
            candidates.Add((otsuMask, score, "otsu"));
            if (debugPath != null)
                SaveDebugMaskOverlay(src, otsuMask, debugPath, $"10_otsu_mask_q{score:F2}");
        }

        var hsvMask = GenerateHsvLightRegionMask(src, sensitivity, debugPath);
        if (hsvMask != null)
        {
            double score = CalculateMaskQuality(hsvMask, src.Width, src.Height, contrastRatio, "hsv");
            candidates.Add((hsvMask, score, "hsv"));
            if (debugPath != null)
                SaveDebugMaskOverlay(src, hsvMask, debugPath, $"20_hsv_mask_q{score:F2}");
        }

        var edgeMask = GenerateEdgeBasedMask(src, sensitivity, debugPath);
        if (edgeMask != null)
        {
            double score = CalculateMaskQuality(edgeMask, src.Width, src.Height, contrastRatio, "edges");
            candidates.Add((edgeMask, score, "edges"));
            if (debugPath != null)
                SaveDebugMaskOverlay(src, edgeMask, debugPath, $"25_edges_mask_q{score:F2}");
        }

        // seed GrabCut with the best mask from brightness/edge strategies
        Mat? priorMask = null;
        if (candidates.Count > 0)
        {
            var bestPrior = candidates.OrderByDescending(c => c.Score).First();
            if (bestPrior.Score > 0.3)
                priorMask = bestPrior.Mask;
        }

        var grabcutMask = GenerateGrabCutMask(src, sensitivity, debugPath, priorMask);
        if (grabcutMask != null)
        {
            double score = CalculateMaskQuality(grabcutMask, src.Width, src.Height, contrastRatio, "grabcut");
            candidates.Add((grabcutMask, score, "grabcut"));
            if (debugPath != null)
                SaveDebugMaskOverlay(src, grabcutMask, debugPath, $"30_grabcut_mask_q{score:F2}");
        }

        if (candidates.Count == 0)
            return (null, 0.0, "none");

        // single candidate
        if (candidates.Count == 1)
            return (candidates[0].Mask, candidates[0].Score, candidates[0].Method);

        // weighted pixel-voting combination
        using var voteMap = new Mat(src.Height, src.Width, MatType.CV_32FC1, Scalar.All(0));
        double totalWeight = 0;
        foreach (var (mask, score, _) in candidates)
        {
            using var sized = (mask.Width == src.Width && mask.Height == src.Height)
                ? mask.Clone()
                : new Mat();
            if (mask.Width != src.Width || mask.Height != src.Height)
                Cv2.Resize(mask, sized, src.Size(), 0, 0, InterpolationFlags.Nearest);

            using var floatMask = new Mat();
            sized.ConvertTo(floatMask, MatType.CV_32FC1, score / 255.0);
            Cv2.Add(voteMap, floatMask, voteMap);
            totalWeight += score;
        }

        double voteThreshold = totalWeight * 0.4;
        var combinedMask = new Mat();
        Cv2.Threshold(voteMap, combinedMask, voteThreshold, 255, ThresholdTypes.Binary);
        combinedMask.ConvertTo(combinedMask, MatType.CV_8UC1);

        string combinedMethod = string.Join("+",
            candidates.OrderByDescending(c => c.Score).Select(c => c.Method));
        double combinedScore = candidates.Max(c => c.Score);

        foreach (var c in candidates)
            c.Mask.Dispose();

        if (debugPath != null)
            SaveDebugMaskOverlay(src, combinedMask, debugPath, "40_combined_vote");

        return (combinedMask, combinedScore, combinedMethod);
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
    /// Generates a document mask using Otsu thresholding on grayscale.
    /// </summary>
    private Mat? GenerateOtsuPaperMask(Mat src, string sensitivity, string? debugPath)
    {
        using var resized = ResizeForProcessing(src, 900, out var scale);
        using var normalized = NormalizeForSegmentation(resized);

        using var blurred = new Mat();
        Cv2.GaussianBlur(normalized, blurred, new Size(5, 5), 0);

        using var bin = new Mat();
        Cv2.Threshold(blurred, bin, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        int kSize = sensitivity == "aggressive" ? 17 : 21;
        using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(kSize, kSize));
        Cv2.MorphologyEx(bin, bin, MorphTypes.Close, k, iterations: 2);
        Cv2.MorphologyEx(bin, bin, MorphTypes.Open, k, iterations: 1);

        Cv2.FindContours(bin, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0) return null;

        var mask = FillLargestContour(contours, resized.Width, resized.Height);
        if (mask == null) return null;

        return ScaleMaskToOriginal(mask, src.Width, src.Height);
    }

    /// <summary>
    /// Generates a document mask using HSV color space segmentation.
    /// </summary>
    private Mat? GenerateHsvLightRegionMask(Mat src, string sensitivity, string? debugPath)
    {
        using var resized = ResizeForProcessing(src, 900, out var scale);

        using var hsv = new Mat();
        Cv2.CvtColor(resized, hsv, ColorConversionCodes.BGR2HSV);

        int sMax = sensitivity == "aggressive" ? 120 : 85;
        int vMin = sensitivity == "conservative" ? 185 : 165;

        using var regionMask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, vMin), new Scalar(180, sMax, 255), regionMask);

        using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(19, 19));
        Cv2.MorphologyEx(regionMask, regionMask, MorphTypes.Close, k, iterations: 2);
        Cv2.MorphologyEx(regionMask, regionMask, MorphTypes.Open, k, iterations: 1);

        Cv2.FindContours(regionMask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0) return null;

        var mask = FillLargestContour(contours, resized.Width, resized.Height);
        if (mask == null) return null;

        return ScaleMaskToOriginal(mask, src.Width, src.Height);
    }
    
    /// <summary>
    /// Generates a document mask using GrabCut foreground segmentation.
    /// Can handle complex backgrounds.
    /// </summary>
    private Mat? GenerateGrabCutMask(Mat src, string sensitivity, string? debugPath, Mat? priorMask = null)
    {
        using var resized = ResizeForProcessing(src, 900, out var scale);

        using var gcMask = new Mat(resized.Rows, resized.Cols, MatType.CV_8UC1, Scalar.All(0));
        using var bgdModel = new Mat(1, 65, MatType.CV_64FC1, Scalar.All(0));
        using var fgdModel = new Mat(1, 65, MatType.CV_64FC1, Scalar.All(0));

        double m = sensitivity == "aggressive" ? 0.03 : 0.05;
        int x = (int)(resized.Cols * m);
        int y = (int)(resized.Rows * m);
        int w = (int)(resized.Cols * (1.0 - 2 * m));
        int h = (int)(resized.Rows * (1.0 - 2 * m));
        var rect = new Rect(x, y, w, h);

        if (priorMask != null)
        {
            using var scaledPrior = new Mat();
            Cv2.Resize(priorMask, scaledPrior, resized.Size(), 0, 0, InterpolationFlags.Nearest);

            gcMask.SetTo(new Scalar(2)); // GC_PR_BGD
            for (int r = 0; r < gcMask.Rows; r++)
                for (int c = 0; c < gcMask.Cols; c++)
                    if (scaledPrior.At<byte>(r, c) > 127)
                        gcMask.Set(r, c, (byte)3); // GC_PR_FGD

            Cv2.GrabCut(resized, gcMask, rect, bgdModel, fgdModel, 5, GrabCutModes.InitWithMask);
        }
        else
        {
            Cv2.GrabCut(resized, gcMask, rect, bgdModel, fgdModel, 5, GrabCutModes.InitWithRect);
        }

        using var fg = new Mat();
        using var sure = new Mat();
        using var prob = new Mat();
        const byte GC_FGD = 1;
        const byte GC_PR_FGD = 3;

        Cv2.InRange(gcMask, new Scalar(GC_FGD), new Scalar(GC_FGD), sure);
        Cv2.InRange(gcMask, new Scalar(GC_PR_FGD), new Scalar(GC_PR_FGD), prob);
        Cv2.BitwiseOr(sure, prob, fg);

        using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(13, 13));
        Cv2.MorphologyEx(fg, fg, MorphTypes.Close, k, iterations: 2);

        Cv2.FindContours(fg, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0) return null;

        var mask = FillLargestContour(contours, resized.Width, resized.Height);
        if (mask == null) return null;

        return ScaleMaskToOriginal(mask, src.Width, src.Height);
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
    /// Scores a binary mask by coverage, rectangularity, compactness, border contact,
    /// and contrast.
    /// </summary>
    private static double CalculateMaskQuality(Mat mask, int imgW, int imgH,
        double contrastRatio = 0.5, string method = "")
    {
        double imgArea = (double)imgW * imgH;
        double maskPixels = Cv2.CountNonZero(mask);
        double coverage = maskPixels / imgArea;

        if (coverage < MinMaskCoverage || coverage > MaxMaskCoverage)
            return 0.0;

        double score = 0.0;

        if (coverage >= 0.15 && coverage <= 0.85)
            score += 0.30;
        else if (coverage > 0.85)
            score += 0.15;
        else
            score += coverage * 1.5;

        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length > 0)
        {
            var largest = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();
            double contourArea = Cv2.ContourArea(largest);
            var minRect = Cv2.MinAreaRect(largest);
            double rectArea = minRect.Size.Width * minRect.Size.Height;
            double rectangularity = rectArea > 0 ? contourArea / rectArea : 0;
            score += rectangularity * 0.30;
        }

        int numComponents = contours.Length;
        if (numComponents == 1)
            score += 0.20;
        else if (numComponents <= 3)
            score += 0.10;

        int edgesTouched = 0;
        for (int col = 0; col < imgW; col++)
        {
            if (mask.At<byte>(0, col) > 0) { edgesTouched |= 1; break; }
        }
        for (int col = 0; col < imgW; col++)
        {
            if (mask.At<byte>(imgH - 1, col) > 0) { edgesTouched |= 2; break; }
        }
        for (int row = 0; row < imgH; row++)
        {
            if (mask.At<byte>(row, 0) > 0) { edgesTouched |= 4; break; }
        }
        for (int row = 0; row < imgH; row++)
        {
            if (mask.At<byte>(row, imgW - 1) > 0) { edgesTouched |= 8; break; }
        }
        int touchedCount = System.Numerics.BitOperations.PopCount((uint)edgesTouched);
        if (touchedCount == 4)
            score -= 0.15;

        // contrast-aware scoring: low contrast means brightness methods might be unreliable
        if (contrastRatio < 0.25 && method is "edges")
            score += 0.15;
        else if (contrastRatio > 0.4 && method is "otsu" or "hsv")
            score += 0.10;

        return Math.Max(0.0, Math.Min(1.0, score));
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
