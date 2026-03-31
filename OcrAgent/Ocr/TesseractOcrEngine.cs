using System.Diagnostics;
using System.Runtime.InteropServices;
using Tesseract;

namespace OcrAgent.Ocr;

/// <summary>
/// Tesseract-backed <see cref="IOcrEngine"/> using bundled tessdata and optional macOS arm64 native fixup.
/// </summary>
public sealed class TesseractOcrEngine : IOcrEngine, IDisposable
{
    private static int _nativeResolverRegistered;

    private readonly TesseractEngine _engine;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Creates a Tesseract engine for <paramref name="defaultLanguage"/> using trained data under <paramref name="tessdataPath"/>.
    /// </summary>
    /// <param name="tessdataPath">Directory containing <c>.traineddata</c> files.</param>
    /// <param name="defaultLanguage">BCP-47-style language code (default eng).</param>
    /// <exception cref="DirectoryNotFoundException">When <paramref name="tessdataPath"/> is missing.</exception>
    public TesseractOcrEngine(string tessdataPath, string defaultLanguage = "eng")
    {
        if (!Directory.Exists(tessdataPath))
            throw new DirectoryNotFoundException(
                $"Tessdata directory not found: {tessdataPath}. " +
                $"Download eng.traineddata from https://github.com/tesseract-ocr/tessdata and place it in {tessdataPath}");

        EnsureNativeResolver();
        _engine = new TesseractEngine(tessdataPath, defaultLanguage, EngineMode.Default);
    }

    /// <summary>
    /// Loads the image, runs Tesseract under the instance lock, and returns text and layout.
    /// </summary>
    /// <param name="imagePath">Path to the image file.</param>
    /// <param name="options">Page segmentation, DPI, and Tesseract variables; null uses defaults.</param>
    /// <returns>Recognition result for the page.</returns>
    public Task<OcrResult> RecognizeAsync(string imagePath, OcrOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        options ??= new OcrOptions();

        lock (_lock)
        {
            ApplyConfigVars(options);

            using var img = Pix.LoadFromFile(imagePath);

            if (options.Dpi > 0)
            {
                img.XRes = options.Dpi;
                img.YRes = options.Dpi;
            }

            using var page = _engine.Process(img, (PageSegMode)(int)options.PageSegMode);

            var result = new OcrResult
            {
                Text = page.GetText().Trim(),
                EngineName = "Tesseract"
            };

            ExtractWordLevel(page, result);

            if (result.Lines.Count > 0)
            {
                var allConf = result.Lines
                    .SelectMany(l => l.Words)
                    .Where(w => w.Confidence > 0)
                    .Select(w => w.Confidence)
                    .ToList();
                result.Confidence = allConf.Count > 0 ? allConf.Average() : 0;
            }
            else
            {
                result.Confidence = page.GetMeanConfidence() * 100;
            }

            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// Applies <see cref="OcrOptions.ConfigVars"/> to the shared engine; failed keys are logged only.
    /// </summary>
    /// <param name="options">Current recognition options.</param>
    private void ApplyConfigVars(OcrOptions options)
    {
        // Same engine instance for all calls; variables persist until changed or process exits.
        foreach (var (key, value) in options.ConfigVars)
        {
            if (!_engine.SetVariable(key, value))
                System.Diagnostics.Debug.WriteLine($"[WARN] Tesseract variable '{key}' could not be set");
        }
    }

    /// <summary>
    /// Walks the page iterator at word level and fills <see cref="OcrResult.Lines"/>.
    /// </summary>
    /// <param name="page">Open Tesseract page.</param>
    /// <param name="result">Result to populate.</param>
    private static void ExtractWordLevel(Page page, OcrResult result)
    {
        using var iter = page.GetIterator();
        if (iter == null) return;

        OcrLine? currentLine = null;

        iter.Begin();
        do
        {
            if (iter.IsAtBeginningOf(PageIteratorLevel.TextLine))
            {
                currentLine = new OcrLine();

                if (iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out var lineRect))
                {
                    currentLine.BoundingBox = new OcrBoundingBox
                    {
                        X = lineRect.X1, Y = lineRect.Y1,
                        Width = lineRect.Width, Height = lineRect.Height
                    };
                }

                result.Lines.Add(currentLine);
            }

            if (currentLine == null) continue;

            var wordText = iter.GetText(PageIteratorLevel.Word);
            if (string.IsNullOrWhiteSpace(wordText)) continue;

            var word = new OcrWord
            {
                Text = wordText.Trim(),
                Confidence = iter.GetConfidence(PageIteratorLevel.Word)
            };

            if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var wordRect))
            {
                word.BoundingBox = new OcrBoundingBox
                {
                    X = wordRect.X1, Y = wordRect.Y1,
                    Width = wordRect.Width, Height = wordRect.Height
                };
            }

            currentLine.Words.Add(word);
        } while (iter.Next(PageIteratorLevel.Word));

        foreach (var line in result.Lines)
        {
            line.Text = string.Join(" ", line.Words.Select(w => w.Text));
            line.Confidence = line.Words.Count > 0
                ? line.Words.Average(w => w.Confidence)
                : 0;
        }
    }

    /// <summary>
    /// On Apple Silicon, copies Homebrew Leptonica/Tesseract dylibs next to the app so native loading succeeds.
    /// </summary>
    private static void EnsureNativeResolver()
    {
        // Run once: InteropDotNet resolves native libs by fixed versioned filenames under BaseDirectory/x64/.
        // The NuGet layout targets x64; on macOS arm64 those names must point at arm64 dylibs or load fails.
        if (Interlocked.Exchange(ref _nativeResolverRegistered, 1) == 1)
            return;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
            RuntimeInformation.OSArchitecture != Architecture.Arm64)
            return;

        var brewPrefix = Environment.GetEnvironmentVariable("HOMEBREW_PREFIX") ?? "/opt/homebrew";
        var brewLib = Path.Combine(brewPrefix, "lib");
        if (!Directory.Exists(brewLib)) return;

        var leptSrc = Path.Combine(brewLib, "libleptonica.dylib");
        var tessSrc = Path.Combine(brewLib, "libtesseract.dylib");
        if (!File.Exists(leptSrc) || !File.Exists(tessSrc)) return;

        var x64Dir = Path.Combine(AppContext.BaseDirectory, "x64");
        if (!Directory.Exists(x64Dir))
            Directory.CreateDirectory(x64Dir);

        // Exact names expected by InteropDotNet (not the generic brew symlinks).
        CopyIfNeeded(leptSrc, Path.Combine(x64Dir, "libleptonica-1.82.0.dylib"));
        CopyIfNeeded(tessSrc, Path.Combine(x64Dir, "libtesseract50.dylib"));
    }

    /// <summary>
    /// Copies the source dylib to the target path if needed.
    /// </summary>
    /// <param name="src">Homebrew library path.</param>
    /// <param name="dest">Expected filename under <c>x64</c>.</param>
    private static void CopyIfNeeded(string src, string dest)
    {
        try
        {
            // Skip copy when dest already matches brew lib size (cheap "up to date" check).
            if (File.Exists(dest))
            {
                var srcLen = new FileInfo(src).Length;
                var dstLen = new FileInfo(dest).Length;
                if (srcLen == dstLen) return;
            }
            File.Copy(src, dest, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WARN] copy native library for Tesseract resolver: {ex.Message}");
        }
    }

    /// <summary>
    /// Releases the underlying Tesseract engine.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.Dispose();
    }
}
