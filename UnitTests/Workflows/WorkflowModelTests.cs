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
}