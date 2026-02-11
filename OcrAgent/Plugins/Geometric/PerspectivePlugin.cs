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
    private const string DebugFolderName = "perspective_debug";

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
        [Description("Enhance edges before detection.")] bool enhanceEdges = true,
        [Description("Enable debug mode to save intermediate detection images.")] bool debug = false)
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

        string? debugPath = null;
        if (debug)
        {
            debugPath = PrepareDebugFolder(imageReference);
            SaveDebugImage(src, debugPath, "00_input");
        }

        // Early full-frame detection
        var (isFullFrame, paperCoverage, isAligned) = DetectFullFrameScenario(src);

        if (debugPath != null)
            SaveDebugFullFrameAnalysis(src, isFullFrame, paperCoverage, isAligned, debugPath);

        // For full-frame aligned documents, return original
        if (isFullFrame && isAligned && paperCoverage > 0.85)
        {
            Cv2.ImEncode(".png", src, out var origBytes);
            var fullFrameMeta = new Dictionary<string, string>
            {
                ["parent"] = imageReference,
                ["op"] = "perspective_correct",
                ["detectedCorners"] = FormatCorners(GetImageBoundaries(src, 0)),
                ["outputDimensions"] = $"{src.Width}x{src.Height}",
                ["confidence"] = "0.95",
                ["detectionMethod"] = "full_frame_aligned",
                ["paperCoverage"] = paperCoverage.ToString("F2"),
                ["isFullFrame"] = "true",
                ["note"] = "Document fills frame and is well-aligned, returned original"
            };
            var fullFrameRef = $"{imageReference}_perspective_fullframe";
            var fullFrameDesiredRef = PluginHelpers.MakeUniqueRef(_store, fullFrameRef);
            var fullFrameNewRef = _store.SaveBytes(origBytes, "png", fullFrameMeta, fullFrameDesiredRef);
            return Task.FromResult(fullFrameNewRef);
        }

        // detect corners by combining multiple strategies
        var det = DetectCornersMultiStrategy(src, sensitivity, enhanceEdges, debugPath, isFullFrame);

        Point2f[] corners;
        string detectionMethod;
        double confidence;

        if (det.Corners is { Length: 4 })
        {
            corners = OrderCorners(det.Corners);
            detectionMethod = det.Method;
            confidence = det.Confidence;
            
            // For full-frame documents, aggressively snap corners to image boundaries
            // This prevents header/footer clipping by ensuring we capture the entire frame
            if (isFullFrame || paperCoverage > 0.65)
            {
                // Use more aggressive threshold (0.08) for full-frame snapping
                corners = ExtendCornersToImageBounds(corners, src.Width, src.Height, 0.08);
                
                if (debugPath != null)
                    SaveDebugImageWithQuad(src, corners, debugPath, "91_after_edge_snap", confidence);
            }
        }
        else
        {
            int autoMargin = marginPercent > 0 ? marginPercent : 2;
            corners = GetImageBoundaries(src, autoMargin);
            detectionMethod = "fallback_image_bounds";
            confidence = 0.30;
        }

        if (debugPath != null)
        {
            SaveDebugImageWithQuad(src, corners, debugPath, $"1ß0_best_{detectionMethod}", confidence);
        }

        if (marginPercent > 0 && detectionMethod != "fallback_image_bounds")
            corners = ExpandQuad(corners, src.Width, src.Height, marginPercent);

        // check if perspective correction is required or not.
        // for images that are already well-aligned (sample RECEIPT001), do not warp.
        double distortionAmount = CalculateDistortionAmount(corners);
        bool isAlreadyAligned = distortionAmount < 0.05;

        // determine if document fills the frame.
        var boundingRect = Cv2.BoundingRect(corners.Select(p => new Point((int)p.X, (int)p.Y)).ToArray());
        double coverage = (double)(boundingRect.Width * boundingRect.Height) / (src.Width * src.Height);

        // if already aligned and document covers >85% of the image, return original image.
        if (isAlreadyAligned && coverage > 0.85 && detectionMethod != "fallback_image_bounds")
        {
            Cv2.ImEncode(".png", src, out var origBytes);
            var alignedMeta = new Dictionary<string, string>
            {
                ["parent"] = imageReference,
                ["op"] = "perspective_correct",
                ["detectedCorners"] = FormatCorners(corners),
                ["outputDimensions"] = $"{src.Width}x{src.Height}",
                ["confidence"] = confidence.ToString("F2"),
                ["detectionMethod"] = "already_aligned",
                ["distortionAmount"] = distortionAmount.ToString("F3"),
                ["coverage"] = coverage.ToString("F2"),
                ["note"] = "Source image already well-aligned, returned original"
            };
            var alignedRef = $"{imageReference}_perspective_aligned";
            var alignedDesiredRef = PluginHelpers.MakeUniqueRef(_store, alignedRef);
            var alignedNewRef = _store.SaveBytes(origBytes, "png", alignedMeta, alignedDesiredRef);
            return Task.FromResult(alignedNewRef);
        }

        // auto-extend bottom area (GrabCut/HSV often crops the bottom edge).
        // goal: avoid cropping the last line or bottom of the document (sample RECEIPT002).
        if (detectionMethod != "fallback_image_bounds" && !isAlreadyAligned)
        {
            corners = OrderCorners(corners);

            // apply extension on methods tends to shrink, along with low confidence.
            bool shouldAutoExtendBottom =
                detectionMethod.Contains("grabcut", StringComparison.OrdinalIgnoreCase) ||
                detectionMethod.Contains("hsv", StringComparison.OrdinalIgnoreCase) ||
                confidence < 0.65;

            if (shouldAutoExtendBottom)
            {
                float bottomY = Math.Max(corners[2].Y, corners[3].Y);
                float targetBottom = src.Height - 2;

                // if still have margin at the bottom, push the bottom edge down.
                float delta = targetBottom - bottomY;

                // avoid over-stretch (8% of height)
                float maxDelta = (float)(src.Height * 0.08);
                if (delta > 0)
                {
                    delta = Math.Min(delta, maxDelta);

                    corners[2] = new Point2f(corners[2].X, Math.Clamp(corners[2].Y + delta, 0, src.Height - 1));
                    corners[3] = new Point2f(corners[3].X, Math.Clamp(corners[3].Y + delta, 0, src.Height - 1));

                    // correct the order
                    corners = OrderCorners(corners);
                }
            }
        }

        // output size
        var (outW, outH) = CalculateOutputDimensions(corners, targetWidth, targetHeight);

        // warp image
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
            ["distortionAmount"] = distortionAmount.ToString("F3"),
            ["coverage"] = coverage.ToString("F2"),
            ["marginPercent"] = marginPercent.ToString(),
            ["marginApplied"] = marginPercent.ToString(),
            ["enhanceEdges"] = enhanceEdges.ToString()
        };

        var baseRef = $"{imageReference}_perspective_{SanitizeForRef(detectionMethod)}";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);

        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    // Detection method - multiple strategies (edge + otsu + hsv + grabcut)
    private (Point2f[]? Corners, double Confidence, string Method) DetectCornersMultiStrategy(
        Mat src,
        string sensitivity,
        bool enhanceEdges,
        string? debugPath = null,
        bool isFullFrame = false)
    {
        // run all strategies and store results
        var candidates = new List<(Point2f[]? Corners, double Confidence, string Method, double Quality)>();

        // edge-based
        var edge = TryDetectByEdges(src, sensitivity, enhanceEdges, debugPath);
        if (edge.Corners is { Length: 4 })
        {
            double quality = CalculateDetectionQuality(edge.Corners, src.Width, src.Height);
            candidates.Add((edge.Corners, edge.Confidence, edge.Method, quality));
            if (debugPath != null)
                SaveDebugImageWithQuad(src, edge.Corners, debugPath, $"10_edges_result_q{quality:F2}", edge.Confidence);
        }

        // otsu paper segmentation
        var otsu = TryDetectByOtsuSegmentation(src, sensitivity, debugPath);
        if (otsu.Corners is { Length: 4 })
        {
            double quality = CalculateDetectionQuality(otsu.Corners, src.Width, src.Height);
            candidates.Add((otsu.Corners, otsu.Confidence, otsu.Method, quality));
            if (debugPath != null)
                SaveDebugImageWithQuad(src, otsu.Corners, debugPath, $"20_otsu_result_q{quality:F2}", otsu.Confidence);
        }

        // hsv region segmentation
        var hsv = TryDetectByHsvLightRegionSegmentation(src, sensitivity, debugPath);
        if (hsv.Corners is { Length: 4 })
        {
            double quality = CalculateDetectionQuality(hsv.Corners, src.Width, src.Height);
            candidates.Add((hsv.Corners, hsv.Confidence, hsv.Method, quality));
            if (debugPath != null)
                SaveDebugImageWithQuad(src, hsv.Corners, debugPath, $"30_hsv_result_q{quality:F2}", hsv.Confidence);
        }

        // grabcut segmentation
        var grabcut = TryDetectByGrabCut(src, sensitivity, debugPath, isFullFrame);
        if (grabcut.Corners is { Length: 4 })
        {
            double quality = CalculateDetectionQuality(grabcut.Corners, src.Width, src.Height);
            candidates.Add((grabcut.Corners, grabcut.Confidence, grabcut.Method, quality));
            if (debugPath != null)
                SaveDebugImageWithQuad(src, grabcut.Corners, debugPath, $"40_grabcut_result_q{quality:F2}", grabcut.Confidence);
        }

        if (candidates.Count == 0)
            return (null, 0.0, "none");

        // pick the best by confidence
        var best = candidates
            .OrderByDescending(c => c.Confidence * 0.4 + c.Quality * 0.6)
            .First();

        if (debugPath != null && best.Corners != null)
        {
            SaveDebugImageWithQuad(src, best.Corners, debugPath, $"90_winner_{best.Method}", best.Confidence);
        }

        return (best.Corners, best.Confidence, best.Method);
    }

    /// <summary>
    /// Calculates a quality score for a detected quad
    /// </summary>
    private static double CalculateDetectionQuality(Point2f[] quad, int imgW, int imgH)
    {
        quad = OrderCorners(quad);
        double quality = 0.0;

        // rectangularity
        double rectScore = RectangularityScore(quad);
        quality += rectScore * 0.35;

        // corner position quality
        int cornersAtBoundary = 0;
        foreach (var p in quad)
        {
            if (p.X <= 2 || p.Y <= 2 || p.X >= imgW - 3 || p.Y >= imgH - 3)
                cornersAtBoundary++;
        }
        double boundaryPenalty = cornersAtBoundary * 0.12;
        quality -= boundaryPenalty;

        // coverage
        var rect = Cv2.BoundingRect(quad.Select(p => new Point((int)p.X, (int)p.Y)).ToArray());
        double coverage = (double)(rect.Width * rect.Height) / (imgW * imgH);

        // ideal coverage is 0.3-0.8 for documents with background
        if (coverage >= 0.3 && coverage <= 0.85)
            quality += 0.25;
        else if (coverage > 0.85)
            quality += 0.15; // full-frame docs
        else
            quality += coverage * 0.3; // Smaller coverage

        // distortion amount
        double distortion = CalculateDistortionAmount(quad);
        if (distortion < 0.3)
            quality += 0.25 * (1.0 - distortion);

        return Math.Max(0.0, Math.Min(1.0, quality));
    }

    private (Point2f[]? Corners, double Confidence, string Method) TryDetectByEdges(
        Mat src,
        string sensitivity,
        bool enhanceEdges,
        string? debugPath = null)
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

        if (debugPath != null)
            SaveDebugImage(prep, debugPath, "11_edges_preprocessed");

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

        if (debugPath != null)
            SaveDebugImage(edges, debugPath, "12_edges_canny");

        using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
        using var morph = new Mat();
        Cv2.Dilate(edges, morph, k, iterations: 2);
        Cv2.MorphologyEx(morph, morph, MorphTypes.Close, k, iterations: 2);

        if (debugPath != null)
            SaveDebugImage(morph, debugPath, "13_edges_morphed");

        Cv2.FindContours(morph, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0) return (null, 0.0, "edges_none");

        if (debugPath != null)
            SaveDebugImageWithContours(resized, contours, debugPath, "14_edges_contours");

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
    private (Point2f[]? Corners, double Confidence, string Method) TryDetectByOtsuSegmentation(Mat src, string sensitivity, string? debugPath = null)
    {
        using var resized = ResizeForDetection(src, 900, out var scale);

        using var gray = new Mat();
        Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);

        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

        // Otsu
        using var bin = new Mat();
        Cv2.Threshold(blurred, bin, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        if (debugPath != null)
            SaveDebugImage(bin, debugPath, "21_otsu_binary");

        // cleanup
        int kSize = sensitivity == "aggressive" ? 17 : 21;
        using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(kSize, kSize));
        Cv2.MorphologyEx(bin, bin, MorphTypes.Close, k, iterations: 2);
        Cv2.MorphologyEx(bin, bin, MorphTypes.Open, k, iterations: 1);

        if (debugPath != null)
            SaveDebugImage(bin, debugPath, "22_otsu_morphed");

        // contours
        Cv2.FindContours(bin, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0) return (null, 0.0, "otsu_none");

        if (debugPath != null)
            SaveDebugImageWithContours(resized, contours, debugPath, "23_otsu_contours");

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
    private (Point2f[]? Corners, double Confidence, string Method) TryDetectByHsvLightRegionSegmentation(Mat src, string sensitivity, string? debugPath = null)
    {
        using var resized = ResizeForDetection(src, 900, out var scale);

        using var hsv = new Mat();
        Cv2.CvtColor(resized, hsv, ColorConversionCodes.BGR2HSV);

        int sMax = sensitivity == "aggressive" ? 120 : 85;
        int vMin = sensitivity == "conservative" ? 185 : 165;

        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, vMin), new Scalar(180, sMax, 255), mask);

        if (debugPath != null)
            SaveDebugImage(mask, debugPath, "31_hsv_initial_mask");

        using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(19, 19));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, k, iterations: 2);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, k, iterations: 1);

        if (debugPath != null)
            SaveDebugImage(mask, debugPath, "32_hsv_morphed");

        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0) return (null, 0.0, "hsv_none");

        if (debugPath != null)
            SaveDebugImageWithContours(resized, contours, debugPath, "33_hsv_contours");

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

    /// <summary>
    /// Foreground segmentation with GrabCut.
    /// </summary>
    private (Point2f[]? Corners, double Confidence, string Method) TryDetectByGrabCut(Mat src, string sensitivity, string? debugPath = null, bool isFullFrame = false)
    {
        using var resized = ResizeForDetection(src, 900, out var scale);

        using var mask = new Mat(resized.Rows, resized.Cols, MatType.CV_8UC1, Scalar.All(0));
        using var bgdModel = new Mat(1, 65, MatType.CV_64FC1, Scalar.All(0));
        using var fgdModel = new Mat(1, 65, MatType.CV_64FC1, Scalar.All(0));

        // smaller margin for full-frame documents
        double m = isFullFrame ? 0.01 : (sensitivity == "aggressive" ? 0.03 : 0.05);
        int x = (int)(resized.Cols * m);
        int y = (int)(resized.Rows * m);
        int w = (int)(resized.Cols * (1.0 - 2 * m));
        int h = (int)(resized.Rows * (1.0 - 2 * m));
        var rect = new Rect(x, y, w, h);

        if (debugPath != null)
        {
            using var rectVis = resized.Clone();
            Cv2.Rectangle(rectVis, rect, new Scalar(0, 255, 0), 2);
            SaveDebugImage(rectVis, debugPath, "41_grabcut_init_rect");
        }

        Cv2.GrabCut(resized, mask, rect, bgdModel, fgdModel, 5, GrabCutModes.InitWithRect);

        // foreground mask
        using var fg = new Mat();
        using var sure = new Mat();
        using var prob = new Mat();
        const byte GC_FGD = 1;
        const byte GC_PR_FGD = 3;

        Cv2.InRange(mask, new Scalar(GC_FGD), new Scalar(GC_FGD), sure);
        Cv2.InRange(mask, new Scalar(GC_PR_FGD), new Scalar(GC_PR_FGD), prob);
        Cv2.BitwiseOr(sure, prob, fg);

        if (debugPath != null)
            SaveDebugImage(fg, debugPath, "42_grabcut_fg_mask");

        using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(13, 13));
        Cv2.MorphologyEx(fg, fg, MorphTypes.Close, k, iterations: 2);

        if (debugPath != null)
            SaveDebugImage(fg, debugPath, "43_grabcut_morphed");

        Cv2.FindContours(fg, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0) return (null, 0.0, "grabcut_none");

        if (debugPath != null)
            SaveDebugImageWithContours(resized, contours, debugPath, "44_grabcut_contours");

        var best = SelectBestQuadFromContours(
            contours,
            resized.Width,
            resized.Height,
            0.08,
            MaxAreaRatio);

        if (best.Quad is null) return (null, 0.0, "grabcut_no_quad");

        var quad = ScaleQuad(best.Quad, 1.0 / scale);
        if (!IsReasonableQuad(quad, src.Width, src.Height)) return (null, 0.0, "grabcut_bad_quad");

        double conf = Math.Min(0.92, 0.55 + best.Score * 0.60);
        return (quad, conf, "grabcut");
    }

    // Contour scoring methods

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

        // NEW: Detect if document fills most part of the frame
        bool isLikelyFullFrame = false;
        double largestCoverageRatio = 0.0;
        if (contours.Length > 0)
        {
            var largest = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();
            largestCoverageRatio = Cv2.ContourArea(largest) / imgArea;
            isLikelyFullFrame = largestCoverageRatio > 0.65; // Lowered from 0.70 to catch more full-frame cases
        }

        foreach (var c in contours
                     .OrderByDescending(c => Cv2.ContourArea(c))
                     .Take(MaxSearchContours))
        {
            double area = Cv2.ContourArea(c);
            double areaRatio = area / imgArea;
            if (areaRatio < minAreaRatio || areaRatio > maxAreaRatio)
                continue;

            // MODIFIED: Only apply border check for non-full-frame documents
            // For full-frame documents (sample RECEIPT001), the contour should touch borders
            if (!isLikelyFullFrame &&
                TouchesBorder(c, imgW, imgH, marginPx: Math.Max(6, (int)(Math.Min(imgW, imgH) * 0.01))))
                continue;

            Point2f[]? quad;

            // high-coverage contours, use min area rect directly
            // This handles bumpy contours caused by background texture (e.g., granite)
            if (areaRatio > 0.60)
            {
                // regularized contour for full-frame to smooth out texture bumps
                quad = RegularizeContourForFullFrame(c, imgW, imgH);
            }
            else
            {
                // standard approximation for documents with clear background
                quad = ApproximateToQuad(c);
                if (quad is null)
                {
                    // fallback: min area rect
                    var rr = Cv2.MinAreaRect(c);
                    quad = Cv2.BoxPoints(rr).Select(p => new Point2f(p.X, p.Y)).ToArray();
                }
            }

            if (quad.Length != 4)
                continue;

            // score: area + rectangularity + coverage bonus - internal penalty
            double rect = RectangularityScore(quad);
            double cornerPenalty = CornerBorderPenalty(quad, imgW, imgH);

            // new: coverage bonus
            var boundingRect = Cv2.BoundingRect(quad.Select(p => new Point((int)p.X, (int)p.Y)).ToArray());
            double coverage = (double)(boundingRect.Width * boundingRect.Height) / imgArea;
            double coverageBonus = coverage > 0.5 ? (coverage - 0.5) * 0.3 : 0;

            // full-frame documents, add bonus for high coverage (reduces penalty for touching borders)
            if (isLikelyFullFrame && coverage > 0.80)
                coverageBonus += 0.15;

            // penalize internal elements - prevents detecting internal text blocks as the document
            double internalPenalty = areaRatio < 0.30 ? 0.15 : 0;

            // score
            double score = (areaRatio * 0.50) + (rect * 0.25) + (cornerPenalty * 0.05) + coverageBonus - internalPenalty;

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
        // convex hull to fix weak/missing edges
        var hull = Cv2.ConvexHull(contour);
        double peri = Cv2.ArcLength(hull, true);

        // try multiple eps values until get 4 points
        // smaller eps => more points; larger eps => fewer points
        double[] epsList = { 0.05, 0.04, 0.03, 0.025, 0.02, 0.018, 0.015, 0.012, 0.01 };

        foreach (var e in epsList)
        {
            var approx = Cv2.ApproxPolyDP(hull, e * peri, true);
            if (approx.Length == 4)
                return approx.Select(p => new Point2f(p.X, p.Y)).ToArray();
        }

        return null;
    }

    /// <summary>
    /// regularizes a contour for full-frame documents by using min area rect.
    /// this smooths out texture bumps (e.g., granite background noise) that cause
    /// contour irregularities along the edges.
    /// </summary>
    private static Point2f[] RegularizeContourForFullFrame(Point[] contour, int imgW, int imgH)
    {    
        // minimum area rect to get clean edges
        var minRect = Cv2.MinAreaRect(contour);
        var boxPoints = Cv2.BoxPoints(minRect);
        
        var quad = boxPoints.Select(p => new Point2f(p.X, p.Y)).ToArray();
        
        // snap corners that are very close to image edges
        quad = SnapQuadToImageEdges(quad, imgW, imgH, edgeThresholdRatio: 0.03);
        
        return OrderCorners(quad);
    }

    /// <summary>
    /// snaps quad corners that are near image edges to the exact edge.
    /// prevents small gaps or overflows at the boundaries.
    /// </summary>
    private static Point2f[] SnapQuadToImageEdges(Point2f[] quad, int imgW, int imgH, double edgeThresholdRatio)
    {
        double thresholdX = imgW * edgeThresholdRatio;
        double thresholdY = imgH * edgeThresholdRatio;
        
        return quad.Select(p =>
        {
            float x = p.X, y = p.Y;
            
            // snap to left edge
            if (x < thresholdX) x = 0;
            // snap to right edge
            if (x > imgW - 1 - thresholdX) x = imgW - 1;
            // snap to top edge
            if (y < thresholdY) y = 0;
            // snap to bottom edge
            if (y > imgH - 1 - thresholdY) y = imgH - 1;
            
            return new Point2f(x, y);
        }).ToArray();
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
        // 1.0 means corners are away from the borders
        // 0.0 means corners are on the border
        int margin = Math.Max(10, (int)(Math.Min(w, h) * 0.02));
        int good = 0;

        foreach (var p in quad)
        {
            if (p.X > margin && p.Y > margin && p.X < (w - 1 - margin) && p.Y < (h - 1 - margin))
                good++;
        }

        return good / 4.0;
    }

    // geometry helper methods

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

        // too small => not a document
        if (rect.Width < imgW * 0.12 || rect.Height < imgH * 0.12)
            return false;

        // entire image => background selection
        if (rect.Width > imgW * 0.997 && rect.Height > imgH * 0.997)
            return false;

        // new: aspect ratio sanity check
        double srcAspect = (double)imgH / imgW;
        double quadAspect = (double)rect.Height / rect.Width;
        if (srcAspect > 1.3 && quadAspect < 0.7)
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
    /// Calculates how much perspective distortion is present in the detected quad
    /// returns a value from 0 (perfect rectangle) to higher values (trapezoidal)
    /// </summary>
    private static double CalculateDistortionAmount(Point2f[] quad)
    {
        quad = OrderCorners(quad);
        
        // measure how much the shape deviates from a rectangle
        double topWidth = Dist(quad[0], quad[1]);
        double bottomWidth = Dist(quad[2], quad[3]);
        double leftHeight = Dist(quad[0], quad[3]);
        double rightHeight = Dist(quad[1], quad[2]);
        
        // width ratio: 1.0 => parallel sides, <1.0 => trapezoidal
        double widthRatio = Math.Min(topWidth, bottomWidth) / Math.Max(topWidth, bottomWidth);
        double heightRatio = Math.Min(leftHeight, rightHeight) / Math.Max(leftHeight, rightHeight);
        
        // distortion score: 0 => perfect rectangle, higher => more distortion
        double distortion = (1.0 - widthRatio) + (1.0 - heightRatio);
        return distortion;
    }

    /// <summary>
    /// Detects if the document fills the whole frame.
    /// </summary>
    private static (bool IsFullFrame, double PaperCoverage, bool IsAligned) DetectFullFrameScenario(Mat src)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(src, hsv, ColorConversionCodes.BGR2HSV);

        // Paper detection
        using var paperMask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 160), new Scalar(180, 80, 255), paperMask);

        // overall paper coverage
        double totalPixels = src.Width * src.Height;
        double paperPixels = Cv2.CountNonZero(paperMask);
        double paperCoverage = paperPixels / totalPixels;

        // border regions
        double borderRatio = 0.03;
        int topH = (int)(src.Height * borderRatio);
        int bottomY = (int)(src.Height * (1 - borderRatio));
        int leftW = (int)(src.Width * borderRatio);
        int rightX = (int)(src.Width * (1 - borderRatio));

        // border strips
        var topStrip = new Rect(0, 0, src.Width, topH);
        var bottomStrip = new Rect(0, bottomY, src.Width, src.Height - bottomY);
        var leftStrip = new Rect(0, 0, leftW, src.Height);
        var rightStrip = new Rect(rightX, 0, src.Width - rightX, src.Height);

        double topPaper = CountPaperRatio(paperMask, topStrip);
        double bottomPaper = CountPaperRatio(paperMask, bottomStrip);
        double leftPaper = CountPaperRatio(paperMask, leftStrip);
        double rightPaper = CountPaperRatio(paperMask, rightStrip);

        // Full-frame if most borders are paper-colored
        // Lowered threshold from 0.75 to 0.65 to catch more full-frame scenarios (e.g., RECEIPT001 at 71%)
        double avgBorderPaper = (topPaper + bottomPaper + leftPaper + rightPaper) / 4.0;
        bool isFullFrame = avgBorderPaper > 0.55 && paperCoverage > 0.65;

        // alignment
        bool isAligned = false;
        if (isFullFrame)
        {
            // edge detection
            using var gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
            using var edges = new Mat();
            Cv2.Canny(gray, edges, 50, 150);

            // Hough lines
            var lines = Cv2.HoughLines(edges, 1, Math.PI / 180, 100);
            if (lines.Length > 0)
            {
                var angles = lines.Take(20).Select(l => l.Theta * 180 / Math.PI).ToList();
                
                // aligned
                int alignedCount = angles.Count(a => 
                    a < 10 || a > 170 || (a > 80 && a < 100));
                
                isAligned = alignedCount > lines.Length * 0.5 || angles.Count < 5;
            }
            else
            {
                // aligned
                isAligned = true;
            }
        }

        return (isFullFrame, paperCoverage, isAligned);
    }

    private static double CountPaperRatio(Mat paperMask, Rect region)
    {
        if (region.Width <= 0 || region.Height <= 0)
            return 0.0;
        
        region = new Rect(
            Math.Max(0, region.X),
            Math.Max(0, region.Y),
            Math.Min(region.Width, paperMask.Width - region.X),
            Math.Min(region.Height, paperMask.Height - region.Y)
        );
        
        if (region.Width <= 0 || region.Height <= 0)
            return 0.0;

        using var roi = new Mat(paperMask, region);
        double pixels = region.Width * region.Height;
        double paperPixels = Cv2.CountNonZero(roi);
        return paperPixels / pixels;
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

    /// <summary>
    /// For full-frame documents, snap corners near image boundaries.
    /// This fixes cases where detection finds 95% of document but misses edge pixels.
    /// </summary>
    private static Point2f[] ExtendCornersToImageBounds(Point2f[] corners, int imgW, int imgH, double threshold = 0.05)
    {
        var result = corners.Select(c => {
            float x = c.X, y = c.Y;
            
            // Snap to left/right edges
            if (x < imgW * threshold) x = 0;
            if (x > imgW * (1 - threshold)) x = imgW - 1;
            
            // Snap to top/bottom edges
            if (y < imgH * threshold) y = 0;
            if (y > imgH * (1 - threshold)) y = imgH - 1;
            
            return new Point2f(x, y);
        }).ToArray();
        
        return OrderCorners(result);
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

    // Debug helper methods

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

    private static void SaveDebugFullFrameAnalysis(Mat src, bool isFullFrame, double paperCoverage, bool isAligned, string debugPath)
    {
        using var vis = src.Clone();
        
        // info overlay
        var info = new[]
        {
            $"Full-Frame Analysis",
            $"IsFullFrame: {isFullFrame}",
            $"PaperCoverage: {paperCoverage:P0}",
            $"IsAligned: {isAligned}"
        };
        
        int y = 30;
        var bgColor = isFullFrame ? new Scalar(0, 100, 0) : new Scalar(100, 0, 0);
        Cv2.Rectangle(vis, new Rect(5, 5, 300, 120), bgColor, -1);
        
        foreach (var line in info)
        {
            Cv2.PutText(vis, line, new Point(10, y), 
                HersheyFonts.HersheySimplex, 0.7, new Scalar(255, 255, 255), 2);
            y += 28;
        }
        
        // border analysis strips
        double borderRatio = 0.03;
        int topH = (int)(src.Height * borderRatio);
        int bottomY = (int)(src.Height * (1 - borderRatio));
        int leftW = (int)(src.Width * borderRatio);
        int rightX = (int)(src.Width * (1 - borderRatio));
        
        var borderColor = new Scalar(255, 255, 0);
        Cv2.Rectangle(vis, new Rect(0, 0, src.Width, topH), borderColor, 2);
        Cv2.Rectangle(vis, new Rect(0, bottomY, src.Width, src.Height - bottomY), borderColor, 2);
        Cv2.Rectangle(vis, new Rect(0, 0, leftW, src.Height), borderColor, 2);
        Cv2.Rectangle(vis, new Rect(rightX, 0, src.Width - rightX, src.Height), borderColor, 2);
        
        var filePath = Path.Combine(debugPath, "01_fullframe_analysis.png");
        Cv2.ImWrite(filePath, vis);
    }

    private static void SaveDebugImageWithQuad(Mat src, Point2f[] quad, string debugPath, string name, double confidence)
    {
        using var vis = src.Clone();
        var ordered = OrderCorners(quad);
        var pts = ordered.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
        
        Cv2.Polylines(vis, new[] { pts }, true, new Scalar(0, 255, 0), 3);
        
        for (int i = 0; i < 4; i++)
        {
            string[] labels = { "TL", "TR", "BR", "BL" };
            Cv2.Circle(vis, pts[i], 8, new Scalar(0, 0, 255), -1);
            Cv2.PutText(vis, labels[i], new Point(pts[i].X + 10, pts[i].Y - 10), 
                HersheyFonts.HersheySimplex, 0.8, new Scalar(255, 0, 0), 2);
        }
        
        var infoText = $"Conf: {confidence:F2}";
        Cv2.PutText(vis, infoText, new Point(10, 30), 
            HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 255, 255), 2);
        
        var filePath = Path.Combine(debugPath, $"{name}.png");
        Cv2.ImWrite(filePath, vis);
    }

    private static void SaveDebugImageWithContours(Mat src, Point[][] contours, string debugPath, string name)
    {
        using var vis = src.Clone();
        
        var sortedContours = contours
            .Select((c, i) => (Contour: c, Index: i, Area: Cv2.ContourArea(c)))
            .OrderByDescending(x => x.Area)
            .Take(5)
            .ToList();
        
        var colors = new[]
        {
            new Scalar(0, 255, 0),
            new Scalar(255, 0, 0),
            new Scalar(0, 0, 255),
            new Scalar(255, 255, 0),
            new Scalar(255, 0, 255)
        };
        
        for (int i = 0; i < sortedContours.Count; i++)
        {
            var (contour, _, area) = sortedContours[i];
            Cv2.DrawContours(vis, new[] { contour }, 0, colors[i % colors.Length], 2);
            
            var rect = Cv2.BoundingRect(contour);
            double areaRatio = area / (src.Width * src.Height);
            var label = $"#{i + 1} ({areaRatio:P0})";
            Cv2.PutText(vis, label, new Point(rect.X, rect.Y - 5), 
                HersheyFonts.HersheySimplex, 0.6, colors[i % colors.Length], 2);
        }
        
        var filePath = Path.Combine(debugPath, $"{name}.png");
        Cv2.ImWrite(filePath, vis);
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.CorrectPerspective);
    }
}
