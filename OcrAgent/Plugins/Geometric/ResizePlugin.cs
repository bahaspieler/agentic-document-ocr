using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Geometric;

/// <summary>
/// Plugin for resizing images to normalize dimensions for consistent OCR processing.
/// Supports pixel-based resizing, DPI normalization.
/// </summary>
public sealed class ResizePlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public ResizePlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Resizes image to normalize dimensions. Use targetDPI for document standardization (300 recommended for OCR). Can specify pixels or use document type presets.")]
    public Task<string> ResizeImage(
        [Description("The reference number of the image to resize.")] string imageReference,
        [Description("Target width in pixels (0 to auto-calculate from height/aspect ratio).")] int targetWidth = 0,
        [Description("Target height in pixels (0 to auto-calculate from width/aspect ratio).")] int targetHeight = 0,
        [Description("Target DPI for document (e.g., 300 for high-quality OCR, 200 for receipts). Overrides width/height if > 0.")] int targetDPI = 0,
        [Description("Maintain aspect ratio when one dimension is 0 (recommended: true).")] bool maintainAspectRatio = true,
        [Description("Interpolation method: 'linear' (fast), 'cubic' (balanced), 'lanczos4' (for upscaling).")] string interpolation = "lanczos4",
        [Description("Document type preset: 'auto', 'receipt', 'form', 'statement', 'invoice'. Overrides other dimensions if specified.")] string documentType = "auto")
    {
        interpolation = interpolation.ToLowerInvariant();
        documentType = documentType.ToLowerInvariant();

        if (interpolation != "linear" && interpolation != "cubic" && interpolation != "lanczos4")
            throw new ArgumentException("interpolation must be 'linear', 'cubic', or 'lanczos4'");

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);

        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        int originalWidth = src.Width;
        int originalHeight = src.Height;

        // Dimension selection (inline presets + simple DPI conversion)
        if (documentType != "auto")
        {
            (targetWidth, targetHeight) = documentType switch
            {
                "receipt" => (1200, 1600),
                "form" => (2550, 3300),
                "statement" => (2550, 3300),
                "invoice" => (2550, 3300),
                "letter" => (2550, 3300),
                "a4" => (2550, 3300),
                _ => throw new ArgumentException(
                    $"Unknown document type: {documentType}. Use 'receipt', 'form', 'statement', 'invoice', 'letter', or 'a4'.")
            };
        }
        else if (targetDPI > 0)
        {
            const int assumedOriginalDPI = 72;

            double widthInches = originalWidth / (double)assumedOriginalDPI;
            double heightInches = originalHeight / (double)assumedOriginalDPI;

            if (widthInches > 12 || heightInches > 18)
            {
                widthInches = 8.27;
                heightInches = 11.69;
            }

            targetWidth = (int)(widthInches * targetDPI);
            targetHeight = (int)(heightInches * targetDPI);
        }
        else
        {
            if (targetWidth == 0 && targetHeight == 0)
                throw new ArgumentException("Must specify either targetWidth, targetHeight, targetDPI, or documentType");

            if (targetWidth < 0 || targetHeight < 0)
                throw new ArgumentException("Dimensions must be >= 0");
        }

        // Maintain aspect ratio when one dimension is 0
        if (maintainAspectRatio)
        {
            double aspectRatio = (double)originalWidth / originalHeight;

            if (targetWidth == 0 && targetHeight > 0)
            {
                targetWidth = (int)(targetHeight * aspectRatio);
            }
            else if (targetHeight == 0 && targetWidth > 0)
            {
                targetHeight = (int)(targetWidth / aspectRatio);
            }
        }

        // Ensure valid dimensions
        targetWidth = Math.Max(1, targetWidth);
        targetHeight = Math.Max(1, targetHeight);

        // Resolve interpolation flag
        var interpFlag = interpolation switch
        {
            "linear" => InterpolationFlags.Linear,
            "cubic" => InterpolationFlags.Cubic,
            "lanczos4" => InterpolationFlags.Lanczos4,
            _ => InterpolationFlags.Lanczos4
        };

        // Resize
        using var resized = new Mat();
        Cv2.Resize(src, resized, new Size(targetWidth, targetHeight), 0, 0, interpFlag);

        // Encode to PNG
        Cv2.ImEncode(".png", resized, out var pngBytes);

        // Metadata
        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "resize",
            ["originalSize"] = $"{originalWidth}x{originalHeight}",
            ["newSize"] = $"{targetWidth}x{targetHeight}",
            ["targetDPI"] = targetDPI.ToString(),
            ["documentType"] = documentType,
            ["interpolation"] = interpolation,
            ["maintainAspectRatio"] = maintainAspectRatio.ToString(),
        };

        // Save with unique reference
        var baseRef = $"{imageReference}_resize_{targetWidth}x{targetHeight}";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);

        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(
            (Func<string, int, int, int, bool, string, string, Task<string>>)this.ResizeImage
        );
    }
}
