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

        var det = TryDetectByEdges(src, sensitivity, enhanceEdges);

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

    // Detection and Helpers

    private (Point2f[]? Corners, double Confidence, string Method) TryDetectByEdges(Mat src, string sensitivity, bool enhanceEdges)
    {
        using var resized = ResizeForDetection(src, 900, out var scale);
        Mat prep;
        using (var gray = new Mat())
        {
            Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);
            if (enhanceEdges)
            {
                using var filtered = new Mat();
                Cv2.BilateralFilter(gray, filtered, 9, 75, 75);
                using var clahe = Cv2.CreateCLAHE(3.0, new Size(8, 8));
                var tmp = new Mat();
                clahe.Apply(filtered, tmp);
                prep = tmp;
            }
            else
            {
                prep = gray.Clone();
            }
        }

        using (prep)
        using (var blurred = new Mat())
        using (var edges = new Mat())
        {
            Cv2.GaussianBlur(prep, blurred, new Size(5, 5), 0);

            (double low, double high) = sensitivity switch
            {
                "aggressive" => (25, 90),
                "conservative" => (80, 200),
                _ => (50, 150)
            };

            Cv2.Canny(blurred, edges, low, high);

            var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
            Cv2.Dilate(edges, edges, kernel, iterations: 2);
            Cv2.MorphologyEx(edges, edges, MorphTypes.Close, kernel, iterations: 2);

            Cv2.FindContours(edges, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            if (contours.Length == 0)
                return (null, 0.0, "edges_none");

            var (quad, score) = GetBestQuad(contours, resized.Size());

            if (quad == null)
                return (null, 0.0, "edges_no_quad");

            var scaledQuad = quad.Select(p => new Point2f((float)(p.X / scale), (float)(p.Y / scale))).ToArray();

            if (!IsQuadReasonable(scaledQuad, src.Width, src.Height))
                return (null, 0.0, "edges_bad_quad");

            double conf = Math.Min(0.95, 0.40 + score * 0.7);
            return (scaledQuad, conf, "edges");
        }
    }

    private static (Point2f[]? Quad, double Score) GetBestQuad(Point[][] contours, Size imageSize)
    {
        double imgArea = imageSize.Width * imageSize.Height;
        double bestScore = 0;
        Point2f[]? bestQuad = null;

        foreach (var c in contours
                     .OrderByDescending(c => Cv2.ContourArea(c))
                     .Take(MaxSearchContours))
        {
            double area = Cv2.ContourArea(c);
            double areaRatio = area / imgArea;
            if (areaRatio < MinAreaRatio || areaRatio > MaxAreaRatio)
                continue;

            if (TouchesBorder(c, imageSize.Width, imageSize.Height, Math.Max(6, (int)(Math.Min(imageSize.Width, imageSize.Height) * 0.01))))
                continue;

            Point2f[]? quad = TryApproxToQuad(c) ?? BoxPointsQuad(c);
            if (quad == null || quad.Length != 4)
                continue;

            double rectScore = QuadRectangularity(quad);
            double borderScore = QuadCornerBoundaryScore(quad, imageSize.Width, imageSize.Height);
            double score = 0.6 * areaRatio + 0.3 * rectScore + 0.1 * borderScore;

            if (score > bestScore)
            {
                bestScore = score;
                bestQuad = quad;
            }
        }
        return (bestQuad, bestScore);
    }

    private static Point2f[]? TryApproxToQuad(Point[] contour)
    {
        var hull = Cv2.ConvexHull(contour);
        double peri = Cv2.ArcLength(hull, true);
        foreach (var eps in new[] { 0.05, 0.03, 0.02, 0.015, 0.01 })
        {
            var approx = Cv2.ApproxPolyDP(hull, eps * peri, true);
            if (approx.Length == 4)
                return approx.Select(pt => new Point2f(pt.X, pt.Y)).ToArray();
        }
        return null;
    }

    private static Point2f[] BoxPointsQuad(Point[] contour)
    {
        var rect = Cv2.MinAreaRect(contour);
        return Cv2.BoxPoints(rect).Select(p => new Point2f(p.X, p.Y)).ToArray();
    }

    private static bool TouchesBorder(Point[] pts, int w, int h, int m)
        => pts.Any(p => p.X <= m || p.Y <= m || p.X >= (w - 1 - m) || p.Y >= (h - 1 - m));

    private static double QuadCornerBoundaryScore(Point2f[] quad, int w, int h)
    {
        int m = Math.Max(10, (int)(Math.Min(w, h) * 0.02));
        return quad.Count(p => p.X > m && p.Y > m && p.X < w - m && p.Y < h - m) / 4.0;
    }

    private static Point2f[] OrderCorners(Point2f[] pts)
    {
        // Find TL, TR, BR, BL by sums/diffs. This is unchanged logic.
        var sum = pts.Select(p => p.X + p.Y).ToArray();
        var diff = pts.Select(p => p.X - p.Y).ToArray();
        var tl = pts[Array.IndexOf(sum, sum.Min())];
        var br = pts[Array.IndexOf(sum, sum.Max())];
        var tr = pts[Array.IndexOf(diff, diff.Max())];
        var bl = pts[Array.IndexOf(diff, diff.Min())];
        return new[] { tl, tr, br, bl };
    }

    private static double QuadRectangularity(Point2f[] quad)
    {
        var q = OrderCorners(quad);
        double top = Distance(q[0], q[1]), right = Distance(q[1], q[2]);
        double bot = Distance(q[2], q[3]), left = Distance(q[3], q[0]);
        double hr = Math.Min(top, bot) / Math.Max(top, bot);
        double vr = Math.Min(left, right) / Math.Max(left, right);
        return (hr + vr) / 2.0;
    }

    private static double Distance(Point2f a, Point2f b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static bool IsQuadReasonable(Point2f[] quad, int imgW, int imgH)
    {
        var rect = Cv2.BoundingRect(quad.Select(p => new Point((int)p.X, (int)p.Y)).ToArray());
        if (rect.Width < imgW * 0.12 || rect.Height < imgH * 0.12) return false;
        if (rect.Width > imgW * 0.997 && rect.Height > imgH * 0.997) return false;
        var cont = quad.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
        return Cv2.IsContourConvex(cont);
    }

    private static Mat ResizeForDetection(Mat src, int maxSide, out double scale)
    {
        int w = src.Width, h = src.Height, longest = Math.Max(w, h);
        if (longest <= maxSide)
        {
            scale = 1.0;
            return src.Clone();
        }
        scale = (double)maxSide / longest;
        int nw = (int)(w * scale), nh = (int)(h * scale);
        var dst = new Mat();
        Cv2.Resize(src, dst, new Size(nw, nh), 0, 0, InterpolationFlags.Area);
        return dst;
    }

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
        float cx = quad.Average(p => p.X), cy = quad.Average(p => p.Y);
        double factor = 1.0 + (marginPercent / 100.0);
        return quad.Select(p =>
        {
            float nx = (float)(cx + (p.X - cx) * factor);
            float ny = (float)(cy + (p.Y - cy) * factor);
            nx = Math.Clamp(nx, 0, imgW - 1);
            ny = Math.Clamp(ny, 0, imgH - 1);
            return new Point2f(nx, ny);
        }).ToArray();
    }

    private static (int width, int height) CalculateOutputDimensions(Point2f[] corners, int targetWidth, int targetHeight)
    {
        corners = OrderCorners(corners);
        double topW = Distance(corners[0], corners[1]), botW = Distance(corners[2], corners[3]);
        double w = Math.Max(topW, botW);
        double leftH = Distance(corners[0], corners[3]), rightH = Distance(corners[1], corners[2]);
        double h = Math.Max(leftH, rightH);

        if (targetWidth > 0 && targetHeight > 0)
            return (targetWidth, targetHeight);
        if (targetWidth > 0)
            return (targetWidth, Math.Max(1, (int)(h * (targetWidth / w))));
        if (targetHeight > 0)
            return (Math.Max(1, (int)(w * (targetHeight / h))), targetHeight);

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
