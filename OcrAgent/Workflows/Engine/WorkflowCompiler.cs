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

    public (Workflow Workflow, int StepCount) Compile(
        WorkflowDefinition definition, WorkflowOverrides? overrides, WorkflowContext context)
    {
        var enabledSteps = GetEnabledSteps(definition, overrides);
        if (enabledSteps.Count == 0)
            throw new InvalidOperationException($"Workflow '{definition.Id}' has no enabled steps.");

        var availableSteps = new List<WorkflowStep>();
        foreach (var step in enabledSteps)
        {
            if (step.Type == "tool" && !_registry.HasTool(step.Tool))
            {
                Console.WriteLine($"  [WARN] Skipping step '{step.Id}': tool '{step.Tool}' is not available.");
                continue;
            }
            if (step.Type == "branch" && (step.Branches == null || step.Branches.Count == 0))
            {
                Console.WriteLine($"  [WARN] Skipping branch step '{step.Id}': no branches defined.");
                continue;
            }
            availableSteps.Add(step);
        }

        if (availableSteps.Count == 0)
            throw new InvalidOperationException($"Workflow '{definition.Id}' has no steps with available tools.");

        var executors = new List<Executor>();
        for (int i = 0; i < availableSteps.Count; i++)
        {
            var step = availableSteps[i];
            var args = MergeArgs(step.Args, overrides?.Steps?.GetValueOrDefault(step.Id)?.Args);

            if (step.Type == "branch")
            {
                executors.Add(BuildBranchExecutor(i, step, overrides, context));
            }
            else
            {
                executors.Add(new ToolStepExecutor(
                    executorId: $"Step_{i}_{step.Tool}",
                    stepId: step.Id,
                    toolName: step.Tool,
                    defaultArgs: args,
                    registry: _registry,
                    workflowContext: context,
                    passthrough: step.Passthrough,
                    outputs: step.Outputs));
            }
        }

        var builder = new WorkflowBuilder(executors[0]);
        for (int i = 0; i < executors.Count - 1; i++)
        {
            builder.AddEdge(executors[i], executors[i + 1]);
        }
        builder.WithOutputFrom(executors[^1]);

        return (builder.Build(), availableSteps.Count);
    }

    private BranchDispatcherExecutor BuildBranchExecutor(
        int stepIndex, WorkflowStep step, WorkflowOverrides? overrides, WorkflowContext context)
    {
        var conditionVar = step.Condition ?? "";
        var branchSteps = new Dictionary<string, List<BranchDispatcherExecutor.BranchToolStep>>();

        foreach (var (caseName, caseSteps) in step.Branches!)
        {
            var toolSteps = new List<BranchDispatcherExecutor.BranchToolStep>();
            foreach (var branchStep in caseSteps)
            {
                if (!branchStep.Enabled) continue;
                if (!_registry.HasTool(branchStep.Tool))
                {
                    Console.WriteLine($"  [WARN] Branch '{caseName}': tool '{branchStep.Tool}' not available, skipping.");
                    continue;
                }

                var args = MergeArgs(branchStep.Args, overrides?.Steps?.GetValueOrDefault(branchStep.Id)?.Args);
                toolSteps.Add(new BranchDispatcherExecutor.BranchToolStep(
                    branchStep.Id, branchStep.Tool, args, branchStep.Passthrough, branchStep.Outputs));
            }
            if (toolSteps.Count > 0)
                branchSteps[caseName] = toolSteps;
        }

        return new BranchDispatcherExecutor(
            executorId: $"Step_{stepIndex}_branch_{step.Id}",
            stepId: step.Id,
            conditionVar: conditionVar,
            branches: branchSteps,
            registry: _registry,
            workflowContext: context);
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
