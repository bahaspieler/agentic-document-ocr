using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace OcrAgent.Inference;

/// <summary>
/// Runs the U2NETP deep learning model to produce a foreground probability mask (0..1).
/// The image is resized to 320x320, normalized and matched to the U2NETP rembg pipeline. 
/// The output mask is min-max normalized and scaled back to fit the original image size.
/// </summary>
public sealed class U2NetpOnnxPredictor : IDisposable
{
    /// <summary>
    /// Side length in pixels for the square fed to the model (width and height).
    /// </summary>
    public const int InputSize = 320;

    private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] Std  = { 0.229f, 0.224f, 0.225f };

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;
    private readonly object _runLock = new();

    private U2NetpOnnxPredictor(InferenceSession session, string inputName, string outputName)
    {
        _session = session;
        _inputName = inputName;
        _outputName = outputName;
    }

    /// <summary>
    /// Loads u2netp.onnx from disk and binds the session to its first input and output tensors.
    /// </summary>
    /// <param name="modelPath">Filesystem path to the ONNX model file.</param>
    /// <returns>Ready-to-use predictor for that file.</returns>
    public static U2NetpOnnxPredictor FromModelPath(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            throw new ArgumentException("modelPath is empty.");

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"u2netp.onnx not found: {modelPath}");

        var opts = new SessionOptions
        {
            InterOpNumThreads = 1,
            IntraOpNumThreads = Environment.ProcessorCount > 1 ? 2 : 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        var session = new InferenceSession(modelPath, opts);
        var inputName = session.InputMetadata.Keys.First();
        var outputName = session.OutputMetadata.Keys.First();

        return new U2NetpOnnxPredictor(session, inputName, outputName);
    }

    /// <summary>
    /// Creates predictor using U2NETP_ONNX_PATH env var or default Assets path.
    /// Returns null if model file cannot be found.
    /// </summary>
    /// <returns>Predictor instance, or null when no model exists at resolved paths.</returns>
    public static U2NetpOnnxPredictor? TryCreateDefault()
    {
        var env = Environment.GetEnvironmentVariable("U2NETP_ONNX_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return FromModelPath(env);

        var baseDir = AppContext.BaseDirectory;
        var p1 = Path.Combine(baseDir, "Assets", "Models", "u2netp.onnx");
        if (File.Exists(p1))
            return FromModelPath(p1);

        var p2 = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Models", "u2netp.onnx");
        if (File.Exists(p2))
            return FromModelPath(p2);

        return null;
    }

    /// <summary>
    /// Predicts a probability mask (CV_32FC1, values 0..1) at the original image size.
    /// </summary>
    /// <param name="bgrImage">Non-empty BGR image.</param>
    /// <returns>Single-channel float mask matching image dimensions.</returns>
    public Mat PredictProbabilityMask(Mat bgrImage)
    {
        if (bgrImage.Empty())
            throw new ArgumentException("Input image is empty.");

        int origW = bgrImage.Width;
        int origH = bgrImage.Height;

        using var rgb = new Mat();
        Cv2.CvtColor(bgrImage, rgb, ColorConversionCodes.BGR2RGB);

        using var resized = new Mat();
        Cv2.Resize(rgb, resized, new Size(InputSize, InputSize), 0, 0, InterpolationFlags.Area);

        var input = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });

        for (int y = 0; y < InputSize; y++)
        {
            for (int x = 0; x < InputSize; x++)
            {
                var p = resized.At<Vec3b>(y, x);
                input[0, 0, y, x] = (p.Item0 / 255f - Mean[0]) / Std[0];
                input[0, 1, y, x] = (p.Item1 / 255f - Mean[1]) / Std[1];
                input[0, 2, y, x] = (p.Item2 / 255f - Mean[2]) / Std[2];
            }
        }

        Tensor<float> outputTensor;

        lock (_runLock)
        {
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputName, input)
            };

            using var results = _session.Run(inputs);
            var outValue = results.First(v => v.Name == _outputName);
            outputTensor = outValue.AsTensor<float>().ToDenseTensor();
        }

        using var small = new Mat(InputSize, InputSize, MatType.CV_32FC1);

        if (outputTensor.Rank == 4 && outputTensor.Dimensions[1] == 1)
        {
            for (int y = 0; y < InputSize; y++)
                for (int x = 0; x < InputSize; x++)
                    small.Set(y, x, outputTensor[0, 0, y, x]);
        }
        else if (outputTensor.Rank == 3)
        {
            for (int y = 0; y < InputSize; y++)
                for (int x = 0; x < InputSize; x++)
                    small.Set(y, x, outputTensor[0, y, x]);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unexpected u2netp output shape: [{string.Join(",", outputTensor.Dimensions.ToArray())}]");
        }

        Cv2.MinMaxLoc(small, out double minV, out double maxV);
        double denom = Math.Max(1e-6, maxV - minV);

        using var norm = new Mat();
        small.ConvertTo(norm, MatType.CV_32FC1, 1.0 / denom, -minV / denom);

        var probMask = new Mat();
        Cv2.Resize(norm, probMask, new Size(origW, origH), 0, 0, InterpolationFlags.Linear);

        Cv2.Min(probMask, 1.0, probMask);
        Cv2.Max(probMask, 0.0, probMask);

        return probMask;
    }

    /// <summary>
    /// Disposes the ONNX inference session.
    /// </summary>
    public void Dispose()
    {
        _session.Dispose();
    }
}
