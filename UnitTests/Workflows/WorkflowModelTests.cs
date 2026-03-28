using System.Text.Json;
using OcrAgent.Workflows.Model;

namespace UnitTests.Workflows;

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
}