using MyExperiment;

namespace UnitTests.Cloud;

/// <summary>Tests natural-language dataset selection independently of Azure Storage.</summary>
[TestClass]
public sealed class BulkCommandParserTests
{
    /// <summary>Recognizes the supported all-images command form.</summary>
    [TestMethod]
    public void IsBulkCommand_AllImagesCommand_ReturnsTrue()
    {
        Assert.IsTrue(BulkCommandParser.IsBulkCommand(
            "Remove background from all images in receipt1-dataset."));
        Assert.IsFalse(BulkCommandParser.IsBulkCommand(
            "Remove background from image RCPT001."));
    }

    /// <summary>Uses a full dataset-folder boundary instead of prefix matching.</summary>
    [TestMethod]
    public void ResolveDatasetName_SimilarNames_SelectsExactMention()
    {
        var selected = BulkCommandParser.ResolveDatasetName(
            "Denoise every receipt in cord-smoke.",
            ["cord", "cord-smoke"]);

        Assert.AreEqual("cord-smoke", selected);
    }

    /// <summary>Rejects a command that does not name a known folder.</summary>
    [TestMethod]
    public void ResolveDatasetName_UnknownFolder_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            BulkCommandParser.ResolveDatasetName(
                "Deskew all images in missing.",
                ["cord", "wildreceipt"]));
    }
}
