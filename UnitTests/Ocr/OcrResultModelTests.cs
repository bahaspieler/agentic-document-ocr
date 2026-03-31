using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Ocr;

namespace UnitTests.Ocr;

/// <summary>
/// Tests OcrResult defaults, JSON serialization, and related model behavior.
/// </summary>
[TestClass]
public class OcrResultModelTests
{
    [TestMethod]
    public void OcrResult_DefaultValues()
    {
        var result = new OcrResult();

        Assert.AreEqual("", result.Text);
        Assert.AreEqual(0, result.Confidence);
        Assert.IsNotNull(result.Lines);
        Assert.AreEqual(0, result.Lines.Count);
        Assert.AreEqual("", result.EngineName);
    }

    [TestMethod]
    public void OcrResult_JsonRoundtrip()
    {
        var original = new OcrResult
        {
            Text = "Hello World",
            Confidence = 92.5,
            EngineName = "Tesseract",
            Lines = new List<OcrLine>
            {
                new()
                {
                    Text = "Hello World",
                    Confidence = 92.5,
                    BoundingBox = new OcrBoundingBox { X = 10, Y = 20, Width = 200, Height = 30 },
                    Words = new List<OcrWord>
                    {
                        new()
                        {
                            Text = "Hello",
                            Confidence = 95,
                            BoundingBox = new OcrBoundingBox { X = 10, Y = 20, Width = 80, Height = 30 }
                        },
                        new()
                        {
                            Text = "World",
                            Confidence = 90,
                            BoundingBox = new OcrBoundingBox { X = 100, Y = 20, Width = 80, Height = 30 }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<OcrResult>(json);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(original.Text, deserialized.Text);
        Assert.AreEqual(original.Confidence, deserialized.Confidence);
        Assert.AreEqual(original.EngineName, deserialized.EngineName);
        Assert.AreEqual(1, deserialized.Lines.Count);
        Assert.AreEqual(2, deserialized.Lines[0].Words.Count);
        Assert.AreEqual("Hello", deserialized.Lines[0].Words[0].Text);
        Assert.AreEqual(95, deserialized.Lines[0].Words[0].Confidence);
    }

    [TestMethod]
    public void OcrResult_JsonPropertyNames_AreCorrect()
    {
        var result = new OcrResult
        {
            Text = "Test",
            Confidence = 85,
            EngineName = "Tesseract"
        };

        var json = JsonSerializer.Serialize(result);

        Assert.IsTrue(json.Contains("\"text\""), "Should use camelCase 'text'");
        Assert.IsTrue(json.Contains("\"confidence\""), "Should use camelCase 'confidence'");
        Assert.IsTrue(json.Contains("\"engineName\""), "Should use camelCase 'engineName'");
        Assert.IsTrue(json.Contains("\"lines\""), "Should use camelCase 'lines'");
    }

    [TestMethod]
    public void OcrResult_EmptyText_Deserializes()
    {
        var json = """{"text":"","confidence":0,"lines":[],"engineName":""}""";
        var result = JsonSerializer.Deserialize<OcrResult>(json);

        Assert.IsNotNull(result);
        Assert.AreEqual("", result.Text);
        Assert.AreEqual(0, result.Lines.Count);
    }

    [TestMethod]
    public void OcrLine_DefaultValues()
    {
        var line = new OcrLine();

        Assert.AreEqual("", line.Text);
        Assert.AreEqual(0, line.Confidence);
        Assert.IsNotNull(line.BoundingBox);
        Assert.IsNotNull(line.Words);
        Assert.AreEqual(0, line.Words.Count);
    }

    [TestMethod]
    public void OcrBoundingBox_DefaultValues()
    {
        var box = new OcrBoundingBox();

        Assert.AreEqual(0, box.X);
        Assert.AreEqual(0, box.Y);
        Assert.AreEqual(0, box.Width);
        Assert.AreEqual(0, box.Height);
    }

    [TestMethod]
    public void OcrBoundingBox_JsonRoundtrip()
    {
        var box = new OcrBoundingBox { X = 5, Y = 10, Width = 100, Height = 25 };
        var json = JsonSerializer.Serialize(box);
        var deserialized = JsonSerializer.Deserialize<OcrBoundingBox>(json);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(5, deserialized.X);
        Assert.AreEqual(10, deserialized.Y);
        Assert.AreEqual(100, deserialized.Width);
        Assert.AreEqual(25, deserialized.Height);
    }

    [TestMethod]
    public void OcrOptions_DefaultValues()
    {
        var opts = new OcrOptions();

        Assert.AreEqual("eng", opts.Language);
        Assert.AreEqual(OcrPageSegMode.Auto, opts.PageSegMode);
        Assert.AreEqual(0, opts.Dpi);
        Assert.IsNotNull(opts.ConfigVars);
        Assert.AreEqual(0, opts.ConfigVars.Count);
    }

    [TestMethod]
    public void OcrOptions_ConfigVars_CanBeSet()
    {
        var opts = new OcrOptions
        {
            ConfigVars = new Dictionary<string, string>
            {
                ["load_system_dawg"] = "false",
                ["load_freq_dawg"] = "false"
            }
        };

        Assert.AreEqual(2, opts.ConfigVars.Count);
        Assert.AreEqual("false", opts.ConfigVars["load_system_dawg"]);
    }

    [TestMethod]
    public void OcrOptions_DpiAndLanguage_CanBeSet()
    {
        var opts = new OcrOptions { Language = "deu", Dpi = 300 };

        Assert.AreEqual("deu", opts.Language);
        Assert.AreEqual(300, opts.Dpi);
    }

    [TestMethod]
    public void OcrPageSegMode_EnumValues_MatchTesseractPSM()
    {
        Assert.AreEqual(3, (int)OcrPageSegMode.Auto);
        Assert.AreEqual(4, (int)OcrPageSegMode.SingleColumn);
        Assert.AreEqual(6, (int)OcrPageSegMode.SingleBlock);
        Assert.AreEqual(7, (int)OcrPageSegMode.SingleLine);
        Assert.AreEqual(8, (int)OcrPageSegMode.SingleWord);
        Assert.AreEqual(10, (int)OcrPageSegMode.SingleChar);
    }

    [TestMethod]
    public void OcrPageSegMode_CastToInt_ForTesseract()
    {
        var psm = OcrPageSegMode.SingleBlock;
        int psmInt = (int)psm;

        Assert.AreEqual(6, psmInt);
    }

    [TestMethod]
    public void OcrPageSegMode_DefaultInOptions_IsAuto()
    {
        var opts = new OcrOptions();
        Assert.AreEqual(OcrPageSegMode.Auto, opts.PageSegMode);
        Assert.AreEqual(3, (int)opts.PageSegMode);
    }

}
