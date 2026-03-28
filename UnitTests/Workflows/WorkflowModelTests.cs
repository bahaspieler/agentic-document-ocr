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
    }
}