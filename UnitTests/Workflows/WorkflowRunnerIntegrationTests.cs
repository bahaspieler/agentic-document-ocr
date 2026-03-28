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

    [TestMethod]
    public async Task RunAsync_SimpleWorkflow_Succeeds()
    {
        WriteDefinition("simple_test", ("s1", "StepA"), ("s2", "StepB"));

        var registry = CreatePassthroughRegistry("StepA", "StepB");
        var catalog = new WorkflowCatalog(_tempDir);
        var compiler = new WorkflowCompiler(registry);
        var runner = new WorkflowRunner(catalog, compiler);

        var result = await runner.RunAsync("simple_test", "REF001");

        Assert.IsTrue(result.Success, $"Workflow failed: {result.Error}");
        Assert.AreEqual("simple_test", result.WorkflowId);
        Assert.AreEqual("REF001", result.InputImageRef);
        Assert.IsTrue(result.OutputImageRef.Length > 0, "OutputImageRef should not be empty");
        Assert.AreEqual(2, result.Trace.Count);
        Assert.IsTrue(result.TotalDurationMs >= 0);
    }

    [TestMethod]
    public async Task RunAsync_TraceEntries_HaveCorrectData()
    {
        WriteDefinition("trace_test", ("step1", "ToolX"), ("step2", "ToolY"));

        var registry = CreatePassthroughRegistry("ToolX", "ToolY");
        var catalog = new WorkflowCatalog(_tempDir);
        var compiler = new WorkflowCompiler(registry);
        var runner = new WorkflowRunner(catalog, compiler);

        var result = await runner.RunAsync("trace_test", "IMG_123");

        Assert.IsTrue(result.Success, $"Workflow failed: {result.Error}");
        Assert.AreEqual(2, result.Trace.Count);

        var firstTrace = result.Trace[0];
        Assert.AreEqual("step1", firstTrace.StepId);
        Assert.AreEqual("ToolX", firstTrace.ToolName);
        Assert.AreEqual("IMG_123", firstTrace.InputRef);
        Assert.IsTrue(firstTrace.Success);
        Assert.IsTrue(firstTrace.DurationMs >= 0);

        var secondTrace = result.Trace[1];
        Assert.AreEqual("step2", secondTrace.StepId);
        Assert.AreEqual("ToolY", secondTrace.ToolName);
        Assert.IsTrue(secondTrace.Success);
    }
}