using OpenCvSharp;

namespace OcrAgent.Inference;

/// <summary>
/// Converts a probability mask (0..1) into a binary document mask
/// and extracts a 4 corners quadrilateral.   
/// </summary>
public static class DocumentContourExtractor
{
    /// <summary>
    /// Extracts a document mask and quad from a float probability mask.
    /// </summary>
    /// <param name="probabilityMask32F">CV_32FC1 probability mask, same size as the source image.</param>
    /// <param name="documentMask8U">Output binary 8U document mask.</param>
    /// <param name="quadCorners">Four corners in order tl, tr, br, bl when true; otherwise empty.</param>
    /// <param name="maskCoverage">Fraction of image pixels set in the binary mask when true.</param>
    /// <param name="confidence">Heuristic score in 0..1 when true; otherwise 0.</param>
    /// <param name="minAreaPercent">Minimum acceptable mask area as a percent of the image.</param>
    /// <param name="maxAreaPercent">Maximum acceptable mask area as a percent of the image.</param>
    /// <returns>True when a quad passes area and shape checks.</returns>
    public static bool TryExtractDocument(
        Mat probabilityMask32F,
        out Mat documentMask8U,
        out Point2f[] quadCorners,
        out double maskCoverage,
        out double confidence,
        int minAreaPercent = 8,
        int maxAreaPercent = 98)
    {
        if (probabilityMask32F.Empty())
            throw new ArgumentException("probabilityMask32F is empty.");

        if (probabilityMask32F.Type() != MatType.CV_32FC1)
            throw new ArgumentException("probabilityMask32F must be CV_32FC1.");

        int w = probabilityMask32F.Cols;
        int h = probabilityMask32F.Rows;

        using var mask8u = new Mat();
        probabilityMask32F.ConvertTo(mask8u, MatType.CV_8UC1, 255.0);

        using var blurred = new Mat();
        Cv2.GaussianBlur(mask8u, blurred, new Size(5, 5), 0);

        documentMask8U = new Mat();
        Cv2.Threshold(blurred, documentMask8U, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        int k = Math.Clamp((int)Math.Round(Math.Min(w, h) * 0.02), 9, 41);
        if (k % 2 == 0) k++;
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(k, k));

        Cv2.MorphologyEx(documentMask8U, documentMask8U, MorphTypes.Close, kernel, iterations: 2);
        Cv2.MorphologyEx(documentMask8U, documentMask8U, MorphTypes.Open, kernel, iterations: 1);

        if (!KeepLargestComponentInPlace(documentMask8U))
        {
            quadCorners = Array.Empty<Point2f>();
            maskCoverage = 0.0;
            confidence = 0.0;
            return false;
        }

        maskCoverage = Cv2.CountNonZero(documentMask8U) / (w * (double)h);

        if (maskCoverage < (minAreaPercent / 100.0) || maskCoverage > (maxAreaPercent / 100.0))
        {
            quadCorners = Array.Empty<Point2f>();
            confidence = 0.0;
            return false;
        }

        Cv2.FindContours(documentMask8U, out var contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0)
        {
            quadCorners = Array.Empty<Point2f>();
            confidence = 0.0;
            return false;
        }

        var bestContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();
        var hull = Cv2.ConvexHull(bestContour);
        double peri = Cv2.ArcLength(hull, true);

        Point[]? approx4 = null;
        double[] epsList = { 0.05, 0.04, 0.03, 0.025, 0.02, 0.018, 0.015, 0.012, 0.01 };

        foreach (var eps in epsList)
        {
            var approx = Cv2.ApproxPolyDP(hull, eps * peri, true);
            if (approx.Length == 4 && Cv2.IsContourConvex(approx))
            {
                approx4 = approx;
                break;
            }
        }

        Point2f[] quad;
        if (approx4 is not null)
        {
            quad = approx4.Select(p => new Point2f(p.X, p.Y)).ToArray();
        }
        else
        {
            var rr = Cv2.MinAreaRect(bestContour);
            quad = Cv2.BoxPoints(rr).Select(p => new Point2f(p.X, p.Y)).ToArray();
        }

        quad = OrderCorners(quad);

        if (!IsReasonableQuad(quad, w, h))
        {
            quadCorners = Array.Empty<Point2f>();
            confidence = 0.0;
            return false;
        }

        quadCorners = quad;
        var rectScore = RectangularityScore(quad);
        confidence = Math.Clamp(
            0.35 + 0.55 * rectScore + 0.10 * Math.Min(1.0, maskCoverage / 0.60),
            0.0, 1.0);

        return true;
    }

