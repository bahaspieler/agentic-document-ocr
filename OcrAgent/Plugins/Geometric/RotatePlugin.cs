using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Geometric;

/// <summary>
/// Generates one PNG per angle in a range by rotating around the image center with an expanded canvas.
/// </summary>
public sealed class RotatePlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    /// <summary>Creates the plugin with the image store used to resolve and save images.</summary>
    /// <param name="store">Backing store for input paths and output bytes.</param>
    public RotatePlugin(IImageStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Saves up to <see cref="PluginHelpers.MaxOutputs"/> rotated variants as separate references.
    /// </summary>
    /// <param name="imageReference">Stored image id to read.</param>
    /// <param name="startAngle">First angle in degrees (inclusive).</param>
    /// <param name="endAngle">Last angle in degrees (inclusive).</param>
    /// <param name="angleStep">Increment between angles; must be positive.</param>
    /// <returns>Comma-separated new references, one per angle.</returns>
    [Description("Rotates the given image by specified angles. It returns the comma-separated list of references of generated images.")]
    public Task<string> RotateImage(
        [Description("The reference number of the image to rotate.")] string imageReference,
        [Description("The starting angle in degrees (0-360).")] int startAngle,
        [Description("The ending angle in degrees (0-360).")] int endAngle,
        [Description("The angle step in degrees.")] int angleStep)
    {
        if (angleStep <= 0) throw new ArgumentException("angleStep must be > 0");
        if (endAngle < startAngle) throw new ArgumentException("endAngle must be >= startAngle");

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImDecode(File.ReadAllBytes(inputPath), ImreadModes.Color);

        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        int w = src.Width;
        int h = src.Height;
        var center = new Point2f(w / 2.0f, h / 2.0f);
        var results = new List<string>();

        for (int angle = startAngle; angle <= endAngle; angle += angleStep)
        {
            if (results.Count >= PluginHelpers.MaxOutputs) break;

            // Normalize angle to 0-360 range
            int normalizedAngle = angle % 360;
            if (normalizedAngle < 0) normalizedAngle += 360;

            // Get rotation matrix
            using var rotationMatrix = Cv2.GetRotationMatrix2D(center, normalizedAngle, 1.0);

            // Calculate new bounding box to avoid cropping
            double cos = Math.Abs(rotationMatrix.At<double>(0, 0));
            double sin = Math.Abs(rotationMatrix.At<double>(0, 1));

            int newW = (int)(h * sin + w * cos);
            int newH = (int)(h * cos + w * sin);

            // Adjust translation
            rotationMatrix.Set(0, 2, rotationMatrix.At<double>(0, 2) + (newW / 2.0) - center.X);
            rotationMatrix.Set(1, 2, rotationMatrix.At<double>(1, 2) + (newH / 2.0) - center.Y);

            using var rotated = new Mat();
            Cv2.WarpAffine(src, rotated, rotationMatrix, new Size(newW, newH), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.White);

            // Encode to PNG bytes
            Cv2.ImEncode(".png", rotated, out var pngBytes);

            var meta = new Dictionary<string, string>
            {
                ["parent"] = imageReference,
                ["op"] = "rotate",
                ["angleDegrees"] = normalizedAngle.ToString()
            };
            
            var baseRef = $"{imageReference}_rotate_{normalizedAngle:D3}";
            var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);

            var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
            results.Add(newRef);
        }

        return Task.FromResult(string.Join(",", results));
    }

    /// <summary>Exposes <see cref="RotateImage"/> as an AI-callable tool.</summary>
    /// <returns>Sequence containing the RotateImage tool.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.RotateImage);
    }
}
