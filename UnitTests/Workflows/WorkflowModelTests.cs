using System.Text.Json;
using OcrAgent.Workflows.Model;

namespace UnitTests.Workflows;

/// <summary>
/// Tests workflow JSON models deserialize and round-trip correctly.
/// </summary>
[TestClass]
public class WorkflowModelTests
{
    [TestMethod]
    public void WorkflowDefinition_Deserialization_Roundtrip()
    {
        var json = """
        {
          "id": "test_workflow",
          "name": "Test Workflow",
          "version": "2.0.0",
          "description": "A test workflow",
          "tags": ["test", "unit"],
          "budget": { "maxVariants": 5, "timeLimitMs": 30000 },
          "steps": [
            {
              "id": "step1",
              "type": "tool",
              "tool": "SomeTool",
              "args": { "param1": "value1", "param2": 42 },
              "enabled": true,
              "description": "First step"
            },
            {
              "id": "step2",
              "type": "tool",
              "tool": "AnotherTool",
              "args": {},
              "enabled": false,
              "description": "Disabled step"
            }
          ]
        }
        """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var def = JsonSerializer.Deserialize<WorkflowDefinition>(json, options)!;

        Assert.AreEqual("test_workflow", def.Id);
        Assert.AreEqual("Test Workflow", def.Name);
        Assert.AreEqual("2.0.0", def.Version);
        Assert.AreEqual("A test workflow", def.Description);
        Assert.AreEqual(2, def.Tags.Length);
        Assert.AreEqual(5, def.Budget.MaxVariants);
        Assert.AreEqual(30000, def.Budget.TimeLimitMs);
        Assert.AreEqual(2, def.Steps.Count);
        Assert.AreEqual("SomeTool", def.Steps[0].Tool);
        Assert.IsFalse(def.Steps[1].Enabled);
    }

    [TestMethod]
    public void WorkflowDefinition_Serialization_PreservesFields()
    {
        var def = new WorkflowDefinition
        {
            Id = "roundtrip",
            Name = "Roundtrip Test",
            Steps = new List<WorkflowStep>
            {
                new() { Id = "s1", Tool = "T1", Args = new Dictionary<string, object?> { ["key"] = "val" } }
            }
        };

        var json = JsonSerializer.Serialize(def);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var restored = JsonSerializer.Deserialize<WorkflowDefinition>(json, options)!;

        Assert.AreEqual(def.Id, restored.Id);
        Assert.AreEqual(def.Name, restored.Name);
        Assert.AreEqual(1, restored.Steps.Count);
        Assert.AreEqual("T1", restored.Steps[0].Tool);
    }

    [TestMethod]
    public void WorkflowOverrides_Deserialization()
    {
        var json = """
        {
          "steps": {
            "step1": { "enabled": false },
            "step2": { "args": { "method": "custom" } }
          }
        }
        """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var overrides = JsonSerializer.Deserialize<WorkflowOverrides>(json, options)!;

        Assert.IsNotNull(overrides.Steps);
        Assert.AreEqual(2, overrides.Steps.Count);
        Assert.AreEqual(false, overrides.Steps["step1"].Enabled);
        Assert.IsNotNull(overrides.Steps["step2"].Args);
    }

    [TestMethod]
    public void WorkflowRunResult_DefaultValues()
    {
        var result = new WorkflowRunResult();

        Assert.AreEqual("", result.WorkflowId);
        Assert.AreEqual("", result.OutputImageRef);
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Trace);
        Assert.AreEqual(0, result.Trace.Count);
    }

    [TestMethod]
    public void StepTraceEntry_DefaultValues()
    {
        var entry = new StepTraceEntry();

        Assert.AreEqual("", entry.StepId);
        Assert.AreEqual("", entry.ToolName);
        Assert.IsNotNull(entry.Args);
        Assert.IsFalse(entry.Success);
    }

    [TestMethod]
    public void WorkflowStep_Passthrough_DefaultFalse()
    {
        var step = new WorkflowStep();
        Assert.IsFalse(step.Passthrough);
    }

    [TestMethod]
    public void WorkflowStep_Outputs_DefaultNull()
    {
        var step = new WorkflowStep();
        Assert.IsNull(step.Outputs);
    }

    [TestMethod]
    public void WorkflowStep_Passthrough_Deserialization()
    {
        var json = """
        {
          "id": "assess",
          "tool": "AssessQuality",
          "passthrough": true,
          "outputs": { "qualityProfile": "$result" }
        }
        """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var step = JsonSerializer.Deserialize<WorkflowStep>(json, options)!;

        Assert.IsTrue(step.Passthrough);
        Assert.IsNotNull(step.Outputs);
        Assert.AreEqual("$result", step.Outputs["qualityProfile"]);
    }

    [TestMethod]
    public void WorkflowStep_ContextReference_InArgs()
    {
        var json = """
        {
          "id": "denoise",
          "tool": "DenoiseImage",
          "args": { "method": "auto", "qualityProfile": "$context.qualityProfile" }
        }
        """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var step = JsonSerializer.Deserialize<WorkflowStep>(json, options)!;

        Assert.AreEqual("$context.qualityProfile", step.Args["qualityProfile"]?.ToString());
    }

    [TestMethod]
    public void WorkflowRunResult_HasContextAndFinalOutput()
    {
        var result = new WorkflowRunResult();

        Assert.IsNull(result.FinalOutput);
        Assert.IsNotNull(result.Context);
        Assert.AreEqual(0, result.Context.Count);
    }
}