    /// <summary>
    /// Converts a float probability mask to a binary 8U mask, not extracting a quad.
    /// </summary>
    /// <param name="probabilityMask32F">CV_32FC1 mask; must be non-empty.</param>
    /// <returns>Owned 8U binary mask, or null when input is invalid or filtering fails.</returns>
    public static Mat? ToBinaryMask(Mat probabilityMask32F)
    {
        if (probabilityMask32F.Empty() || probabilityMask32F.Type() != MatType.CV_32FC1)
            return null;

        int w = probabilityMask32F.Cols;
        int h = probabilityMask32F.Rows;

        using var mask8u = new Mat();
        probabilityMask32F.ConvertTo(mask8u, MatType.CV_8UC1, 255.0);

        using var blurred = new Mat();
        Cv2.GaussianBlur(mask8u, blurred, new Size(5, 5), 0);

        var result = new Mat();
        Cv2.Threshold(blurred, result, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        int k = Math.Clamp((int)Math.Round(Math.Min(w, h) * 0.02), 9, 41);
        if (k % 2 == 0) k++;
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(k, k));

        Cv2.MorphologyEx(result, result, MorphTypes.Close, kernel, iterations: 2);
        Cv2.MorphologyEx(result, result, MorphTypes.Open, kernel, iterations: 1);

        if (!KeepLargestComponentInPlace(result))
        {
            result.Dispose();
            return null;
        }

        double coverage = Cv2.CountNonZero(result) / (w * (double)h);
        if (coverage < 0.05 || coverage > 0.98)
        {
            result.Dispose();
            return null;
        }

        return result;
    }

    private static bool KeepLargestComponentInPlace(Mat mask8u)
    {
        Cv2.FindContours(mask8u, out var contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0)
            return false;

        int bestIdx = 0;
        double bestArea = 0;
        for (int i = 0; i < contours.Length; i++)
        {
            double a = Cv2.ContourArea(contours[i]);
            if (a > bestArea) { bestArea = a; bestIdx = i; }
        }

        using var cleaned = new Mat(mask8u.Rows, mask8u.Cols, MatType.CV_8UC1, Scalar.Black);
        Cv2.DrawContours(cleaned, contours, bestIdx, Scalar.White, thickness: -1);
        cleaned.CopyTo(mask8u);
        return true;
    }

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

    /// <summary>
    /// Euclidean distance between two points in image coordinates.
    /// </summary>
    /// <param name="a">First point.</param>
    /// <param name="b">Second point.</param>
    /// <returns>Scalar distance.</returns>
    public static double Dist(Point2f a, Point2f b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>
    /// Sorts four quad vertices to top-left, top-right, bottom-right, bottom-left using sum and difference of x,y.
    /// </summary>
    /// <param name="pts">Four corner points from contour or min-area rectangle.</param>
    /// <returns>Array in order tl, tr, br, bl.</returns>
    public static Point2f[] OrderCorners(Point2f[] pts)
    {
        var sum = pts.Select(p => p.X + p.Y).ToArray();
        var diff = pts.Select(p => p.X - p.Y).ToArray();

        var tl = pts[Array.IndexOf(sum, sum.Min())];
        var br = pts[Array.IndexOf(sum, sum.Max())];
        var tr = pts[Array.IndexOf(diff, diff.Max())];
        var bl = pts[Array.IndexOf(diff, diff.Min())];

        return new[] { tl, tr, br, bl };
    }
}
