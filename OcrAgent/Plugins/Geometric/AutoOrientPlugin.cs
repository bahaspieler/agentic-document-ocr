using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Geometric;

/// <summary>
/// Detects document orientation via Tesseract OSD (PSM 0) and rotates
/// to upright position. Handles 90/180/270 degree rotations.
/// </summary>
public sealed class AutoOrientPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;
    private readonly string? _tesseractPath;

    public AutoOrientPlugin(IImageStore store, string? tesseractPath = null)
    {
        _store = store;
        _tesseractPath = tesseractPath ?? FindTesseract();
    }

    [Description("Auto-detects document orientation using Tesseract OSD and rotates to upright. Fixes 90/180/270 degree rotations.")]
    public Task<string> AutoOrient(
        [Description("The reference number of the image to orient.")] string imageReference)
    {
        if (_tesseractPath == null)
            return Task.FromResult(imageReference);

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference}");

        int rotation = DetectOrientation(inputPath);

        if (rotation == 0)
        {
            Cv2.ImEncode(".png", src, out var origBytes);
            var skipMeta = new Dictionary<string, string>
            {
                ["parent"] = imageReference,
                ["op"] = "auto_orient",
                ["detectedRotation"] = "0",
                ["note"] = "already upright"
            };
            var skipRef = PluginHelpers.MakeUniqueRef(_store, $"{imageReference}_oriented");
            return Task.FromResult(_store.SaveBytes(origBytes, "png", skipMeta, skipRef));
        }

        using var rotated = RotateExact(src, rotation);
        Cv2.ImEncode(".png", rotated, out var pngBytes);

        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "auto_orient",
            ["detectedRotation"] = rotation.ToString(),
            ["correctionApplied"] = $"-{rotation} degrees"
        };
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, $"{imageReference}_oriented");
        return Task.FromResult(_store.SaveBytes(pngBytes, "png", meta, desiredRef));
    }

    private int DetectOrientation(string imagePath)
    {
        try
        {
            var args = $"\"{imagePath}\" stdout --psm 0";
            var psi = new ProcessStartInfo(_tesseractPath!, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return 0;

            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(10000);

            // Tesseract OSD outputs to stderr:
            // "Orientation in degrees: 90"
            // "Rotate: 270"
            var rotateMatch = Regex.Match(stderr, @"Rotate:\s*(\d+)");
            if (rotateMatch.Success && int.TryParse(rotateMatch.Groups[1].Value, out int rotate))
            {
                return rotate switch
                {
                    90 or 180 or 270 => rotate,
                    _ => 0
                };
            }

            var orientMatch = Regex.Match(stderr, @"Orientation in degrees:\s*(\d+)");
            if (orientMatch.Success && int.TryParse(orientMatch.Groups[1].Value, out int orient))
            {
                // "Orientation in degrees" = how much it IS rotated
                // We need the inverse to correct it
                return orient switch
                {
                    90 => 270,
                    180 => 180,
                    270 => 90,
                    _ => 0
                };
            }
        }
        catch
        {
            // OSD can fail on very small or noisy images
        }

        return 0;
    }

    /// <summary>Rotate by exact 90/180/270 degrees without quality loss.</summary>
    private static Mat RotateExact(Mat src, int degrees)
    {
        return degrees switch
        {
            90 => RotateClockwise90(src),
            180 => Rotate180(src),
            270 => RotateCounterClockwise90(src),
            _ => src.Clone()
        };
    }

    private static Mat RotateClockwise90(Mat src)
    {
        var dst = new Mat();
        Cv2.Transpose(src, dst);
        Cv2.Flip(dst, dst, FlipMode.Y);
        return dst;
    }

    private static Mat RotateCounterClockwise90(Mat src)
    {
        var dst = new Mat();
        Cv2.Transpose(src, dst);
        Cv2.Flip(dst, dst, FlipMode.X);
        return dst;
    }

    private static Mat Rotate180(Mat src)
    {
        var dst = new Mat();
        Cv2.Flip(src, dst, FlipMode.XY);
        return dst;
    }

    private static string? FindTesseract()
    {
        var candidates = new[] { "tesseract", "/opt/homebrew/bin/tesseract", "/usr/bin/tesseract", "/usr/local/bin/tesseract" };
        foreach (var candidate in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo(candidate, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
                if (proc is { ExitCode: 0 }) return candidate;
            }
            catch { }
        }
        return null;
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.AutoOrient);
    }
}
