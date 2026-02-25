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
        // Only allow on_white for now
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

        
        return Task.FromResult(string.Empty);
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
