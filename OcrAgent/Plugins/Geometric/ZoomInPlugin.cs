using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Geometric;

/// <summary>
/// Plugin for zooming into images by cropping and resizing.
/// Produces multiple zoom variants based on percentage ranges.
/// </summary>
public sealed class ZoomInPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    /// <summary>Creates the plugin with the image store used to resolve and save images.</summary>
    /// <param name="store">Backing store for input paths and output bytes.</param>
    public ZoomInPlugin(IImageStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Center-crops by a zoom-derived ratio, resizes back to the original size, saves each step as PNG.
    /// </summary>
    /// <param name="imageReference">Stored image id to read.</param>
    /// <param name="startZoom">First zoom percent (e.g. 10 → 1.1× effective scale).</param>
    /// <param name="endZoom">Last zoom percent (inclusive).</param>
    /// <param name="zoomStep">Step between percent values; must be positive.</param>
    /// <returns>Comma-separated new references, one per zoom level.</returns>
    [Description("Performs zooming in of the given image. It returns the comma-separated list of references of generated images.")]
    public Task<string> ZoomInImage(
        [Description("The reference number of the image, which should be zoomed in.")] string imageReference,
        [Description("The first zoom in in percent.")] int startZoom,
        [Description("The last zoom in in percent")] int endZoom,
        [Description("The delta zoom, which defines zoom-in step.")] int zoomStep)
    {
        if (zoomStep <= 0) throw new ArgumentException("zoomStep must be > 0");
        if (endZoom < startZoom) throw new ArgumentException("endZoom must be >= startZoom");
        
        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);
        
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        int w = src.Width;
        int h = src.Height;
        var results = new List<string>();

        for (int z = startZoom; z <= endZoom; z += zoomStep)
        {
            if (results.Count >= PluginHelpers.MaxOutputs) break;

            // zoom factor: e.g., 10% zoom = 1.1x
            double factor = 1.0 + (z / 100.0);
            double cropRatio = 1.0 / factor;

            int cropW = Math.Max(1, (int)(w * cropRatio));
            int cropH = Math.Max(1, (int)(h * cropRatio));

            int x = (w - cropW) / 2;
            int y = (h - cropH) / 2;

            var roi = new Rect(x, y, cropW, cropH);

            using var cropped = new Mat(src, roi);
            using var resized = new Mat();
            Cv2.Resize(cropped, resized, new Size(w, h), 0, 0, InterpolationFlags.Linear);

            // encode to PNG bytes
            Cv2.ImEncode(".png", resized, out var pngBytes);

            var meta = new Dictionary<string, string>
            {
                ["parent"] = imageReference,
                ["op"] = "zoom_in",
                ["zoomPercent"] = z.ToString()
            };
            
            var baseRef = $"{imageReference}_zoomIn_{z:D3}";
            var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);

            var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
            results.Add(newRef);
        }

        return Task.FromResult(string.Join(",", results));
    }

    /// <summary>Exposes <see cref="ZoomInImage"/> as an AI-callable tool.</summary>
    /// <returns>Sequence containing the ZoomInImage tool.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.ZoomInImage);
    }
}
