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
}