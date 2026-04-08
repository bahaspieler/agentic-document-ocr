using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Diagnostics;
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

    /// <summary>Creates the plugin with the image store used to resolve and save images.</summary>
    /// <param name="store">Backing store for input paths and output bytes.</param>
    public ResizePlugin(IImageStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Resizes by explicit size, DPI-derived size, or document preset; records size and DPI hints in metadata.
    /// </summary>
    /// <param name="imageReference">Stored image id to read.</param>
    /// <param name="targetWidth">Target width in pixels; 0 with height for aspect fill.</param>
    /// <param name="targetHeight">Target height in pixels; 0 with width for aspect fill.</param>
    /// <param name="targetDPI">When &gt; 0 and preset is auto, scales from assumed physical size.</param>
    /// <param name="maintainAspectRatio">Fills missing dimension from the other when true.</param>
    /// <param name="interpolation"><c>linear</c>, <c>cubic</c>, or <c>lanczos4</c>.</param>
    /// <param name="documentType">Preset name or <c>auto</c> to use width/height/DPI only.</param>
    /// <param name="skipIfLarger">When true, skips resize if the source already meets target dimensions.</param>
    /// <returns>New image reference for the resized or skipped copy.</returns>
    [Description("Resizes image to normalize dimensions. Use targetDPI for document standardization (300 recommended for OCR). Can specify pixels or use document type presets.")]
    public Task<string> ResizeImage(
        [Description("The reference number of the image to resize.")] string imageReference,
        [Description("Target width in pixels (0 to auto-calculate from height/aspect ratio).")] int targetWidth = 0,
        [Description("Target height in pixels (0 to auto-calculate from width/aspect ratio).")] int targetHeight = 0,
        [Description("Target DPI for document (e.g., 300 for high-quality OCR, 200 for receipts). Overrides width/height if > 0.")] int targetDPI = 0,
        [Description("Maintain aspect ratio when one dimension is 0 (recommended: true).")] bool maintainAspectRatio = true,
        [Description("Interpolation method: 'linear' (fast), 'cubic' (balanced), 'lanczos4' (for upscaling).")] string interpolation = "lanczos4",
        [Description("Document type preset: 'auto', 'receipt', 'form', 'statement', 'invoice'. Overrides other dimensions if specified.")] string documentType = "auto",
        [Description("If true, only resize when the image is smaller than target. Prevents quality loss from upscaling.")] bool skipIfLarger = false)
    {
        interpolation = interpolation.ToLowerInvariant();
        documentType = documentType.ToLowerInvariant();

        if (interpolation != "linear" && interpolation != "cubic" && interpolation != "lanczos4")
            throw new ArgumentException("interpolation must be 'linear', 'cubic', or 'lanczos4'");

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImDecode(File.ReadAllBytes(inputPath), ImreadModes.Color);

        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        int originalWidth = src.Width;
        int originalHeight = src.Height;
        int effectiveDPI = targetDPI;

        if (documentType != "auto")
        {
            (targetWidth, targetHeight, effectiveDPI) = GetDocumentPreset(documentType, originalWidth, originalHeight);
        }
        else if (targetDPI > 0)
        {
            (targetWidth, targetHeight) = CalculateDimensionsFromDPI(targetDPI, originalWidth, originalHeight, documentType);
        }
        else
        {
            if (targetWidth == 0 && targetHeight == 0)
                throw new ArgumentException("Must specify either targetWidth, targetHeight, targetDPI, or documentType");

            if (targetWidth < 0 || targetHeight < 0)
                throw new ArgumentException("Dimensions must be >= 0");
        }

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

        targetWidth = Math.Max(1, targetWidth);
        targetHeight = Math.Max(1, targetHeight);

        if (skipIfLarger && originalHeight >= targetHeight && originalWidth >= targetWidth)
        {
            effectiveDPI = EstimateCurrentDpi(originalWidth, originalHeight, documentType);
            Debug.WriteLine($"[Resize] Image already large enough ({originalWidth}x{originalHeight}), skipping resize. effectiveDPI={effectiveDPI}");

            var skipMeta = new Dictionary<string, string>
            {
                ["parent"] = imageReference,
                ["op"] = "resize",
                ["skipped"] = "true",
                ["skipReason"] = "image already at or above target dimensions",
                ["originalSize"] = $"{originalWidth}x{originalHeight}",
                ["effectiveDPI"] = effectiveDPI.ToString(),
                ["documentType"] = documentType,
            };
            var skipRef = PluginHelpers.MakeUniqueRef(_store, $"{imageReference}_resize_skipped");
            Cv2.ImEncode(".png", src, out var skipBytes);
            var savedSkipRef = _store.SaveBytes(skipBytes, "png", skipMeta, skipRef);
            return Task.FromResult(savedSkipRef);
        }

        var interpFlag = interpolation switch
        {
            "linear" => InterpolationFlags.Linear,
            "cubic" => InterpolationFlags.Cubic,
            "lanczos4" => InterpolationFlags.Lanczos4,
            _ => InterpolationFlags.Lanczos4
        };

        using var resized = new Mat();
        Cv2.Resize(src, resized, new Size(targetWidth, targetHeight), 0, 0, interpFlag);

        if (effectiveDPI == 0 && targetHeight > 0 && originalHeight > 0)
            effectiveDPI = (int)(targetDPI > 0 ? targetDPI : 300.0 * targetHeight / originalHeight);

        Cv2.ImEncode(".png", resized, out var pngBytes);

        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "resize",
            ["originalSize"] = $"{originalWidth}x{originalHeight}",
            ["newSize"] = $"{targetWidth}x{targetHeight}",
            ["targetDPI"] = targetDPI.ToString(),
            ["effectiveDPI"] = effectiveDPI.ToString(),
            ["documentType"] = documentType,
            ["interpolation"] = interpolation,
            ["maintainAspectRatio"] = maintainAspectRatio.ToString(),
        };

        var baseRef = $"{imageReference}_resize_{targetWidth}x{targetHeight}";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);

        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    private static (int width, int height, int effectiveDPI) GetDocumentPreset(
        string documentType, int originalWidth, int originalHeight)
    {
        return documentType switch
        {
            "receipt" => GetReceiptDimensions(originalWidth, originalHeight),
            "form" => (2550, 3300, 300),
            "statement" => (2550, 3300, 300),
            "invoice" => (2550, 3300, 300),
            "letter" => (2550, 3300, 300),
            "a4" => (2480, 3508, 300),
            _ => throw new ArgumentException($"Unknown document type: {documentType}. Use 'receipt', 'form', 'statement', 'invoice', 'letter', or 'a4'.")
        };
    }

    private static (int width, int height, int effectiveDPI) GetReceiptDimensions(int originalWidth, int originalHeight)
    {
        const int targetDpi = 300;
        double aspect = (double)originalWidth / Math.Max(originalHeight, 1);

        // Thermal receipts are typically ~3.15 inches wide.
        // Use height-only target: 300 DPI * estimated height, preserving aspect ratio.
        double widthInches = 3.15;
        int w = (int)(widthInches * targetDpi);
        int h = (int)(w / aspect);

        // Clamp height to reasonable range (avoid absurdly tall images from very long receipts)
        h = Math.Clamp(h, 800, 6000);
        w = (int)(h * aspect);

        return (w, h, targetDpi);
    }

    private static int EstimateCurrentDpi(int width, int height, string documentType)
    {
        double widthInches = documentType == "receipt" ? 3.15 : 8.27;
        return (int)(width / widthInches);
    }

    private static (int width, int height) CalculateDimensionsFromDPI(
        int dpi, int originalWidth, int originalHeight, string documentType)
    {
        double widthInches, heightInches;

        if (documentType == "receipt")
        {
            widthInches = 3.15;
            heightInches = widthInches * originalHeight / Math.Max(originalWidth, 1);
        }
        else
        {
            widthInches = originalWidth / 72.0;
            heightInches = originalHeight / 72.0;

            if (widthInches > 12 || heightInches > 18)
            {
                widthInches = 8.27;
                heightInches = 11.69;
            }
        }

        int targetWidth = (int)(widthInches * dpi);
        int targetHeight = (int)(heightInches * dpi);

        return (targetWidth, targetHeight);
    }

    /// <summary>Exposes <see cref="ResizeImage"/> as an AI-callable tool.</summary>
    /// <returns>Sequence containing the ResizeImage tool.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.ResizeImage);
    }
}
