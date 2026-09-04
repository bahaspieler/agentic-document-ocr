using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Ocr;
using OcrAgent.Plugins.Ocr;
using OcrAgent.Storage;
using OpenCvSharp;

namespace UnitTests.Plugins;

/// <summary>
/// Tests OcrPlugin Tesseract runs, PSM modes, and JSON output.
/// </summary>
[TestClass]
public class OcrPluginTests
{
    private string _tempRoot = null!;
    private LocalFileImageStore _store = null!;
    private string _testImageRef = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "OcrPluginTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _store = new LocalFileImageStore(_tempRoot);
        _testImageRef = CreateTestImage();
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }
        catch { }
    }

    [TestMethod]
    public async Task RunOcr_NaturalVariant_CallsEngineOnce()
    {
        var engine = new FakeOcrEngine("Receipt text", 88);
        var plugin = new OcrPlugin(_store, engine);

        var resultJson = await plugin.RunOcr(_testImageRef, variant: "natural");

        Assert.AreEqual(1, engine.CallCount, "Natural variant should call engine once");
        var result = JsonSerializer.Deserialize<OcrResult>(resultJson);
        Assert.IsNotNull(result);
        Assert.AreEqual("Receipt text", result.Text);
    }

    [TestMethod]
    public async Task RunOcr_OtsuVariant_CallsEngineOnce()
    {
        var engine = new FakeOcrEngine("Otsu text", 75);
        var plugin = new OcrPlugin(_store, engine);

        var resultJson = await plugin.RunOcr(_testImageRef, variant: "otsu");

        Assert.AreEqual(1, engine.CallCount);
        var result = JsonSerializer.Deserialize<OcrResult>(resultJson);
        Assert.IsNotNull(result);
        Assert.AreEqual("Otsu text", result.Text);
    }

    [TestMethod]
    public async Task RunOcr_AdaptiveGaussianVariant_CallsEngineOnce()
    {
        var engine = new FakeOcrEngine("Adaptive text", 80);
        var plugin = new OcrPlugin(_store, engine);

        var resultJson = await plugin.RunOcr(_testImageRef, variant: "adaptive_gaussian");

        Assert.AreEqual(1, engine.CallCount);
        var result = JsonSerializer.Deserialize<OcrResult>(resultJson);
        Assert.IsNotNull(result);
        Assert.AreEqual("Adaptive text", result.Text);
    }

    [TestMethod]
    public async Task RunOcr_SauvolaVariant_CallsEngineOnce()
    {
        var engine = new FakeOcrEngine("Sauvola text", 82);
        var plugin = new OcrPlugin(_store, engine);

        var resultJson = await plugin.RunOcr(_testImageRef, variant: "sauvola");

        Assert.AreEqual(1, engine.CallCount);
        var result = JsonSerializer.Deserialize<OcrResult>(resultJson);
        Assert.IsNotNull(result);
        Assert.AreEqual("Sauvola text", result.Text);
    }

    [TestMethod]
    public async Task RunOcr_EmptyVariant_DefaultsToNatural()
    {
        var engine = new FakeOcrEngine("Default text", 80);
        var plugin = new OcrPlugin(_store, engine);

        var resultJson = await plugin.RunOcr(_testImageRef, variant: "");

        Assert.AreEqual(1, engine.CallCount);
        var result = JsonSerializer.Deserialize<OcrResult>(resultJson);
        Assert.IsNotNull(result);
        Assert.AreEqual("Default text", result.Text);
    }

    [TestMethod]
    public async Task RunOcr_EmptyPsm_DefaultsToAuto()
    {
        var engine = new FakeOcrEngine("PSM auto", 80);
        var plugin = new OcrPlugin(_store, engine);

        await plugin.RunOcr(_testImageRef, pageSegMode: "");

        Assert.AreEqual(1, engine.CallCount);
        Assert.AreEqual(OcrPageSegMode.Auto, engine.LastOptions!.PageSegMode);
    }

    [TestMethod]
    public async Task RunOcr_SingleColumnPsm_ParsedCorrectly()
    {
        var engine = new FakeOcrEngine("Col text", 80);
        var plugin = new OcrPlugin(_store, engine);

        await plugin.RunOcr(_testImageRef, pageSegMode: "single_column");

        Assert.AreEqual(OcrPageSegMode.SingleColumn, engine.LastOptions!.PageSegMode);
    }

    [TestMethod]
    public async Task RunOcr_SingleBlockPsm_ParsedCorrectly()
    {
        var engine = new FakeOcrEngine("Block text", 80);
        var plugin = new OcrPlugin(_store, engine);

        await plugin.RunOcr(_testImageRef, pageSegMode: "single_block");

        Assert.AreEqual(OcrPageSegMode.SingleBlock, engine.LastOptions!.PageSegMode);
    }

    [TestMethod]
    public async Task RunOcr_SingleLinePsm_ParsedCorrectly()
    {
        var engine = new FakeOcrEngine("Line text", 80);
        var plugin = new OcrPlugin(_store, engine);

        await plugin.RunOcr(_testImageRef, pageSegMode: "single_line");

        Assert.AreEqual(OcrPageSegMode.SingleLine, engine.LastOptions!.PageSegMode);
    }

    [TestMethod]
    public async Task RunOcr_UnknownPsm_DefaultsToAuto()
    {
        var engine = new FakeOcrEngine("Auto text", 80);
        var plugin = new OcrPlugin(_store, engine);

        await plugin.RunOcr(_testImageRef, pageSegMode: "unknown_mode");

        Assert.AreEqual(OcrPageSegMode.Auto, engine.LastOptions!.PageSegMode);
    }

    [TestMethod]
    public async Task RunOcr_AutoVariant_TriesMultipleCandidates()
    {
        var engine = new FakeOcrEngine("Multi text TOTAL 5.99", 85);
        var plugin = new OcrPlugin(_store, engine);

        var resultJson = await plugin.RunOcr(_testImageRef, variant: "auto");

        // auto mode tries natural, otsu, adaptive_gaussian × 2 PSMs (SingleColumn + SingleBlock)
        Assert.IsGreaterThanOrEqualTo(4, engine.CallCount, $"Auto variant should try at least 4 candidates, got {engine.CallCount}");
        var result = JsonSerializer.Deserialize<OcrResult>(resultJson);
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public async Task RunOcr_MultiPsm_TriesBothColumnAndBlock()
    {
        var engine = new FakeOcrEngine("Multi PSM text", 80);
        var plugin = new OcrPlugin(_store, engine);

        var resultJson = await plugin.RunOcr(_testImageRef, pageSegMode: "multi", variant: "natural");

        // multi PSM should try both SingleColumn and SingleBlock
        Assert.AreEqual(2, engine.CallCount, "Multi PSM should try 2 modes");

        var psmsSeen = engine.AllOptions.Select(o => o.PageSegMode).Distinct().ToList();
        Assert.Contains(OcrPageSegMode.SingleColumn, psmsSeen, "Should try SingleColumn");
        Assert.Contains(OcrPageSegMode.SingleBlock, psmsSeen, "Should try SingleBlock");
    }

    [TestMethod]
    public async Task RunOcr_AutoVariant_ReturnsJsonResult()
    {
        var engine = new FakeOcrEngine("Result text", 75);
        var plugin = new OcrPlugin(_store, engine);

        var resultJson = await plugin.RunOcr(_testImageRef, variant: "auto");

        var result = JsonSerializer.Deserialize<OcrResult>(resultJson);
        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrEmpty(result.Text));
    }

    [TestMethod]
    public async Task RunOcr_AutoVariant_WithMaxVariants_LimitsCount()
    {
        var engine = new FakeOcrEngine("Limited text", 80);
        var plugin = new OcrPlugin(_store, engine);

        await plugin.RunOcr(_testImageRef, variant: "auto", maxVariants: 1);

        // maxVariants=1 should limit to only the first variant (natural) × 2 PSMs
        Assert.AreEqual(2, engine.CallCount, $"maxVariants=1 should limit to 2 calls (1 variant × 2 PSMs), got {engine.CallCount}");
    }

    [TestMethod]
    public async Task RunOcr_AutoVariant_WithQualityProfile_TriesSauvola()
    {
        var engine = new FakeOcrEngine("Sauvola auto text", 80);
        var plugin = new OcrPlugin(_store, engine);

        var qualityProfile = """{"illuminationUniformity": 0.5}""";
        await plugin.RunOcr(_testImageRef, variant: "auto", qualityProfile: qualityProfile);

        // Low uniformity (0.5 < 0.85) should trigger Sauvola in addition to natural, otsu, adaptive
        Assert.IsGreaterThanOrEqualTo(6,
engine.CallCount, $"Auto with low illumination should try all 4 variants (natural+otsu+adaptive+sauvola × 2 PSMs), got {engine.CallCount}");
    }

    [TestMethod]
    public async Task RunOcr_AutoVariant_HighUniformity_SkipsSauvola()
    {
        var engine = new FakeOcrEngine("No Sauvola", 80);
        var plugin = new OcrPlugin(_store, engine);

        var qualityProfile = """{"illuminationUniformity": 0.95}""";
        await plugin.RunOcr(_testImageRef, variant: "auto", qualityProfile: qualityProfile);

        // High uniformity should skip Sauvola: 3 variants × 2 PSMs = 6
        Assert.AreEqual(6, engine.CallCount,
            $"Auto with high uniformity should try 3 variants × 2 PSMs = 6, got {engine.CallCount}");
    }

    [TestMethod]
    public async Task RunOcr_PassesDpiToEngine()
    {
        var engine = new FakeOcrEngine("DPI text", 80);
        var plugin = new OcrPlugin(_store, engine);

        await plugin.RunOcr(_testImageRef, dpi: 300);

        Assert.AreEqual(300, engine.LastOptions!.Dpi);
    }

    [TestMethod]
    public async Task RunOcr_SetsConfigVars()
    {
        var engine = new FakeOcrEngine("Config text", 80);
        var plugin = new OcrPlugin(_store, engine);

        await plugin.RunOcr(_testImageRef);

        Assert.IsNotNull(engine.LastOptions);
        Assert.AreEqual("false", engine.LastOptions.ConfigVars["load_system_dawg"]);
        Assert.AreEqual("false", engine.LastOptions.ConfigVars["load_freq_dawg"]);
    }

    [TestMethod]
    public async Task RunOcr_SavesOcrResultFiles()
    {
        var engine = new FakeOcrEngine("Saved text", 80);
        var plugin = new OcrPlugin(_store, engine);

        await plugin.RunOcr(_testImageRef, variant: "natural");

        var imageDir = Path.GetDirectoryName(_store.GetImagePath(_testImageRef))!;
        Assert.IsTrue(File.Exists(Path.Combine(imageDir, "ocr_result.txt")), "ocr_result.txt should exist");
        Assert.IsTrue(File.Exists(Path.Combine(imageDir, "ocr_result.json")), "ocr_result.json should exist");

        var savedText = File.ReadAllText(Path.Combine(imageDir, "ocr_result.txt"));
        Assert.AreEqual("Saved text", savedText);
    }

    [TestMethod]
    public async Task RunOcr_AutoVariant_SavesCandidateScores()
    {
        var engine = new FakeOcrEngine("Candidate text", 80);
        var plugin = new OcrPlugin(_store, engine);

        await plugin.RunOcr(_testImageRef, variant: "auto");

        var imageDir = Path.GetDirectoryName(_store.GetImagePath(_testImageRef))!;
        Assert.IsTrue(File.Exists(Path.Combine(imageDir, "ocr_candidates.json")),
            "ocr_candidates.json should be saved in auto mode");
    }

    [TestMethod]
    public void GetAITools_ReturnsExactlyOne()
    {
        var engine = new FakeOcrEngine("test", 80);
        var plugin = new OcrPlugin(_store, engine);

        var tools = plugin.GetAITools().ToList();

        Assert.HasCount(1, tools);
    }

    private string CreateTestImage()
    {
        using var img = new Mat(200, 300, MatType.CV_8UC3, Scalar.White);
        Cv2.PutText(img, "TEST 123", new Point(30, 100),
            HersheyFonts.HersheySimplex, 1.0, Scalar.Black, 2);
        Cv2.ImEncode(".png", img, out var pngBytes);
        return _store.SaveBytes(pngBytes, "png", new Dictionary<string, string> { ["source"] = "test" }, "OCR_TEST");
    }

}
