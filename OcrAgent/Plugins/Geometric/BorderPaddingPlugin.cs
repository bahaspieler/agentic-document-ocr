using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Geometric;

/// <summary>
/// Adds a solid-color border around images. Tesseract requires at least a few pixels of
/// border around text to function correctly -- after perspective correction and cropping,
/// receipts often have zero border.
/// </summary>
public sealed class BorderPaddingPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    /// <summary>
    /// Creates a border padding plugin.
    /// </summary>
    /// <param name="store">Image store used to load and save images.</param>
    public BorderPaddingPlugin(IImageStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Adds a solid border around an image.
    /// </summary>
    /// <param name="imageReference">Reference of the image to pad.</param>
    /// <param name="pixels">Border width in pixels on each side.</param>
    /// <param name="color">Border color, either <c>white</c> or <c>black</c>.</param>
    /// <returns>The reference of the padded image.</returns>
    [Description("Adds a solid border around the image. Tesseract needs at least ~10px of border to avoid clipping text at edges.")]
    public Task<string> AddBorderPadding(
        [Description("The reference number of the image to pad.")] string imageReference,
        [Description("Border size in pixels on each side.")] int pixels = 10,
        [Description("Border color: 'white' or 'black'.")] string color = "white")
    {
        pixels = Math.Clamp(pixels, 1, 100);
        color = color.ToLowerInvariant();

        var borderColor = color == "black" ? Scalar.All(0) : Scalar.White;

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImDecode(File.ReadAllBytes(inputPath), ImreadModes.Color);
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference}");

        using var padded = new Mat();
        Cv2.CopyMakeBorder(src, padded, pixels, pixels, pixels, pixels, BorderTypes.Constant, borderColor);

        Cv2.ImEncode(".png", padded, out var pngBytes);

        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "border_padding",
            ["pixels"] = pixels.ToString(),
            ["color"] = color,
            ["originalSize"] = $"{src.Width}x{src.Height}",
            ["newSize"] = $"{padded.Width}x{padded.Height}",
        };

        var baseRef = $"{imageReference}_border_{pixels}px";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);
        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    /// <summary>
    /// Gets the AI-callable border padding tool.
    /// </summary>
    /// <returns>The tools exposed by this plugin.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.AddBorderPadding);
    }
}
