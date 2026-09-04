using System.Text.Json;
using OcrAgent.Agent;

namespace UnitTests.Cloud;

/// <summary>Tests deterministic validation at the AI planning boundary.</summary>
[TestClass]
public sealed class BulkActionPlannerTests
{
    /// <summary>Accepts an ordered chain of registered image transformations.</summary>
    [TestMethod]
    public void Validate_RegisteredToolChain_ReturnsPlan()
    {
        var plan = new BulkActionPlan(
        [
            new BulkPlanStep("tool", "CorrectPerspective"),
            new BulkPlanStep("tool", "RemoveDocumentBackground")
        ]);

        var result = BulkActionPlanner.Validate(
            plan,
            ["CorrectPerspective", "RemoveDocumentBackground"]);

        Assert.AreSame(plan, result);
    }

    /// <summary>Rejects non-transform or hallucinated tool names.</summary>
    [TestMethod]
    public void Validate_UnsafeTool_Throws()
    {
        var plan = new BulkActionPlan(
            [new BulkPlanStep("tool", "RunOcr")]);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            BulkActionPlanner.Validate(plan, ["RunOcr"]));
    }

    /// <summary>Rejects model-supplied image references so children remain isolated.</summary>
    [TestMethod]
    public void Validate_ImageReferenceArgument_Throws()
    {
        using var document = JsonDocument.Parse("\"wrong-image\"");
        var plan = new BulkActionPlan(
        [
            new BulkPlanStep(
                "tool",
                "DenoiseImage",
                new Dictionary<string, JsonElement>
                {
                    ["imageReference"] = document.RootElement.Clone()
                })
        ]);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            BulkActionPlanner.Validate(plan, ["DenoiseImage"]));
    }

    /// <summary>Accepts a registered workflow only when it is the complete plan.</summary>
    [TestMethod]
    public void Validate_RegisteredWorkflow_ReturnsPlan()
    {
        var plan = new BulkActionPlan(
            [new BulkPlanStep("workflow", "receipt-standard")]);

        var result = BulkActionPlanner.Validate(
            plan, [], ["receipt-standard"]);

        Assert.AreSame(plan, result);
    }

    /// <summary>Offers workflows only for explicitly workflow-level commands.</summary>
    [TestMethod]
    public void AllowsWorkflows_PreprocessingOnly_ReturnsFalse()
    {
        Assert.IsFalse(BulkActionPlanner.AllowsWorkflows(
            "Remove background from all images in cord."));
        Assert.IsTrue(BulkActionPlanner.AllowsWorkflows(
            "Extract receipt data using workflow receipt_extract_v3 from every image in cord."));
    }
}
