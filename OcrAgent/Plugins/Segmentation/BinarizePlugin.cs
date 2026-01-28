using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Segmentation;

public sealed class BinarizePlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public BinarizePlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Converts image to binary (black/white) for OCR.")]
    public Task<string> BinarizeImage(
        [Description("The reference number of the image to binarize.")] string imageReference,
        [Description("Binarization method: 'otsu' (automatic threshold), 'adaptive_mean' (local mean), 'adaptive_gaussian' (weighted local threshold).")] string method = "otsu",
        [Description("Block size for adaptive methods (must be odd number, e.g., 11, 15, 21). Larger values = smoother result.")] int blockSize = 11,
        [Description("Constant subtracted from calculated threshold (0-10). Higher = darker result.")] int constant = 2,
        [Description("Invert colors for dark backgrounds (some receipts/forms have dark backgrounds).")] bool invertForDarkBackground = false)
    
    {
        method = method.ToLowerInvariant();
        if (method != "otsu" && method != "adaptive_mean" && method != "adaptive_gaussian")
            throw new ArgumentException("method must be 'otsu', 'adaptive_mean', or 'adaptive_gaussian'");

        if (method.StartsWith("adaptive"))
        {
            if (blockSize < 3 || blockSize % 2 == 0)
                throw new ArgumentException("blockSize must be an odd number >= 3 for adaptive methods");
            
            if (constant < 0 || constant > 20)
                throw new ArgumentException("constant must be between 0 and 20");
        }

        throw new NotImplementedException();
    }
    var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);

        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        // grayscale
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        using var binary = new Mat();

        switch (method)
        {
            case "otsu":
                ApplyOtsuBinarization(gray, binary);
                break;

            case "adaptive_mean":
                ApplyAdaptiveMean(gray, binary, blockSize, constant);
                break;

            case "adaptive_gaussian":
                ApplyAdaptiveGaussian(gray, binary, blockSize, constant);
                break;
        }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.BinarizeImage);
    }
}
