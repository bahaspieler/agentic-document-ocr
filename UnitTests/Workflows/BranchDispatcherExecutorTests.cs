using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Workflows.Engine;
using OcrAgent.Workflows.Model;

namespace UnitTests.Workflows;

/// <summary>
/// Tests branch dispatching through WorkflowRunner, since BranchDispatcherExecutor
/// is compiled and executed within the workflow protocol by WorkflowCompiler/Runner.
/// </summary>
[TestClass]
public class BranchDispatcherExecutorTests
{
    private string _tempDir = "";
    private readonly List<string> _invokedTools = new();

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"branch_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _invokedTools.Clear();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [TestMethod]
    public async Task Branch_MatchingCondition_ExecutesBranchSteps()
    {
        var registry = CreateTrackingRegistry("PreStep", "TesseractTool", "PaddleTool");
        var definition = CreateBranchWorkflow("tesseract");

        WriteDef(definition);
        var catalog = new WorkflowCatalog(_tempDir);
        var compiler = new WorkflowCompiler(registry);
        var runner = new WorkflowRunner(catalog, compiler);

        var overridesJson = """{ "initialContext": { "ocrEngine": "tesseract" } }""";
        var result = await runner.RunAsync("branch_test", "IMG001", overridesJson);

        Assert.IsTrue(result.Success, $"Workflow failed: {result.Error}");
        Assert.IsTrue(_invokedTools.Contains("TesseractTool"),
            "Tesseract branch tool should have been invoked");
        Assert.IsFalse(_invokedTools.Contains("PaddleTool"),
            "Paddle branch tool should NOT have been invoked");
    }

    [TestMethod]
    public async Task Branch_OtherCondition_ExecutesCorrectBranch()
    {
        var registry = CreateTrackingRegistry("PreStep", "TesseractTool", "PaddleTool");
        var definition = CreateBranchWorkflow("paddle");

        WriteDef(definition);
        var catalog = new WorkflowCatalog(_tempDir);
        var compiler = new WorkflowCompiler(registry);
        var runner = new WorkflowRunner(catalog, compiler);

        var overridesJson = """{ "initialContext": { "ocrEngine": "paddle" } }""";
        var result = await runner.RunAsync("branch_test", "IMG001", overridesJson);

        Assert.IsTrue(result.Success, $"Workflow failed: {result.Error}");
        Assert.IsTrue(_invokedTools.Contains("PaddleTool"),
            "Paddle branch tool should have been invoked");
        Assert.IsFalse(_invokedTools.Contains("TesseractTool"),
            "Tesseract branch tool should NOT have been invoked");
    }

    [TestMethod]
    public async Task Branch_NoMatch_UsesDefaultBranch()
    {
        var registry = CreateTrackingRegistry("PreStep", "TesseractTool", "PaddleTool", "DefaultTool");

        var definition = new WorkflowDefinition
        {
            Id = "branch_test",
            Name = "Branch Default Test",
            Steps = new List<WorkflowStep>
            {
                ToolStep("pre", "PreStep"),
                new()
                {
                    Id = "engine_branch", Type = "branch",
                    Condition = "$context.ocrEngine", Enabled = true,
                    Branches = new Dictionary<string, List<WorkflowStep>>
                    {
                        ["tesseract"] = new() { ToolStep("tess_step", "TesseractTool") },
                        ["default"] = new() { ToolStep("default_step", "DefaultTool") }
                    }
                }
            }
        };

        WriteDef(definition);
        var catalog = new WorkflowCatalog(_tempDir);
        var compiler = new WorkflowCompiler(registry);
        var runner = new WorkflowRunner(catalog, compiler);

        var overridesJson = """{ "initialContext": { "ocrEngine": "unknown_engine" } }""";
        var result = await runner.RunAsync("branch_test", "IMG001", overridesJson);

        Assert.IsTrue(result.Success, $"Workflow failed: {result.Error}");
        Assert.IsTrue(_invokedTools.Contains("DefaultTool"),
            "Default branch should have been taken for unknown condition");
    }

