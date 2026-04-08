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
        using var src = Cv2.ImDecode(File.ReadAllBytes(inputPath), ImreadModes.Color);
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference}");

        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        double blurScore = ComputeBlurScore(gray);
        double noiseSigma = EstimateNoiseSigma(gray);
        string noiseLevel = CategorizeNoise(noiseSigma);
        double meanBrightness = ComputeMeanBrightness(src);
        double contrast = ComputeContrast(gray);
        double illuminationUniformity = ComputeIlluminationUniformity(gray);
        bool isDarkBackground = meanBrightness < 127 && ComputeDarkPixelRatio(gray) > 0.60;
        double estimatedSkewDeg = EstimateSkew(gray);
        bool resolutionAdequate = Math.Min(src.Width, src.Height) >= 1000;

        var profile = new
        {
            blurScore = Math.Round(blurScore, 1),
            isBlurry = blurScore < 100,
            noiseLevel,
            noiseSigma = Math.Round(noiseSigma, 2),
            meanBrightness = Math.Round(meanBrightness, 1),
            contrast = Math.Round(contrast, 1),
            illuminationUniformity = Math.Round(illuminationUniformity, 3),
            isDarkBackground,
            estimatedSkewDeg = Math.Round(estimatedSkewDeg, 2),
            resolutionAdequate,
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

    private static double ComputeIlluminationUniformity(Mat gray)
    {
        const int gridSize = 4;
        int blockH = gray.Rows / gridSize;
        int blockW = gray.Cols / gridSize;
        if (blockH < 1 || blockW < 1) return 1.0;

        var blockMeans = new List<double>();
        for (int gy = 0; gy < gridSize; gy++)
        {
            for (int gx = 0; gx < gridSize; gx++)
            {
                var roi = new Rect(gx * blockW, gy * blockH, blockW, blockH);
                using var block = new Mat(gray, roi);
                var mean = Cv2.Mean(block);
                blockMeans.Add(mean[0]);
            }
        }

        double avg = blockMeans.Average();
        if (avg < 1e-6) return 1.0;
        double variance = blockMeans.Sum(m => (m - avg) * (m - avg)) / blockMeans.Count;
        double cv = Math.Sqrt(variance) / avg;

        return Math.Clamp(1.0 - cv, 0.0, 1.0);
    }

    private static double ComputeDarkPixelRatio(Mat gray)
    {
        int total = gray.Rows * gray.Cols;
        if (total == 0) return 0;

        int dark = 0;
        for (int y = 0; y < gray.Rows; y++)
            for (int x = 0; x < gray.Cols; x++)
                if (gray.At<byte>(y, x) < 127) dark++;

        return (double)dark / total;
    }

    private static double EstimateSkew(Mat gray)
    {
        using var edges = new Mat();
        Cv2.Canny(gray, edges, 50, 150);

        var lines = Cv2.HoughLinesP(edges, 1, Math.PI / 180, 100, 100, 10);
        if (lines.Length == 0) return 0.0;

        var angles = new List<double>();
        foreach (var line in lines)
        {
            double angle = Math.Atan2(line.P2.Y - line.P1.Y, line.P2.X - line.P1.X) * 180 / Math.PI;
            if (Math.Abs(angle) < 45)
                angles.Add(angle);
        }

        if (angles.Count == 0) return 0.0;
        angles.Sort();
        return angles[angles.Count / 2];
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.AssessQuality);
    }
}
