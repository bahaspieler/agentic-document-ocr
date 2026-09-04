using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using OcrAgent.Ocr;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Ocr;

/// <summary>
/// Tesseract OCR plugin with built-in binarization variants (natural, Otsu,
/// adaptive Gaussian, Sauvola) and multi-PSM scoring. When variant is "auto",
/// runs all applicable variants × PSM modes and returns the best-scoring result.
/// </summary>
public sealed class OcrPlugin : IImageProcessingTool
{
    private const int AdaptiveBlockSize = 15;
    private const int AdaptiveOffset = 4;
    private const int SauvolaWindowSize = 51;
    private const double SauvolaK = 0.2;
    private const double SauvolaR = 128;
    private const double IlluminationUniformityThreshold = 0.85;

    private readonly IImageStore _store;
    private readonly IOcrEngine _ocrEngine;

    #region Public API

    /// <summary>Creates the plugin with storage and the OCR engine used for recognition.</summary>
    /// <param name="store">Resolves image paths and output directories for sidecar files.</param>
    /// <param name="ocrEngine">Engine that performs Tesseract recognition.</param>
    public OcrPlugin(IImageStore store, IOcrEngine ocrEngine)
    {
        _store = store;
        _ocrEngine = ocrEngine;
    }

    /// <summary>
    /// Runs OCR on one binarization path or, in auto/multi mode, scores several variant×PSM runs and returns the best JSON.
    /// </summary>
    /// <param name="imageReference">Stored image id whose file path is passed to the engine.</param>
    /// <param name="language">Tesseract language code (e.g. eng).</param>
    /// <param name="pageSegMode">PSM name, <c>multi</c> to compare column vs block, or <c>auto</c> with variant auto.</param>
    /// <param name="variant">Binarization preset or <c>auto</c> to run multiple and pick by score.</param>
    /// <param name="dpi">DPI hint for layout; 0 lets the engine decide.</param>
    /// <param name="qualityProfile">Optional JSON from quality assessment; affects Sauvola inclusion in auto mode.</param>
    /// <param name="maxVariants">Caps how many binarization variants run in auto mode; 0 means no cap.</param>
    /// <returns>Serialized <see cref="OcrResult"/> JSON (single run or best candidate).</returns>
    [Description("Runs Tesseract OCR on an image. Supports binarization variants " +
                 "(natural, otsu, adaptive_gaussian, sauvola) and PSM modes. " +
                 "Use variant='auto' to try all variants with multi-PSM and return the best result.")]
    public async Task<string> RunOcr(
        [Description("The reference number of the image to extract text from.")] string imageReference,
        [Description("OCR language code (e.g. 'eng' for English).")] string language = "eng",
        [Description("Page segmentation mode: 'auto', 'single_column', 'single_block', 'single_line', 'single_word', or 'multi' (tries column+block and picks best).")] string pageSegMode = "auto",
        [Description("Binarization variant: 'natural' (no binarization), 'otsu', 'adaptive_gaussian', 'sauvola', or 'auto' (tries all and picks best).")] string variant = "natural",
        [Description("DPI hint for Tesseract (0 = auto-detect, 300 recommended after resize).")] int dpi = 0,
        [Description("Quality profile JSON from AssessQuality (used to decide Sauvola in auto mode).")] string? qualityProfile = null,
        [Description("Maximum number of variants to try in auto mode (0 = unlimited).")] int maxVariants = 0)
    {
        var inputPath = _store.GetImagePath(imageReference);
        var effectiveVariant = string.IsNullOrEmpty(variant) ? "natural" : variant.ToLowerInvariant();
        var effectivePsm = string.IsNullOrEmpty(pageSegMode) ? "auto" : pageSegMode.ToLowerInvariant();

        var configVars = new Dictionary<string, string>
        {
            ["load_system_dawg"] = "false",
            ["load_freq_dawg"] = "false",
        };

        if (effectiveVariant == "auto" || effectivePsm == "multi")
            return await RunMultiStrategy(imageReference, inputPath, effectiveVariant, effectivePsm, language, dpi, configVars, qualityProfile, maxVariants);

        var psm = ParsePageSegMode(effectivePsm);
        var options = new OcrOptions { Language = language, PageSegMode = psm, Dpi = dpi, ConfigVars = configVars };

        string ocrInputPath = inputPath;
        string? tempPath = null;
        try
        {
            if (effectiveVariant != "natural")
            {
                using var src = Cv2.ImDecode(File.ReadAllBytes(inputPath), ImreadModes.Color);
                tempPath = effectiveVariant switch
                {
                    "otsu" => ApplyOtsu(src, imageReference),
                    "adaptive_gaussian" => ApplyAdaptiveGaussian(src, imageReference),
                    "sauvola" => ApplySauvola(src, imageReference),
                    _ => null
                };
                if (tempPath != null) ocrInputPath = tempPath;
            }

            var result = await _ocrEngine.RecognizeAsync(ocrInputPath, options);
            SaveOcrResults(imageReference, result);
            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false });
        }
        finally
        {
            if (tempPath != null) try { File.Delete(tempPath); } catch (Exception ex) { Debug.WriteLine($"[WARN] delete temp binarized image after OCR: {ex.Message}"); }
        }
    }

    #endregion

    #region Multi-Strategy OCR

    private async Task<string> RunMultiStrategy(
        string imageReference, string inputPath,
        string variantMode, string psmMode,
        string language, int dpi,
        Dictionary<string, string> configVars,
        string? qualityProfile, int maxVariants)
    {
        using var src = Cv2.ImDecode(File.ReadAllBytes(inputPath), ImreadModes.Color);
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference}");

        var psms = psmMode == "multi" || variantMode == "auto"
            ? new[] { OcrPageSegMode.SingleColumn, OcrPageSegMode.SingleBlock }
            : new[] { ParsePageSegMode(psmMode) };

        bool tryAll = variantMode == "auto";
        bool needSauvola = tryAll && ShouldTrySauvola(qualityProfile);
        var candidates = new List<(string json, OcrScore score)>();
        var variantImages = new Dictionary<string, string>();
        int variantCount = 0;
        int variantLimit = maxVariants > 0 ? maxVariants : int.MaxValue;

        // C0: Natural (no binarization)
        if (tryAll || variantMode == "natural")
        {
            if (variantCount < variantLimit)
            {
                variantImages["natural"] = inputPath;
                await RunVariant(candidates, inputPath, "natural", psms, language, dpi, configVars);
                variantCount++;
            }
        }

        // C1: Otsu
        if (tryAll || variantMode == "otsu")
        {
            if (variantCount < variantLimit)
            {
                var path = ApplyOtsu(src, imageReference);
                if (path != null) { variantImages["otsu"] = path; await RunVariant(candidates, path, "otsu", psms, language, dpi, configVars); variantCount++; }
            }
        }

        // C2: Adaptive Gaussian
        if (tryAll || variantMode == "adaptive_gaussian")
        {
            if (variantCount < variantLimit)
            {
                var path = ApplyAdaptiveGaussian(src, imageReference);
                if (path != null) { variantImages["adaptive_gaussian"] = path; await RunVariant(candidates, path, "adaptive_gaussian", psms, language, dpi, configVars); variantCount++; }
            }
        }

        // C3: Sauvola (in auto mode, only if illumination is uneven)
        if (tryAll ? needSauvola : variantMode == "sauvola")
        {
            if (variantCount < variantLimit)
            {
                var path = ApplySauvola(src, imageReference);
                if (path != null) { variantImages["sauvola"] = path; await RunVariant(candidates, path, "sauvola", psms, language, dpi, configVars); variantCount++; }
            }
        }

        if (candidates.Count == 0)
            throw new InvalidOperationException("No OCR candidates were generated.");

        var best = candidates.OrderByDescending(c => c.score.Score).First();
        Debug.WriteLine($"[OCR] Best: variant={best.score.Variant} psm={best.score.Psm} " +
                        $"score={best.score.Score} words={best.score.WordCount} conf={best.score.AvgConfidence}");

        SaveCandidateScores(imageReference, candidates);
        SaveWinningResult(imageReference, best.json);
        SaveWinningVariantImage(imageReference, best.score.Variant, variantImages);

        foreach (var (name, path) in variantImages)
            if (name != "natural") try { File.Delete(path); } catch (Exception ex) { Debug.WriteLine($"[WARN] delete temporary variant image after multi-OCR: {ex.Message}"); }

        return best.json;
    }

    private async Task RunVariant(
        List<(string json, OcrScore score)> candidates,
        string imagePath, string variantName,
        OcrPageSegMode[] psms, string language, int dpi,
        Dictionary<string, string> configVars)
    {
        foreach (var psm in psms)
        {
            try
            {
                var options = new OcrOptions
                {
                    Language = language,
                    PageSegMode = psm,
                    Dpi = dpi,
                    ConfigVars = new Dictionary<string, string>(configVars),
                };
                var result = await _ocrEngine.RecognizeAsync(imagePath, options);
                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false });
                var score = OcrScoringPlugin.ComputeScore(json, variantName, psm.ToString());
                candidates.Add((json, score));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OCR] {variantName}/{psm} failed: {ex.Message}");
            }
        }
    }

    #endregion

    #region Binarization Variants

    private static string? ApplyOtsu(Mat src, string imageReference)
    {
        try
        {
            using var gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
            using var binary = new Mat();
            Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            var path = Path.Combine(Path.GetTempPath(), $"{imageReference}_otsu_{Guid.NewGuid():N}.png");
            Cv2.ImWrite(path, binary);
            return path;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WARN] Otsu binarization: {ex.Message}");
            return null;
        }
    }

    private static string? ApplyAdaptiveGaussian(Mat src, string imageReference)
    {
        try
        {
            using var gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
            using var binary = new Mat();
            Cv2.AdaptiveThreshold(gray, binary, 255,
                AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, AdaptiveBlockSize, AdaptiveOffset);
            var path = Path.Combine(Path.GetTempPath(), $"{imageReference}_adaptive_{Guid.NewGuid():N}.png");
            Cv2.ImWrite(path, binary);
            return path;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WARN] adaptive Gaussian binarization: {ex.Message}");
            return null;
        }
    }

    private static string? ApplySauvola(Mat src, string imageReference)
    {
        try
        {
            using var gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

            using var grayF = new Mat();
            gray.ConvertTo(grayF, MatType.CV_64F);
            using var mean = new Mat();
            Cv2.BoxFilter(grayF, mean, MatType.CV_64F, new Size(SauvolaWindowSize, SauvolaWindowSize));
            using var sqr = new Mat();
            Cv2.Multiply(grayF, grayF, sqr);
            using var meanSqr = new Mat();
            Cv2.BoxFilter(sqr, meanSqr, MatType.CV_64F, new Size(SauvolaWindowSize, SauvolaWindowSize));
            using var meanSquared = new Mat();
            Cv2.Multiply(mean, mean, meanSquared);
            using var variance = new Mat();
            Cv2.Subtract(meanSqr, meanSquared, variance);
            Cv2.Max(variance, 0, variance);
            using var stddev = new Mat();
            Cv2.Sqrt(variance, stddev);

            using var stdOverR = new Mat();
            stddev.ConvertTo(stdOverR, MatType.CV_64F, 1.0 / SauvolaR);
            using var factor = new Mat();
            // Sauvola: T = m * (1 + k * (σ/R - 1)); factor = 1 + k*(σ/R - 1), then threshold = m * factor
            Cv2.Subtract(stdOverR, Scalar.All(1.0), factor);
            Cv2.Multiply(factor, Scalar.All(SauvolaK), factor);
            Cv2.Add(factor, Scalar.All(1.0), factor);
            using var threshold = new Mat();
            Cv2.Multiply(mean, factor, threshold);

            using var binary = new Mat(gray.Size(), MatType.CV_8UC1);
            for (int y = 0; y < gray.Rows; y++)
                for (int x = 0; x < gray.Cols; x++)
                    binary.Set(y, x, grayF.At<double>(y, x) > threshold.At<double>(y, x) ? (byte)255 : (byte)0);

            var path = Path.Combine(Path.GetTempPath(), $"{imageReference}_sauvola_{Guid.NewGuid():N}.png");
            Cv2.ImWrite(path, binary);
            return path;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WARN] Sauvola binarization: {ex.Message}");
            return null;
        }
    }

    private static bool ShouldTrySauvola(string? qualityProfile)
    {
        if (string.IsNullOrEmpty(qualityProfile)) return false;
        try
        {
            using var doc = JsonDocument.Parse(qualityProfile);
            // Sauvola targets uneven lighting; skip when uniformity is already high (≥ threshold).
            if (doc.RootElement.TryGetProperty("illuminationUniformity", out var u))
                return u.GetDouble() < IlluminationUniformityThreshold;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WARN] parse quality profile for Sauvola decision: {ex.Message}");
        }
        return false;
    }

    #endregion

    #region Result Saving

    private string GetOutputDirectory(string imageReference)
    {
        var basePath = _store.GetImagePath(imageReference);
        return Path.GetDirectoryName(basePath) ?? "";
    }

    private void SaveOcrResults(string imageReference, OcrResult result)
    {
        try
        {
            var dir = GetOutputDirectory(imageReference);
            File.WriteAllText(Path.Combine(dir, "ocr_result.txt"), result.Text);
            File.WriteAllText(Path.Combine(dir, "ocr_result.json"),
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WARN] save OCR result files: {ex.Message}");
        }
    }

    private void SaveWinningResult(string imageReference, string bestJson)
    {
        try
        {
            var dir = GetOutputDirectory(imageReference);
            var ocrResult = JsonSerializer.Deserialize<OcrResult>(bestJson);
            File.WriteAllText(Path.Combine(dir, "ocr_result.txt"), ocrResult?.Text ?? "");
            File.WriteAllText(Path.Combine(dir, "ocr_result.json"),
                JsonSerializer.Serialize(ocrResult, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WARN] save winning OCR result files: {ex.Message}");
        }
    }

    private void SaveWinningVariantImage(string imageReference, string winningVariant, Dictionary<string, string> variantImages)
    {
        try
        {
            if (!variantImages.TryGetValue(winningVariant, out var variantPath) || !File.Exists(variantPath)) return;
            var dir = GetOutputDirectory(imageReference);
            var destPath = Path.Combine(dir, $"ocr_best_variant_{winningVariant}.png");
            File.Copy(variantPath, destPath, overwrite: true);
            Debug.WriteLine($"[OCR] Saved best variant image: ocr_best_variant_{winningVariant}.png");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OCR] Failed to save variant image: {ex.Message}");
        }
    }

    private void SaveCandidateScores(string imageReference, List<(string json, OcrScore score)> candidates)
    {
        try
        {
            var dir = GetOutputDirectory(imageReference);
            var scores = candidates.Select(c => c.score).ToList();
            File.WriteAllText(Path.Combine(dir, "ocr_candidates.json"),
                JsonSerializer.Serialize(scores, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WARN] save OCR candidate scores: {ex.Message}");
        }
    }

    #endregion

    private static OcrPageSegMode ParsePageSegMode(string mode) => mode.ToLowerInvariant() switch
    {
        "single_column" => OcrPageSegMode.SingleColumn,
        "single_block" => OcrPageSegMode.SingleBlock,
        "single_line" => OcrPageSegMode.SingleLine,
        "single_word" => OcrPageSegMode.SingleWord,
        "single_char" => OcrPageSegMode.SingleChar,
        _ => OcrPageSegMode.Auto
    };

    #region Public API

    /// <summary>Exposes <see cref="RunOcr"/> as an AI-callable tool.</summary>
    /// <returns>Sequence containing the RunOcr tool.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.RunOcr);
    }

    #endregion
}
