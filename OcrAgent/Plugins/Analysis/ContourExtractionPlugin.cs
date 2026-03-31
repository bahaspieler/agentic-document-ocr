using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.Json;
using OcrAgent.Storage;
using OcrAgent.Inference;
using OpenCvSharp;

namespace OcrAgent.Plugins.Analysis;

/// <summary>
/// Finds document outline (quad + mask reference) using U2-Net when available, else classical CV.
/// </summary>
public sealed class ContourExtractionPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;
    private readonly U2NetpOnnxPredictor? _predictor;

    /// <summary>
    /// Creates a plugin with optional U2-Net mask prediction.
    /// </summary>
    /// <param name="store">Store for input paths and saved masks.</param>
    /// <param name="predictor">Optional ONNX predictor; if null, uses OpenCV-only path.</param>
    public ContourExtractionPlugin(IImageStore store, U2NetpOnnxPredictor? predictor = null)
    {
        _store = store;
        _predictor = predictor;
    }

    /// <summary>
    /// Returns JSON with found flag, quad corners, mask reference, and scores; does not alter the source file.
    /// </summary>
    /// <param name="imageReference">Image id in the store.</param>
    /// <returns>Serialized contour result.</returns>
    [Description("Extracts document boundaries and contours from an image. Returns JSON with quad corners, mask reference, and confidence score. Does not modify the original image.")]
    public Task<string> ExtractDocumentContours(
        [Description("The reference number of the image to analyze.")] string imageReference)
    {
        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference}");

        if (_predictor != null)
            return Task.FromResult(ExtractWithU2Netp(src, imageReference));

        return Task.FromResult(ExtractWithCV(src, imageReference));
    }

    /// <summary>
    /// Runs <see cref="OcrAgent.Inference.DocumentContourExtractor"/> on the U2-Net probability map and saves the mask.
    /// </summary>
    private string ExtractWithU2Netp(Mat src, string imageReference)
    {
        using var probMask = _predictor!.PredictProbabilityMask(src);

        bool found = DocumentContourExtractor.TryExtractDocument(
            probMask, out var mask8u, out var quadCorners,
            out double maskCoverage, out double confidence);

        if (!found || mask8u == null)
        {
            mask8u?.Dispose();
            return JsonSerializer.Serialize(new { found = false });
        }

        using (mask8u)
        {
            Cv2.ImEncode(".png", mask8u, out var maskBytes);
            var maskRef = _store.SaveBytes(maskBytes, "png",
                new Dictionary<string, string>
                {
                    ["parent"] = imageReference,
                    ["op"] = "contour_mask"
                },
                PluginHelpers.MakeUniqueRef(_store, $"{imageReference}_contour_mask"));

            var corners = quadCorners.Select(p => new[] { Math.Round(p.X, 1), Math.Round(p.Y, 1) }).ToArray();

            return JsonSerializer.Serialize(new
            {
                found = true,
                quadCorners = corners,
                maskReference = maskRef,
                confidence = Math.Round(confidence, 3),
                maskCoverage = Math.Round(maskCoverage, 3)
            });
        }
    }

    /// <summary>
    /// Edge-based largest contour, convex hull, and four-point approximation fallback.
    /// </summary>
    private string ExtractWithCV(Mat src, string imageReference)
    {
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
        using var edges = new Mat();
        Cv2.Canny(blurred, edges, 50, 150);

        int k = Math.Clamp((int)(Math.Min(src.Width, src.Height) * 0.01), 3, 15);
        if (k % 2 == 0) k++;
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(k, k));
        Cv2.Dilate(edges, edges, kernel, iterations: 2);

        Cv2.FindContours(edges, out var contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
            return JsonSerializer.Serialize(new { found = false });

        double imgArea = src.Width * (double)src.Height;
        var bestContour = contours
            .Where(c => Cv2.ContourArea(c) > imgArea * 0.10)
            .OrderByDescending(c => Cv2.ContourArea(c))
            .FirstOrDefault();

        if (bestContour == null)
            return JsonSerializer.Serialize(new { found = false });

        var hull = Cv2.ConvexHull(bestContour);
        double peri = Cv2.ArcLength(hull, true);
        Point[]? approx4 = null;

        foreach (var eps in new[] { 0.05, 0.04, 0.03, 0.02, 0.015, 0.01 })
        {
            var approx = Cv2.ApproxPolyDP(hull, eps * peri, true);
            if (approx.Length == 4 && Cv2.IsContourConvex(approx))
            {
                approx4 = approx;
                break;
            }
        }

        if (approx4 == null)
            return JsonSerializer.Serialize(new { found = false });

        var quad = DocumentContourExtractor.OrderCorners(
            approx4.Select(p => new Point2f(p.X, p.Y)).ToArray());
        double contourArea = Cv2.ContourArea(bestContour);
        double coverage = contourArea / imgArea;

        using var mask = new Mat(src.Rows, src.Cols, MatType.CV_8UC1, Scalar.Black);
        Cv2.DrawContours(mask, new[] { bestContour }, 0, Scalar.White, thickness: -1);

        Cv2.ImEncode(".png", mask, out var maskBytes);
        var maskRef = _store.SaveBytes(maskBytes, "png",
            new Dictionary<string, string>
            {
                ["parent"] = imageReference,
                ["op"] = "contour_mask"
            },
            PluginHelpers.MakeUniqueRef(_store, $"{imageReference}_contour_mask"));

        var corners = quad.Select(p => new[] { Math.Round(p.X, 1), Math.Round(p.Y, 1) }).ToArray();

        return JsonSerializer.Serialize(new
        {
            found = true,
            quadCorners = corners,
            maskReference = maskRef,
            confidence = Math.Round(0.5 * coverage / 0.5, 3),
            maskCoverage = Math.Round(coverage, 3)
        });
    }

    /// <summary>
    /// Exposes contour extraction as an agent tool.
    /// </summary>
    /// <returns>Single wrapped function.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.ExtractDocumentContours);
    }
}
