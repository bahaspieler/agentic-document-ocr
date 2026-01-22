using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Geometric;

/// <summary>
/// Plugin for detecting and correcting document skew.
/// Essential for receipts, scanned documents, and forms.
/// </summary>
public sealed class DeskewPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;
    private const double MinimumSkewThreshold = 0.5;

    public DeskewPlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Auto-detects and corrects document skew. Essential for receipts and forms. Returns deskewed image reference.")]
    public Task<string> DeskewImage(
        [Description("The reference number of the image to deskew.")] string imageReference,
        [Description("Maximum expected skew angle in degrees (10 for receipts or 5 for scans).")] double maxAngle = 20.0,
        [Description("Detection method: 'hough' (best for text lines) or 'projection' (best for forms/tables).")] string method = "hough",
        [Description("Preserve original image size (true) or fit to rotated dimensions (false).")] bool preserveSize = true)
    {
        method = method.ToLowerInvariant();
        if (method != "hough" && method != "projection")
            throw new ArgumentException("method must be 'hough' or 'projection'");

        if (maxAngle <= 0 || maxAngle > 45)
            throw new ArgumentException("maxAngle must be between 0 and 45 degrees");

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);

        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        
        return Task.FromResult(string.Empty);
    }

    private double DetectSkewHoughLines(Mat src, double maxAngle)
    {
        // Convert to grayscale
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        // Gaussian blur to reduce noise
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

        // Edge detection
        using var edges = new Mat();
        Cv2.Canny(blurred, edges, 50, 150, 3);

        // Hough Line Transform
        var lines = Cv2.HoughLinesP(edges, 1, Math.PI / 180, 100, 100, 10);

        if (lines == null || lines.Length == 0)
            return 0.0;

        // angles of detected lines
        var angles = new List<double>();
        foreach (var line in lines)
        {
            double angle = Math.Atan2(line.P2.Y - line.P1.Y, line.P2.X - line.P1.X) * 180.0 / Math.PI;
            
            // normalize to -45 to 45 degrees
            if (angle < -45)
                angle += 90;
            else if (angle > 45)
                angle -= 90;

            // lines within the range
            if (Math.Abs(angle) <= maxAngle)
                angles.Add(angle);
        }

        // return median angle
        if (angles.Count == 0)
            return 0.0;

        angles.Sort();
        return angles[angles.Count / 2];
    }
    

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.DeskewImage);
    }
}
