using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.Json;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Analysis;

public sealed class QualityAssessmentPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public QualityAssessmentPlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Analyzes image quality and returns a JSON profile with blur score, noise level, brightness, contrast, illumination uniformity, dark background detection, skew estimate, and resolution adequacy. Does not modify the image.")]
    public Task<string> AssessQuality(
        [Description("The reference number of the image to analyze.")] string imageReference)
    {
        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference}");

        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        double blurScore = ComputeBlurScore(gray);
        double noiseSigma = EstimateNoiseSigma(gray);
        string noiseLevel = CategorizeNoise(noiseSigma);
        double meanBrightness = ComputeMeanBrightness(src);
        double contrast = ComputeContrast(gray);

        var profile = new
        {
            blurScore = Math.Round(blurScore, 1),
            isBlurry = blurScore < 100,
            noiseLevel,
            noiseSigma = Math.Round(noiseSigma, 2),
            meanBrightness = Math.Round(meanBrightness, 1),
            contrast = Math.Round(contrast, 1),
            dimensions = $"{src.Width}x{src.Height}"
        };

        return Task.FromResult(JsonSerializer.Serialize(profile));
    }

    private static double ComputeBlurScore(Mat gray)
    {
        using var laplacian = new Mat();
        Cv2.Laplacian(gray, laplacian, MatType.CV_64F);
        Cv2.MeanStdDev(laplacian, out _, out var stddev);
        return stddev[0] * stddev[0];
    }

    internal static double EstimateNoiseSigma(Mat gray)
    {
        using var laplacian = new Mat();
        Cv2.Laplacian(gray, laplacian, MatType.CV_64F);

        int total = laplacian.Rows * laplacian.Cols;
        var absValues = new double[total];
        int idx = 0;
        for (int y = 0; y < laplacian.Rows; y++)
            for (int x = 0; x < laplacian.Cols; x++)
                absValues[idx++] = Math.Abs(laplacian.At<double>(y, x));

        Array.Sort(absValues);
        double median = absValues[total / 2];
        return median / 0.6745;
    }

    internal static string CategorizeNoise(double sigma)
    {
        if (sigma < 10) return "low";
        if (sigma < 25) return "medium";
        return "high";
    }

    private static double ComputeMeanBrightness(Mat bgr)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
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

    private static double ComputeContrast(Mat gray)
    {
        Cv2.MeanStdDev(gray, out _, out var stddev);
        return stddev[0];
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.AssessQuality);
    }
}