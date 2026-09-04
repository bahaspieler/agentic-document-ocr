using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.Json;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Enhancement;

/// <summary>
/// Normalizes uneven illumination and shadows in document images.
/// </summary>
public sealed class IlluminationNormalizePlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    /// <summary>
    /// Creates an illumination normalization plugin.
    /// </summary>
    /// <param name="store">Image store used to load and save images.</param>
    public IlluminationNormalizePlugin(IImageStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Estimates and removes uneven illumination from an image.
    /// </summary>
    /// <param name="imageReference">Reference of the image to normalize.</param>
    /// <param name="kernelSize">Odd Gaussian kernel size used to estimate illumination.</param>
    /// <param name="strength">Blend strength between the source and normalized images.</param>
    /// <param name="qualityProfile">Optional quality profile used to skip already-uniform images.</param>
    /// <returns>The source reference when skipped; otherwise, the normalized image reference.</returns>
    [Description("Removes shadows and normalizes uneven illumination from document images. Skips processing if illumination is already uniform. Returns the image reference (unchanged if skipped, or new reference).")]
    public Task<string> NormalizeIllumination(
        [Description("The reference number of the image to normalize.")] string imageReference,
        [Description("Gaussian blur kernel size for illumination estimation (must be odd, larger = smoother). Default 51.")] int kernelSize = 51,
        [Description("Blending strength between original and normalized (0.0-1.0). 1.0 = fully normalized.")] double strength = 1.0,
        [Description("Optional quality profile JSON from AssessQuality. Skips if illumination uniformity is already high.")] string? qualityProfile = null)
    {
        if (kernelSize < 3 || kernelSize % 2 == 0)
            throw new ArgumentException("kernelSize must be an odd number >= 3");
        strength = Math.Clamp(strength, 0.0, 1.0);

        if (!string.IsNullOrEmpty(qualityProfile))
        {
            try
            {
                using var doc = JsonDocument.Parse(qualityProfile);
                if (doc.RootElement.TryGetProperty("illuminationUniformity", out var u) && u.GetDouble() > 0.90)
                    return Task.FromResult(imageReference);
            }
            catch { }
        }

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImDecode(File.ReadAllBytes(inputPath), ImreadModes.Color);
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        using var grayF = new Mat();
        gray.ConvertTo(grayF, MatType.CV_64F);

        using var illumination = new Mat();
        Cv2.GaussianBlur(grayF, illumination, new Size(kernelSize, kernelSize), 0);

        using var denom = new Mat();
        Cv2.Add(illumination, Scalar.All(1.0), denom);

        using var normalized = new Mat();
        Cv2.Divide(grayF, denom, normalized, 255.0);
        Cv2.Min(normalized, Scalar.All(255.0), normalized);
        Cv2.Max(normalized, Scalar.All(0.0), normalized);

        using var result = new Mat();
        if (strength < 1.0)
        {
            Cv2.AddWeighted(normalized, strength, grayF, 1.0 - strength, 0, result);
        }
        else
        {
            normalized.CopyTo(result);
        }

        using var result8u = new Mat();
        result.ConvertTo(result8u, MatType.CV_8UC1);

        using var output = new Mat();
        Cv2.CvtColor(result8u, output, ColorConversionCodes.GRAY2BGR);

        Cv2.ImEncode(".png", output, out var pngBytes);

        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "illumination_normalize",
            ["kernelSize"] = kernelSize.ToString(),
            ["strength"] = strength.ToString("F2")
        };

        var desiredRef = PluginHelpers.MakeUniqueRef(_store, $"{imageReference}_illum_norm");
        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    /// <summary>
    /// Gets the AI-callable illumination normalization tool.
    /// </summary>
    /// <returns>The tools exposed by this plugin.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.NormalizeIllumination);
    }
}
