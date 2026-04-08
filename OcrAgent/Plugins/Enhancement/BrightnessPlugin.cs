using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Enhancement;

/// <summary>
/// Plugin for adjusting image brightness.
/// Generates multiple brightness variants based on adjustment ranges.
/// </summary>
public sealed class BrightnessPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    /// <summary>
    /// Creates the plugin with the image store used to load inputs and save outputs.
    /// </summary>
    /// <param name="store">Image storage implementation.</param>
    public BrightnessPlugin(IImageStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Saves PNG variants with brightness offsets from <paramref name="startBrightness"/> to <paramref name="endBrightness"/> in steps of <paramref name="step"/>.
    /// </summary>
    /// <param name="imageReference">Input image reference.</param>
    /// <param name="startBrightness">First offset in [-100, 100].</param>
    /// <param name="endBrightness">Last offset in [-100, 100].</param>
    /// <param name="step">Positive step between offsets.</param>
    /// <returns>Comma-separated references of saved images.</returns>
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
        using var src = Cv2.ImDecode(File.ReadAllBytes(inputPath), ImreadModes.Color);
        
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        var results = new List<string>();

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

        return Task.FromResult(string.Join(",", results));
    }

    /// <summary>
    /// Nudges mean luminance toward ~150 when it falls outside the skip window; otherwise returns the input reference.
    /// </summary>
    /// <param name="imageReference">Input image reference.</param>
    /// <param name="qualityProfile">Optional JSON with meanBrightness from quality assessment.</param>
    /// <returns>Same reference if skipped, or a new reference for the adjusted image.</returns>
    [Description("Automatically normalizes image brightness to the optimal range for OCR (~150 median luminance). Skips if brightness is already adequate. Returns the image reference (unchanged if skipped, or new reference if adjusted).")]
    public Task<string> AutoAdjustBrightness(
        [Description("The reference number of the image to adjust.")] string imageReference,
        [Description("Optional quality profile JSON from AssessQuality. Uses pre-computed brightness data if available.")] string? qualityProfile = null)
    {
        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImDecode(File.ReadAllBytes(inputPath), ImreadModes.Color);
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        double currentBrightness = GetMeanBrightness(src, qualityProfile);

        if (currentBrightness >= 130 && currentBrightness <= 170)
            return Task.FromResult(imageReference);

        const double targetBrightness = 150.0;
        double beta = targetBrightness - currentBrightness;

        using var adjusted = new Mat();
        src.ConvertTo(adjusted, MatType.CV_8UC3, 1.0, beta);

        Cv2.ImEncode(".png", adjusted, out var pngBytes);
        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "auto_brightness",
            ["originalBrightness"] = currentBrightness.ToString("F1"),
            ["adjustment"] = beta.ToString("F1")
        };
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, $"{imageReference}_auto_brightness");
        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    private static double GetMeanBrightness(Mat src, string? qualityProfile)
    {
        if (!string.IsNullOrEmpty(qualityProfile))
        {
            try
            {
                using var doc = JsonDocument.Parse(qualityProfile);
                if (doc.RootElement.TryGetProperty("meanBrightness", out var mb))
                    return mb.GetDouble();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WARN] quality profile parse (meanBrightness): {ex.Message}");
            }
        }

        using var hsv = new Mat();
        Cv2.CvtColor(src, hsv, ColorConversionCodes.BGR2HSV);
        var channels = Cv2.Split(hsv);
        try
        {
            var mean = Cv2.Mean(channels[2]);
            return mean[0];
        }
        finally
        {
            foreach (var ch in channels) ch.Dispose();
        }
    }

    /// <summary>
    /// Registers brightness-related tools for the AI runtime.
    /// </summary>
    /// <returns>Tool bindings for this plugin.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.AdjustBrightness);
        yield return AIFunctionFactory.Create(this.AutoAdjustBrightness);
    }
}
