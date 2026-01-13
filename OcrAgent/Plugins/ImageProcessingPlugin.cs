using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins;

public sealed class ImageProcessingPlugin
{
    private readonly IImageStore _store;

    public ImageProcessingPlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Registers an image into the storage system and returns its reference ID. The image is provided as a local file path.")]
    public string RegisterImage(
        [Description("Local file path of the image to register.")] string filePath,
        [Description("Optional reference ID to use (e.g., 123ABC). If empty, a new reference is generated.")] string? desiredReference = null) 
    {
        return _store.SaveFromFile(filePath, desiredReference);
    }

    [Description("Performs zooming in of the given image. It returns the comma-separated list of references of generated images.")]
    public Task<string> ZoomInImage(
        [Description("The reference number of the image, which should be zoomed in.")] string imageReference,
        [Description("The first zoom in in percent.")] int startZoom,
        [Description("The The last zoom in in percent")] int endZoom,
        [Description("The delta zoom, which defines zoomin step.")] int zoomStep)
    {
        if (zoomStep <= 0) throw new ArgumentException("zoomStep must be > 0");
        if (endZoom < startZoom) throw new ArgumentException("endZoom must be >= startZoom");
        
        const int maxOutputs = 20;          // cap
        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);
        
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        int w = src.Width;
        int h = src.Height;
        var results = new List<string>();

        for (int z = startZoom; z <= endZoom; z += zoomStep)
        {
            if (results.Count >= maxOutputs) break;

            // zoom factor: 10%
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
            var desiredRef = MakeUniqueRef(baseRef);

            var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
            results.Add(newRef);
        }

        return Task.FromResult(string.Join(",", results));
    }

    private string MakeUniqueRef(string baseRef)
    {
        var candidate = baseRef;
        int i = 2;

        while (_store.Exists(candidate))
        {
            candidate = $"{baseRef}_{i:D2}";
            i++;
        }

        return candidate;
    }

    public IEnumerable<AITool> AsAITools()
    {
        yield return AIFunctionFactory.Create(this.RegisterImage);
        yield return AIFunctionFactory.Create(this.ZoomInImage);
    }
}