using System.Text.Json;
using Microsoft.Extensions.AI;
using OcrAgent.Workflows.Engine;
using OcrAgent.Workflows.Model;

namespace UnitTests.Workflows;

[TestClass]
public class WorkflowRunnerIntegrationTests
{
    private string _tempDir = "";

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"workflow_runner_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static ToolBindingRegistry CreatePassthroughRegistry(params string[] toolNames)
    {
        var registry = new ToolBindingRegistry();

        foreach (var name in toolNames)
        {
            var func = AIFunctionFactory.Create(
                (string imageReference) => $"{name.ToLower()}_processed_{imageReference}",
                name);

            registry.Register(func);
        }

        return registry;
    }

    private void WriteDefinition(string id, params (string stepId, string tool)[] steps)
    {
        var def = new WorkflowDefinition
        {
            Id = id,
            Name = $"Test {id}",
            Description = $"Test workflow {id}",
            Steps = steps.Select(s => new WorkflowStep
            {
                Id = s.stepId,
                Type = "tool",
                Tool = s.tool,
                Enabled = true,
                Args = new Dictionary<string, object?>()
            }).ToList()
        };

        var json = JsonSerializer.Serialize(def);
        File.WriteAllText(Path.Combine(_tempDir, $"{id}.json"), json);
    }
}