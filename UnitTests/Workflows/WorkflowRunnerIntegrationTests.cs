using Microsoft.Extensions.AI;
using OcrAgent.Workflows.Engine;

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
}