    [TestMethod]
    public async Task Branch_NoMatchNoDefault_PassesThrough()
    {
        var registry = CreateTrackingRegistry("PreStep", "TesseractTool");

        var definition = new WorkflowDefinition
        {
            Id = "branch_test",
            Name = "Branch Passthrough Test",
            Steps = new List<WorkflowStep>
            {
                ToolStep("pre", "PreStep"),
                new()
                {
                    Id = "engine_branch", Type = "branch",
                    Condition = "$context.ocrEngine", Enabled = true,
                    Branches = new Dictionary<string, List<WorkflowStep>>
                    {
                        ["tesseract"] = new() { ToolStep("tess_step", "TesseractTool") },
                    }
                }
            }
        };

        WriteDef(definition);
        var catalog = new WorkflowCatalog(_tempDir);
        var compiler = new WorkflowCompiler(registry);
        var runner = new WorkflowRunner(catalog, compiler);

        var overridesJson = """{ "initialContext": { "ocrEngine": "paddle" } }""";
        var result = await runner.RunAsync("branch_test", "IMG001", overridesJson);

        Assert.IsTrue(result.Success, $"Workflow failed: {result.Error}");
        Assert.IsFalse(_invokedTools.Contains("TesseractTool"),
            "No branch should match, so TesseractTool should not run");
    }

    [TestMethod]
    public async Task Branch_MissingContextVar_UsesDefault()
    {
        var registry = CreateTrackingRegistry("PreStep", "DefaultTool");

        var definition = new WorkflowDefinition
        {
            Id = "branch_test",
            Name = "Branch Missing Context",
            Steps = new List<WorkflowStep>
            {
                ToolStep("pre", "PreStep"),
                new()
                {
                    Id = "engine_branch", Type = "branch",
                    Condition = "$context.ocrEngine", Enabled = true,
                    Branches = new Dictionary<string, List<WorkflowStep>>
                    {
                        ["default"] = new() { ToolStep("default_step", "DefaultTool") }
                    }
                }
            }
        };

        WriteDef(definition);
        var catalog = new WorkflowCatalog(_tempDir);
        var compiler = new WorkflowCompiler(registry);
        var runner = new WorkflowRunner(catalog, compiler);

        // No initialContext set, so ocrEngine context var will be null -> "default"
        var result = await runner.RunAsync("branch_test", "IMG001");

        Assert.IsTrue(result.Success, $"Workflow failed: {result.Error}");
        Assert.IsTrue(_invokedTools.Contains("DefaultTool"),
            "Default branch should be used when context var is missing");
    }

    [TestMethod]
    public void Compile_BranchWithNoBranches_SkipsStep()
    {
        var registry = CreateTrackingRegistry("PreStep");

        var definition = new WorkflowDefinition
        {
            Id = "branch_test",
            Name = "Empty Branch Test",
            Steps = new List<WorkflowStep>
            {
                ToolStep("pre", "PreStep"),
                new()
                {
                    Id = "empty_branch", Type = "branch",
                    Condition = "$context.x", Enabled = true,
                    Branches = null
                }
            }
        };

        var compiler = new WorkflowCompiler(registry);
        var (workflow, stepCount) = compiler.Compile(definition, null, new WorkflowContext());

        Assert.IsNotNull(workflow);
        Assert.AreEqual(1, stepCount, "Empty branch should be skipped during compilation");
    }

