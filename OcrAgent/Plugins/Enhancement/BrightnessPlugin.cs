using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;

namespace OcrAgent.Plugins.Enhancement;

public sealed class BrightnessPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public BrightnessPlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Adjusts the brightness of the given image. It returns the comma-separated list of references of generated images.")]
    public Task<string> AdjustBrightness(
        [Description("The reference number of the image to adjust.")] string imageReference,
        [Description("The starting brightness adjustment (-100 to +100).")] int startBrightness,
        [Description("The ending brightness adjustment (-100 to +100).")] int endBrightness,
        [Description("The brightness adjustment step.")] int step)
    {
        if (step <= 0) throw new ArgumentException("step must be > 0");
        if (endBrightness < startBrightness) throw new ArgumentException("endBrightness must be >= startBrightness");
        if (startBrightness < -100 || startBrightness > 100) throw new ArgumentException("startBrightness must be in range -100 to 100");
        if (endBrightness < -100 || endBrightness > 100) throw new ArgumentException("endBrightness must be in range -100 to 100");
        
        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);
        
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");
        
        // var results = new List<string>();

        for (int brightness = startBrightness; brightness <= endBrightness; brightness += step)
        {
            if (results.Count >= PluginHelpers.MaxOutputs) break;

            using var adjusted = new Mat();
            
            // Alpha=1.0 for fixed contrast, beta adds brightness
            double beta = brightness * 2.55; // Scale -100 / 100 to 0-255
            src.ConvertTo(adjusted, MatType.CV_8UC3, 1.0, beta);

            // Encode to PNG
            Cv2.ImEncode(".png", adjusted, out var pngBytes);

            var meta = new Dictionary<string, string>
            {
                ["parent"] = imageReference,
                ["op"] = "brightness",
                ["adjustment"] = brightness.ToString()
            };
            
            var baseRef = $"{imageReference}_brightness_{(brightness >= 0 ? "p" : "m")}{Math.Abs(brightness):D3}";
            var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);

            var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
            results.Add(newRef);
        }
        
        return Task.FromResult(string.Empty);
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.AdjustBrightness);
    }
}