using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Geometric;

/// <summary>
/// Crops to document content using contour JSON when present, otherwise Otsu foreground bounds.
/// Optionally trims near-uniform edge rows/columns and applies a margin.
/// </summary>
public sealed class AutoCropPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    /// <summary>Creates the plugin with the image store used to resolve and save images.</summary>
    /// <param name="store">Backing store for input paths and output bytes.</param>
    public AutoCropPlugin(IImageStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Detects a crop rectangle, optionally trims flat borders, encodes PNG, and saves a new reference.
    /// </summary>
    /// <param name="imageReference">Stored image id to read.</param>
    /// <param name="marginPercent">Padding around the crop as a percent of width/height (0–20).</param>
    /// <param name="trimBorders">When true, shrink the crop where edge rows/columns have low gray variance.</param>
    /// <param name="contourData">Optional JSON with quad corners from contour extraction.</param>
    /// <returns>New image reference, or the input reference if the crop would be tiny or nearly full-frame.</returns>
    [Description("Automatically crops the image to the document content area and trims uniform borders. Uses contour data from ExtractDocumentContours if available, otherwise detects content bounds via thresholding.")]
    public Task<string> AutoCrop(
        [Description("The reference number of the image to crop.")] string imageReference,
        [Description("Percentage margin to add around detected content area.")] int marginPercent = 1,
        [Description("Trim uniform-color borders from edges (e.g. scanner artifacts).")] bool trimBorders = true,
        [Description("Optional contour data JSON from ExtractDocumentContours. Uses quad corners for crop region if available.")] string? contourData = null)
    {
        if (marginPercent < 0 || marginPercent > 20)
            throw new ArgumentException("marginPercent must be between 0 and 20");

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImDecode(File.ReadAllBytes(inputPath), ImreadModes.Color);
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        Rect cropRect = GetCropRect(src, contourData);

        if (trimBorders)
            cropRect = TrimUniformBorders(src, cropRect);

        cropRect = ApplyMargin(cropRect, src.Width, src.Height, marginPercent);

        if (cropRect.Width < 10 || cropRect.Height < 10)
            return Task.FromResult(imageReference);

        // Skip if crop region is essentially the full image
        double areaRatio = (double)(cropRect.Width * cropRect.Height) / (src.Width * src.Height);
        if (areaRatio > 0.97)
            return Task.FromResult(imageReference);

        using var cropped = new Mat(src, cropRect);
        Cv2.ImEncode(".png", cropped, out var pngBytes);

        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "auto_crop",
            ["cropRect"] = $"{cropRect.X},{cropRect.Y},{cropRect.Width},{cropRect.Height}",
            ["marginPercent"] = marginPercent.ToString()
        };

        var desiredRef = PluginHelpers.MakeUniqueRef(_store, $"{imageReference}_autocrop");
        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    private static Rect GetCropRect(Mat src, string? contourData)
    {
        if (!string.IsNullOrEmpty(contourData))
        {
            try
            {
                using var doc = JsonDocument.Parse(contourData);
                if (doc.RootElement.TryGetProperty("found", out var found) && found.GetBoolean()
                    && doc.RootElement.TryGetProperty("quadCorners", out var corners))
                {
                    var points = new List<Point>();
                    foreach (var corner in corners.EnumerateArray())
                    {
                        var coords = corner.EnumerateArray().ToArray();
                        points.Add(new Point((int)coords[0].GetDouble(), (int)coords[1].GetDouble()));
                    }
                    if (points.Count == 4)
                        return Cv2.BoundingRect(points);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WARN] contour JSON parse for crop rect: {ex.Message}");
            }
        }

        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        using var binary = new Mat();
        Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        using var nonZero = new Mat();
        Cv2.FindNonZero(binary, nonZero);
        if (nonZero.Empty())
            return new Rect(0, 0, src.Width, src.Height);

        return Cv2.BoundingRect(nonZero);
    }

    private static Rect TrimUniformBorders(Mat src, Rect rect)
    {
        using var roi = new Mat(src, rect);
        using var gray = new Mat();
        Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);

        int top = 0, bottom = 0, left = 0, right = 0;
        const double uniformThreshold = 5.0;

        for (int y = 0; y < gray.Rows; y++)
        {
            using var row = gray.Row(y);
            Cv2.MeanStdDev(row, out _, out var std);
            if (std[0] < uniformThreshold) top++;
            else break;
        }

        for (int y = gray.Rows - 1; y >= top; y--)
        {
            using var row = gray.Row(y);
            Cv2.MeanStdDev(row, out _, out var std);
            if (std[0] < uniformThreshold) bottom++;
            else break;
        }

        for (int x = 0; x < gray.Cols; x++)
        {
            using var col = gray.Col(x);
            Cv2.MeanStdDev(col, out _, out var std);
            if (std[0] < uniformThreshold) left++;
            else break;
        }

        for (int x = gray.Cols - 1; x >= left; x--)
        {
            using var col = gray.Col(x);
            Cv2.MeanStdDev(col, out _, out var std);
            if (std[0] < uniformThreshold) right++;
            else break;
        }

        int newX = rect.X + left;
        int newY = rect.Y + top;
        int newW = rect.Width - left - right;
        int newH = rect.Height - top - bottom;

        if (newW < 10 || newH < 10) return rect;
        return new Rect(newX, newY, newW, newH);
    }

    private static Rect ApplyMargin(Rect rect, int imgW, int imgH, int marginPercent)
    {
        int marginX = rect.Width * marginPercent / 100;
        int marginY = rect.Height * marginPercent / 100;

        int x = Math.Max(0, rect.X - marginX);
        int y = Math.Max(0, rect.Y - marginY);
        int w = Math.Min(imgW - x, rect.Width + 2 * marginX);
        int h = Math.Min(imgH - y, rect.Height + 2 * marginY);

        return new Rect(x, y, w, h);
    }

    /// <summary>Exposes <see cref="AutoCrop"/> as an AI-callable tool.</summary>
    /// <returns>Sequence containing the AutoCrop tool.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.AutoCrop);
    }
}
