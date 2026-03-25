using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OcrAgent.Workflows.Engine;
using OcrAgent.Workflows.Model;

// Note: WorkflowCompiler.Compile now requires a WorkflowContext parameter

namespace UnitTests.Workflows;

[TestClass]
public class WorkflowCompilerTests
{
    private static ToolBindingRegistry CreateRegistryWithTools(params string[] toolNames)
    {
        var registry = new ToolBindingRegistry();
        foreach (var name in toolNames)
        {
            var func = AIFunctionFactory.Create((string imageReference) => $"processed_{imageReference}", name);
            registry.Register(func);
        }
        return registry;
    }

    private static WorkflowDefinition CreateDefinition(params (string id, string tool, bool enabled)[] steps)
    {
        return new WorkflowDefinition
        {
            Id = "test_workflow",
            Name = "Test",
            Steps = steps.Select(s => new WorkflowStep
            {
                Id = s.id,
                Type = "tool",
                Tool = s.tool,
                Enabled = s.enabled,
                Args = new Dictionary<string, object?>()
            }).ToList()
        };
    }

    [TestMethod]
    public void Compile_ThreeStepWorkflow_DoesNotThrow()
    {
        var registry = CreateRegistryWithTools("ToolA", "ToolB", "ToolC");
        var compiler = new WorkflowCompiler(registry);
        var definition = CreateDefinition(
            ("s1", "ToolA", true),
            ("s2", "ToolB", true),
            ("s3", "ToolC", true));

        var (workflow, stepCount) = compiler.Compile(definition, null, new WorkflowContext());

        Assert.IsNotNull(workflow);
        Assert.AreEqual(3, stepCount);
    }


    [TestMethod]
    public void Compile_DisabledStepIsExcluded()
    {
        var registry = CreateRegistryWithTools("ToolA", "ToolB");
        var compiler = new WorkflowCompiler(registry);
        var definition = CreateDefinition(
            ("s1", "ToolA", true),
            ("s2", "ToolB", false));

        var (workflow, stepCount) = compiler.Compile(definition, null, new WorkflowContext());

        Assert.IsNotNull(workflow);
        Assert.AreEqual(1, stepCount);
    }
    [TestMethod]
    public void Compile_OverrideDisablesStep()
    {
        var registry = CreateRegistryWithTools("ToolA", "ToolB");
        var compiler = new WorkflowCompiler(registry);
        var definition = CreateDefinition(
            ("s1", "ToolA", true),
            ("s2", "ToolB", true));

        var overrides = new WorkflowOverrides
        {
            Steps = new Dictionary<string, StepOverride>
            {
                ["s2"] = new StepOverride { Enabled = false }
            }
        };

        var (workflow, _) = compiler.Compile(definition, overrides, new WorkflowContext());

        Assert.IsNotNull(workflow);
    }

    [TestMethod]
    public void Compile_OverrideMergesArgs()
    {
        var registry = CreateRegistryWithTools("ToolA");
        var compiler = new WorkflowCompiler(registry);
        var definition = new WorkflowDefinition
        {
            Id = "test",
            Name = "Test",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "s1",
                    Type = "tool",
                    Tool = "ToolA",
                    Enabled = true,
                    Args = new Dictionary<string, object?> { ["method"] = "default", ["strength"] = "low" }
                }
            }
        };

        var overrides = new WorkflowOverrides
        {
            Steps = new Dictionary<string, StepOverride>
            {
                ["s1"] = new StepOverride
                {
                    Args = new Dictionary<string, object?> { ["strength"] = "high" }
                }
            }
        };

        var (workflow, _) = compiler.Compile(definition, overrides, new WorkflowContext());
        Assert.IsNotNull(workflow);
    }

    [TestMethod]
    public void Compile_UnavailableTool_SkipsStepAndCompiles()
    {
        var registry = CreateRegistryWithTools("ToolA");
        var compiler = new WorkflowCompiler(registry);
        var definition = CreateDefinition(
            ("s1", "ToolA", true),
            ("s2", "NonExistentTool", true));

        var (workflow, stepCount) = compiler.Compile(definition, null, new WorkflowContext());

        Assert.IsNotNull(workflow);
        Assert.AreEqual(1, stepCount);
    }

    [TestMethod]
    public void Compile_AllToolsUnavailable_ThrowsInvalidOperation()
    {
        var registry = CreateRegistryWithTools("ToolA");
        var compiler = new WorkflowCompiler(registry);
        var definition = CreateDefinition(("s1", "NonExistentTool", true));
        bool thrown = false;

        try
        {
            compiler.Compile(definition, null, new WorkflowContext());
        }
        catch (InvalidOperationException ex)
        {
            thrown = true;
            Assert.IsTrue(ex.Message.Contains("no steps with available tools"));
        }

        Assert.IsTrue(thrown, "Expected InvalidOperationException when all tools unavailable");
    }


    [TestMethod]
    public void Compile_NoEnabledSteps_ThrowsInvalidOperation()
    {
        var registry = CreateRegistryWithTools("ToolA");
        var compiler = new WorkflowCompiler(registry);
        var definition = CreateDefinition(("s1", "ToolA", false));
        bool thrown = false;

        try
        {
            compiler.Compile(definition, null, new WorkflowContext());
        }
        catch (InvalidOperationException ex)
        {
            thrown = true;
            Assert.IsTrue(ex.Message.Contains("no enabled steps"));
        }

        Assert.IsTrue(thrown, "Expected InvalidOperationException for no enabled steps");
    }

}