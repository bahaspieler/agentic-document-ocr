using System.Diagnostics;
using Microsoft.Agents.AI.Workflows;
using OcrAgent.Workflows.Executors;
using OcrAgent.Workflows.Model;

namespace OcrAgent.Workflows.Engine;

/// <summary>
/// Turns a <see cref="WorkflowDefinition"/> into a linear Microsoft Agents <see cref="Workflow"/>: one executor per step, edges in order.
/// </summary>
public sealed class WorkflowCompiler
{
    private readonly ToolBindingRegistry _registry;

    /// <summary>
    /// Creates a compiler that resolves tools from the given registry.
    /// </summary>
    /// <param name="registry">Tool name to function bindings.</param>
    public WorkflowCompiler(ToolBindingRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Filters enabled steps, drops missing tools or empty branches, builds executors, then wires them head-to-tail.
    /// </summary>
    /// <param name="definition">Workflow JSON model.</param>
    /// <param name="overrides">Optional per-step enable/args and initial context from JSON.</param>
    /// <param name="context">Shared context passed into executors for arg resolution.</param>
    /// <returns>Built workflow and the number of steps kept after filtering.</returns>
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
                Debug.WriteLine($"[WARN] Skipping step '{step.Id}': tool '{step.Tool}' is not available.");
                continue;
            }
            if (step.Type == "branch" && (step.Branches == null || step.Branches.Count == 0))
            {
                Debug.WriteLine($"[WARN] Skipping branch step '{step.Id}': no branches defined.");
                continue;
            }
            availableSteps.Add(step);
        }

        if (availableSteps.Count == 0)
            throw new InvalidOperationException($"Workflow '{definition.Id}' has no steps with available tools.");

        // One graph node per step: either a single ToolStepExecutor or one BranchDispatcherExecutor (fan-out inside).
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
                // Linear chain: this executor receives the previous node's output ref as workflow input.
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

        // Assemble a simple pipeline: first executor is the entry; each AddEdge forwards to the next; last node is the workflow output.
        var builder = new WorkflowBuilder(executors[0]);
        for (int i = 0; i < executors.Count - 1; i++)
        {
            builder.AddEdge(executors[i], executors[i + 1]);
        }
        builder.WithOutputFrom(executors[^1]);

        return (builder.Build(), availableSteps.Count);
    }

    /// <summary>
    /// Builds a branch node: per case, a list of tool steps with merged args; skips disabled or unregistered tools.
    /// </summary>
    /// <param name="stepIndex">Position in the linear workflow for stable executor ids.</param>
    /// <param name="step">Branch step from the definition.</param>
    /// <param name="overrides">Optional per-step argument overrides.</param>
    /// <param name="context">Run context for the dispatcher.</param>
    /// <returns>Executor that selects a case and runs nested tools.</returns>
    private BranchDispatcherExecutor BuildBranchExecutor(
        int stepIndex, WorkflowStep step, WorkflowOverrides? overrides, WorkflowContext context)
    {
        var conditionVar = step.Condition ?? "";
        var branchSteps = new Dictionary<string, List<BranchToolStep>>();

        foreach (var (caseName, caseSteps) in step.Branches!)
        {
            var toolSteps = new List<BranchToolStep>();
            foreach (var branchStep in caseSteps)
            {
                if (!branchStep.Enabled) continue;
                if (!_registry.HasTool(branchStep.Tool))
                {
                    Debug.WriteLine($"[WARN] Branch '{caseName}': tool '{branchStep.Tool}' not available, skipping.");
                    continue;
                }

                var args = MergeArgs(branchStep.Args, overrides?.Steps?.GetValueOrDefault(branchStep.Id)?.Args);
                toolSteps.Add(new BranchToolStep(
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

    /// <summary>
    /// Returns definition steps that stay enabled after per-step override disables.
    /// </summary>
    /// <param name="definition">Source steps list.</param>
    /// <param name="overrides">May turn individual steps off.</param>
    /// <returns>Ordered list of enabled steps.</returns>
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

    /// <summary>
    /// Starts from definition args and overwrites keys present in <paramref name="overrideArgs"/>.
    /// </summary>
    /// <param name="defaults">Base arguments from the step.</param>
    /// <param name="overrideArgs">Optional replacement values by key.</param>
    /// <returns>Merged dictionary.</returns>
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