    [TestMethod]
    public async Task Branch_MultipleBranchSteps_AllExecuteInOrder()
    {
        var registry = CreateTrackingRegistry("PreStep", "BinTool", "MorphTool");

        var definition = new WorkflowDefinition
        {
            Id = "branch_test",
            Name = "Multi-step Branch",
            Steps = new List<WorkflowStep>
            {
                ToolStep("pre", "PreStep"),
                new()
                {
                    Id = "engine_branch", Type = "branch",
                    Condition = "$context.ocrEngine", Enabled = true,
                    Branches = new Dictionary<string, List<WorkflowStep>>
                    {
                        ["tesseract"] = new()
                        {
                            ToolStep("bin", "BinTool"),
                            ToolStep("morph", "MorphTool")
                        }
                    }
                }
            }
        };

        WriteDef(definition);
        var catalog = new WorkflowCatalog(_tempDir);
        var compiler = new WorkflowCompiler(registry);
        var runner = new WorkflowRunner(catalog, compiler);

        var overridesJson = """{ "initialContext": { "ocrEngine": "tesseract" } }""";
        var result = await runner.RunAsync("branch_test", "IMG001", overridesJson);

        Assert.IsTrue(result.Success, $"Workflow failed: {result.Error}");
        var binIdx = _invokedTools.IndexOf("BinTool");
        var morphIdx = _invokedTools.IndexOf("MorphTool");
        Assert.IsTrue(binIdx >= 0, "BinTool should have been invoked");
        Assert.IsTrue(morphIdx >= 0, "MorphTool should have been invoked");
        Assert.IsTrue(binIdx < morphIdx, "BinTool should execute before MorphTool");
    }

    [TestMethod]
    public async Task Branch_ContextOutputs_StoreCorrectly()
    {
        var registry = CreateTrackingRegistry("PreStep", "AnalyzeTool");

        var definition = new WorkflowDefinition
        {
            Id = "branch_test",
            Name = "Branch Output Test",
            Steps = new List<WorkflowStep>
            {
                ToolStep("pre", "PreStep"),
                new()
                {
                    Id = "engine_branch", Type = "branch",
                    Condition = "$context.ocrEngine", Enabled = true,
                    Branches = new Dictionary<string, List<WorkflowStep>>
                    {
                        ["tesseract"] = new()
                        {
                            new()
                            {
                                Id = "analyze", Type = "tool", Tool = "AnalyzeTool",
                                Enabled = true, Args = new(),
                                Passthrough = true,
                                Outputs = new Dictionary<string, string> { ["analyzed"] = "$result" }
                            }
                        }
                    }
                }
            }
        };

        WriteDef(definition);
        var catalog = new WorkflowCatalog(_tempDir);
        var compiler = new WorkflowCompiler(registry);
        var runner = new WorkflowRunner(catalog, compiler);

        var overridesJson = """{ "initialContext": { "ocrEngine": "tesseract" } }""";
        var result = await runner.RunAsync("branch_test", "IMG001", overridesJson);

        Assert.IsTrue(result.Success, $"Workflow failed: {result.Error}");
        Assert.IsTrue(result.Context.ContainsKey("analyzed"),
            "Context should contain the 'analyzed' output from branch step");
    }

    private ToolBindingRegistry CreateTrackingRegistry(params string[] toolNames)
    {
        var registry = new ToolBindingRegistry();
        foreach (var name in toolNames)
        {
            var captured = name;
            var func = AIFunctionFactory.Create(
                (string imageReference) =>
                {
                    _invokedTools.Add(captured);
                    return $"{captured.ToLower()}_processed_{imageReference}";
                },
                name);
            registry.Register(func);
        }
        return registry;
    }

    private static WorkflowStep ToolStep(string id, string tool) => new()
    {
        Id = id, Type = "tool", Tool = tool, Enabled = true, Args = new()
    };

    private WorkflowDefinition CreateBranchWorkflow(string conditionForTest)
    {
        return new WorkflowDefinition
        {
            Id = "branch_test",
            Name = "Branch Test Workflow",
            Steps = new List<WorkflowStep>
            {
                ToolStep("pre", "PreStep"),
                new()
                {
                    Id = "engine_branch",
                    Type = "branch",
                    Condition = "$context.ocrEngine",
                    Enabled = true,
                    Branches = new Dictionary<string, List<WorkflowStep>>
                    {
                        ["tesseract"] = new() { ToolStep("tess_step", "TesseractTool") },
                        ["paddle"] = new() { ToolStep("paddle_step", "PaddleTool") }
                    }
                }
            }
        };
    }

    private void WriteDef(WorkflowDefinition def)
    {
        var json = JsonSerializer.Serialize(def);
        File.WriteAllText(Path.Combine(_tempDir, $"{def.Id}.json"), json);
    }

}
