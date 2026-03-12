using Microsoft.Agents.AI.Workflows;
using OcrAgent.Workflows.Executors;
using OcrAgent.Workflows.Model;

namespace OcrAgent.Workflows.Engine;

public sealed class WorkflowCompiler
{
    private readonly ToolBindingRegistry _registry;

    public WorkflowCompiler(ToolBindingRegistry registry)
    {
        _registry = registry;
    }

    public Workflow Compile(WorkflowDefinition definition, WorkflowOverrides? overrides = null)
    {
        var enabledSteps = GetEnabledSteps(definition, overrides);
        if (enabledSteps.Count == 0)
            throw new InvalidOperationException($"Workflow '{definition.Id}' has no enabled steps.");

        foreach (var step in enabledSteps)
        {
            if (step.Type == "tool" && !_registry.HasTool(step.Tool))
                throw new InvalidOperationException(
                    $"Step '{step.Id}' references unknown tool '{step.Tool}'.");
        }

        var executors = new List<ToolStepExecutor>();
        for (int i = 0; i < enabledSteps.Count; i++)
        {
            var step = enabledSteps[i];
            var args = MergeArgs(step.Args, overrides?.Steps?.GetValueOrDefault(step.Id)?.Args);

            executors.Add(new ToolStepExecutor(
                executorId: $"Step_{i}_{step.Tool}",
                stepId: step.Id,
                toolName: step.Tool,
                defaultArgs: args,
                registry: _registry));
        }

        var builder = new WorkflowBuilder(executors[0]);
        for (int i = 0; i < executors.Count - 1; i++)
        {
            builder.AddEdge(executors[i], executors[i + 1]);
        }
        builder.WithOutputFrom(executors[^1]);

        return builder.Build();
    }

    private static List<WorkflowStep> GetEnabledSteps(
        WorkflowDefinition definition, WorkflowOverrides? overrides)
    {
        return definition.Steps.Where(s =>
        {
            if (!s.Enabled) return false;
            if (overrides?.Steps?.TryGetValue(s.Id, out var ov) == true
                && ov.Enabled == false) return false;
            return true;
        }).ToList();
    }

    private static Dictionary<string, object?> MergeArgs(
        Dictionary<string, object?> defaults,
        Dictionary<string, object?>? overrideArgs)
    {
        var merged = new Dictionary<string, object?>(defaults);
        if (overrideArgs != null)
        {
            foreach (var kv in overrideArgs)
                merged[kv.Key] = kv.Value;
        }
        return merged;
    }
}
