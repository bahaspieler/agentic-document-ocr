using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;
using System.Globalization;

namespace OcrAgent.Plugins.Geometric;

/// <summary>
/// Auto perspective correction for documents/receipts.
/// </summary>
public sealed class PerspectivePlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    private const int MaxSearchContours = 15;
    private const double MinAreaRatio = 0.10;
    private const double MaxAreaRatio = 0.995;

    public PerspectivePlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Auto-detects document boundaries and corrects perspective distortion. Returns corrected image reference.")]
    public Task<string> CorrectPerspective(
        [Description("The reference number of the image to correct.")] string imageReference,
        [Description("Detection sensitivity: 'auto' (recommended), 'aggressive', 'conservative'.")] string sensitivity = "auto",
        [Description("Target output width in pixels (0 = auto).")] int targetWidth = 0,
        [Description("Target output height in pixels (0 = auto).")] int targetHeight = 0,
        [Description("Add margin padding around detected document.")] int marginPercent = 0,
        [Description("Enhance edges before detection.")] bool enhanceEdges = true)
    {
        sensitivity = sensitivity.ToLowerInvariant();
        if (sensitivity is not ("auto" or "aggressive" or "conservative"))
            throw new ArgumentException("sensitivity must be 'auto', 'aggressive', or 'conservative'");

        if (marginPercent < 0 || marginPercent > 20)
            throw new ArgumentException("marginPercent must be between 0 and 20");

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        var det = DetectCornersMultiStrategy(src, sensitivity, enhanceEdges);

        Point2f[] corners;
        string detectionMethod;
        double confidence;

        if (det.Corners is { Length: 4 })
        {
            corners = OrderCorners(det.Corners);
            detectionMethod = det.Method;
            confidence = det.Confidence;
        }
        else
        {
            int autoMargin = marginPercent > 0 ? marginPercent : 2;
            corners = GetImageBoundaries(src, autoMargin);
            detectionMethod = "fallback_image_bounds";
            confidence = 0.30;
        }

        if (marginPercent > 0 && detectionMethod != "fallback_image_bounds")
            corners = ExpandQuad(corners, src.Width, src.Height, marginPercent);

        var (outW, outH) = CalculateOutputDimensions(corners, targetWidth, targetHeight);

        using var corrected = ApplyPerspectiveTransform(src, corners, outW, outH);
        Cv2.ImEncode(".png", corrected, out var pngBytes);

        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "perspective_correct",
            ["detectedCorners"] = FormatCorners(corners),
            ["outputDimensions"] = $"{outW}x{outH}",
            ["confidence"] = confidence.ToString("F2"),
            ["detectionMethod"] = detectionMethod,
            ["marginPercent"] = marginPercent.ToString(),
            ["enhanceEdges"] = enhanceEdges.ToString()
        };

        var baseRef = $"{imageReference}_perspective_{SanitizeForRef(detectionMethod)}";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);

        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    // Detection method - multiple strategies (edge + otsu + hsv)
    private (Point2f[]? Corners, double Confidence, string Method) DetectCornersMultiStrategy(
        Mat src,
        string sensitivity,
        bool enhanceEdges)
    {
        var candidates = new List<(Point2f[]? Corners, double Confidence, string Method)>();

        // edge-based
        var edge = TryDetectByEdges(src, sensitivity, enhanceEdges);
        if (edge.Corners is { Length: 4 })
            candidates.Add((edge.Corners, edge.Confidence, edge.Method));

        // otsu paper segmentation
        var otsu = TryDetectByOtsuSegmentation(src, sensitivity);
        if (otsu.Corners is { Length: 4 })
            candidates.Add((otsu.Corners, otsu.Confidence, otsu.Method));

        // hsv region segmentation
        var hsv = TryDetectByHsvLightRegionSegmentation(src, sensitivity);
        if (hsv.Corners is { Length: 4 })
            candidates.Add((hsv.Corners, hsv.Confidence, hsv.Method));

        if (candidates.Count == 0)
            return (null, 0.0, "none");

        // pick the best by confidence
        var best = candidates.OrderByDescending(c => c.Confidence).First();
        return (best.Corners, best.Confidence, best.Method);
    }

    private (Point2f[]? Corners, double Confidence, string Method) TryDetectByEdges(
        Mat src,
        string sensitivity,
        bool enhanceEdges)
    {
        using var resized = ResizeForDetection(src, 900, out var scale);
        using var gray = new Mat();
        Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);

        using var prep = new Mat();
        if (enhanceEdges)
        {
            using var filtered = new Mat();
            Cv2.BilateralFilter(gray, filtered, 9, 75, 75);
            using var clahe = Cv2.CreateCLAHE(3.0, new Size(8, 8));
            clahe.Apply(filtered, prep);
        }
        else
        {
            gray.CopyTo(prep);
        }

        using var blurred = new Mat();
        Cv2.GaussianBlur(prep, blurred, new Size(5, 5), 0);

        (double low, double high) = sensitivity switch
        {
            "aggressive" => (25, 90),
            "conservative" => (80, 200),
            _ => (50, 150)
        };

        using var edges = new Mat();
        Cv2.Canny(blurred, edges, low, high);

        using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
        using var morph = new Mat();
        Cv2.Dilate(edges, morph, k, iterations: 2);
        Cv2.MorphologyEx(morph, morph, MorphTypes.Close, k, iterations: 2);

        Cv2.FindContours(morph, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0) return (null, 0.0, "edges_none");

        var best = SelectBestQuadFromContours(
            contours,
            resized.Width,
            resized.Height,
            MinAreaRatio,
            MaxAreaRatio);

        if (best.Quad is null) return (null, 0.0, "edges_no_quad");

        // scale back to original
        var quad = ScaleQuad(best.Quad, 1.0 / scale);
        if (!IsReasonableQuad(quad, src.Width, src.Height)) return (null, 0.0, "edges_bad_quad");

        // Confidence
        double conf = Math.Min(0.95, 0.40 + best.Score * 0.70);
        return (quad, conf, "edges");
    }

    /// <summary>
    /// Paper segmentation via Otsu (grayscale).
    /// This fixes RECEIPT002 (sample-image) correction.
    /// </summary>
    private (Point2f[]? Corners, double Confidence, string Method) TryDetectByOtsuSegmentation(Mat src, string sensitivity)
    {
        using var resized = ResizeForDetection(src, 900, out var scale);

        using var gray = new Mat();
        Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);

        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

        // Otsu
        using var bin = new Mat();
        Cv2.Threshold(blurred, bin, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        // cleanup
        int kSize = sensitivity == "aggressive" ? 17 : 21;
        using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(kSize, kSize));
        Cv2.MorphologyEx(bin, bin, MorphTypes.Close, k, iterations: 2);
        Cv2.MorphologyEx(bin, bin, MorphTypes.Open, k, iterations: 1);

        // contours
        Cv2.FindContours(bin, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0) return (null, 0.0, "otsu_none");

        var best = SelectBestQuadFromContours(
            contours,
            resized.Width,
            resized.Height,
            MinAreaRatio,
            MaxAreaRatio);

        if (best.Quad is null) return (null, 0.0, "otsu_no_quad");

        var quad = ScaleQuad(best.Quad, 1.0 / scale);
        if (!IsReasonableQuad(quad, src.Width, src.Height)) return (null, 0.0, "otsu_bad_quad");

        double conf = Math.Min(0.94, 0.50 + best.Score * 0.65);
        return (quad, conf, "otsu_segmentation");
    }

    /// <summary>
    /// Light region segmentation via HSV masking.
    /// </summary>
    private (Point2f[]? Corners, double Confidence, string Method) TryDetectByHsvLightRegionSegmentation(Mat src, string sensitivity)
    {
        using var resized = ResizeForDetection(src, 900, out var scale);

        using var hsv = new Mat();
        Cv2.CvtColor(resized, hsv, ColorConversionCodes.BGR2HSV);

        int sMax = sensitivity == "aggressive" ? 120 : 85;
        int vMin = sensitivity == "conservative" ? 185 : 165;

        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, vMin), new Scalar(180, sMax, 255), mask);

        using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(19, 19));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, k, iterations: 2);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, k, iterations: 1);

        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0) return (null, 0.0, "hsv_none");

        var best = SelectBestQuadFromContours(
            contours,
            resized.Width,
            resized.Height,
            MinAreaRatio,
            MaxAreaRatio);

        if (best.Quad is null) return (null, 0.0, "hsv_no_quad");

        var quad = ScaleQuad(best.Quad, 1.0 / scale);
        if (!IsReasonableQuad(quad, src.Width, src.Height)) return (null, 0.0, "hsv_bad_quad");

        double conf = Math.Min(0.88, 0.35 + best.Score * 0.75);
        return (quad, conf, "hsv_light_segmentation");
    }

    // Contour Scoring and Geometry Helper Methods

    private static (Point2f[]? Quad, double Score) SelectBestQuadFromContours(
        Point[][] contours,
        int imgW,
        int imgH,
        double minAreaRatio,
        double maxAreaRatio)
    {
        double imgArea = (double)imgW * imgH;
        double bestScore = 0.0;
        Point2f[]? bestQuad = null;

        foreach (var c in contours
                     .OrderByDescending(c => Cv2.ContourArea(c))
                     .Take(MaxSearchContours))
        {
            double area = Cv2.ContourArea(c);
            double areaRatio = area / imgArea;
            if (areaRatio < minAreaRatio || areaRatio > maxAreaRatio)
                continue;

            if (TouchesBorder(c, imgW, imgH, marginPx: Math.Max(6, (int)(Math.Min(imgW, imgH) * 0.01))))
                continue;

            var quad = ApproximateToQuad(c);
            if (quad is null)
            {
                var rr = Cv2.MinAreaRect(c);
                quad = Cv2.BoxPoints(rr).Select(p => new Point2f(p.X, p.Y)).ToArray();
            }

            if (quad.Length != 4)
                continue;

            double rect = RectangularityScore(quad);
            double cornerPenalty = CornerBorderPenalty(quad, imgW, imgH);
            double score = (areaRatio * 0.60) + (rect * 0.30) + (cornerPenalty * 0.10);

            if (score > bestScore)
            {
                bestScore = score;
                bestQuad = quad;
            }
        }

        return (bestQuad, bestScore);
    }

    private static Point2f[]? ApproximateToQuad(Point[] contour)
    {
        var hull = Cv2.ConvexHull(contour);
        double peri = Cv2.ArcLength(hull, true);

        double[] epsList = { 0.05, 0.04, 0.03, 0.025, 0.02, 0.018, 0.015, 0.012, 0.01 };

        foreach (var e in epsList)
        {
            var approx = Cv2.ApproxPolyDP(hull, e * peri, true);
            if (approx.Length == 4)
                return approx.Select(p => new Point2f(p.X, p.Y)).ToArray();
        }

        return null;
    }

    private static bool TouchesBorder(Point[] contour, int w, int h, int marginPx)
    {
        foreach (var p in contour)
        {
            if (p.X <= marginPx || p.Y <= marginPx || p.X >= (w - 1 - marginPx) || p.Y >= (h - 1 - marginPx))
                return true;
        }
        return false;
    }

    private static double CornerBorderPenalty(Point2f[] quad, int w, int h)
    {
        int margin = Math.Max(10, (int)(Math.Min(w, h) * 0.02));
        int good = 0;

        foreach (var p in quad)
        {
            if (p.X > margin && p.Y > margin && p.X < (w - 1 - margin) && p.Y < (h - 1 - margin))
                good++;
        }

        return good / 4.0;
    }

    private static Mat ResizeForDetection(Mat src, int maxSide, out double scale)
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

    private static Point2f[] OrderCorners(Point2f[] pts)
    {
        var sum = pts.Select(p => p.X + p.Y).ToArray();
        var diff = pts.Select(p => p.X - p.Y).ToArray();

        var tl = pts[Array.IndexOf(sum, sum.Min())];
        var br = pts[Array.IndexOf(sum, sum.Max())];
        var tr = pts[Array.IndexOf(diff, diff.Max())];
        var bl = pts[Array.IndexOf(diff, diff.Min())];

        return new[] { tl, tr, br, bl };
    }

    private static Point2f[] ScaleQuad(Point2f[] quad, double factor)
        => quad.Select(p => new Point2f((float)(p.X * factor), (float)(p.Y * factor))).ToArray();

    private static bool IsReasonableQuad(Point2f[] quad, int imgW, int imgH)
    {
        var rect = Cv2.BoundingRect(quad.Select(p => new Point((int)p.X, (int)p.Y)).ToArray());

        if (rect.Width < imgW * 0.12 || rect.Height < imgH * 0.12)
            return false;

        if (rect.Width > imgW * 0.997 && rect.Height > imgH * 0.997)
            return false;

        var contour = quad.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
        return Cv2.IsContourConvex(contour);
    }

    private static double RectangularityScore(Point2f[] quad)
    {
        quad = OrderCorners(quad);
        double top = Dist(quad[0], quad[1]);
        double right = Dist(quad[1], quad[2]);
        double bottom = Dist(quad[2], quad[3]);
        double left = Dist(quad[3], quad[0]);

        double hr = Math.Min(top, bottom) / Math.Max(top, bottom);
        double vr = Math.Min(left, right) / Math.Max(left, right);
        return (hr + vr) / 2.0;
    }

    private static double Dist(Point2f a, Point2f b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static Point2f[] GetImageBoundaries(Mat src, int marginPercent)
    {
        int mx = (int)(src.Width * marginPercent / 100.0);
        int my = (int)(src.Height * marginPercent / 100.0);

        return new[]
        {
            new Point2f(mx, my),
            new Point2f(src.Width - mx, my),
            new Point2f(src.Width - mx, src.Height - my),
            new Point2f(mx, src.Height - my)
        };
    }

    private static Point2f[] ExpandQuad(Point2f[] quad, int imgW, int imgH, int marginPercent)
    {
        float cx = quad.Average(p => p.X);
        float cy = quad.Average(p => p.Y);
        double factor = 1.0 + (marginPercent / 100.0);

        Point2f Expand(Point2f p)
        {
            float nx = (float)(cx + (p.X - cx) * factor);
            float ny = (float)(cy + (p.Y - cy) * factor);
            nx = Math.Clamp(nx, 0, imgW - 1);
            ny = Math.Clamp(ny, 0, imgH - 1);
            return new Point2f(nx, ny);
        }

        return quad.Select(Expand).ToArray();
    }

    private static (int width, int height) CalculateOutputDimensions(Point2f[] corners, int targetWidth, int targetHeight)
    {
        corners = OrderCorners(corners);

        double topWidth = Dist(corners[0], corners[1]);
        double bottomWidth = Dist(corners[2], corners[3]);
        double w = Math.Max(topWidth, bottomWidth);

        double leftHeight = Dist(corners[0], corners[3]);
        double rightHeight = Dist(corners[1], corners[2]);
        double h = Math.Max(leftHeight, rightHeight);

        if (targetWidth > 0 && targetHeight > 0)
            return (targetWidth, targetHeight);

        if (targetWidth > 0)
        {
            double s = targetWidth / w;
            return (targetWidth, Math.Max(1, (int)(h * s)));
        }

        if (targetHeight > 0)
        {
            double s = targetHeight / h;
            return (Math.Max(1, (int)(w * s)), targetHeight);
        }

        return (Math.Max(1, (int)w), Math.Max(1, (int)h));
    }

    private static Mat ApplyPerspectiveTransform(Mat src, Point2f[] srcCorners, int outputWidth, int outputHeight)
    {
        srcCorners = OrderCorners(srcCorners);

        var dstCorners = new[]
        {
            new Point2f(0, 0),
            new Point2f(outputWidth - 1, 0),
            new Point2f(outputWidth - 1, outputHeight - 1),
            new Point2f(0, outputHeight - 1)
        };

        using var matrix = Cv2.GetPerspectiveTransform(srcCorners, dstCorners);

        var dst = new Mat();
        Cv2.WarpPerspective(
            src,
            dst,
            matrix,
            new Size(outputWidth, outputHeight),
            InterpolationFlags.Linear,
            BorderTypes.Constant,
            Scalar.White);

        return dst;
    }

    private static string FormatCorners(Point2f[] c)
    {
        c = OrderCorners(c);
        return $"[({c[0].X:F0},{c[0].Y:F0}), ({c[1].X:F0},{c[1].Y:F0}), ({c[2].X:F0},{c[2].Y:F0}), ({c[3].X:F0},{c[3].Y:F0})]";
    }

    private static string SanitizeForRef(string s)
    {
        var safe = new string(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
        while (safe.Contains("__")) safe = safe.Replace("__", "_");
        return safe.Trim('_');
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.CorrectPerspective);
    }
}
