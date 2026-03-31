using System.Diagnostics;
using Microsoft.Agents.AI.Workflows;
using OcrAgent.Workflows.Engine;
using OcrAgent.Workflows.Events;
using OcrAgent.Workflows.Model;

namespace OcrAgent.Workflows.Executors;

/// <summary>
/// Executor that reads a context variable and dispatches to the matching branch.
/// Each branch is a list of tool steps executed sequentially via the ToolBindingRegistry.
/// This encapsulates branching within a single executor node in the linear workflow graph.
/// </summary>
internal sealed class BranchDispatcherExecutor : Executor
{
    private readonly WorkflowContext _workflowContext;
    private readonly ToolBindingRegistry _registry;
    private readonly string _stepId;
    private readonly string _conditionVar;
    private readonly Dictionary<string, List<BranchToolStep>> _branches;

    /// <summary>
    /// One tool invocation inside a branch: id, tool name, args, passthrough, and optional context outputs.
    /// </summary>
    /// <param name="StepId">Step id for tracing.</param>
    /// <param name="ToolName">Registered tool name.</param>
    /// <param name="Args">Argument template for the tool.</param>
    /// <param name="Passthrough">When true, branch keeps the previous image reference as output.</param>
    /// <param name="Outputs">Optional context keys; <c>$result</c> maps to tool output.</param>
    internal record BranchToolStep(
        string StepId, string ToolName,
        Dictionary<string, object?> Args,
        bool Passthrough,
        Dictionary<string, string>? Outputs);

    /// <summary>
    /// Creates a branch dispatcher for the given condition variable and branch map.
    /// </summary>
    /// <param name="executorId">Host executor id.</param>
    /// <param name="stepId">Workflow step id for tracing.</param>
    /// <param name="conditionVar">Context key or <c>$context.key</c> form.</param>
    /// <param name="branches">Case value to ordered tool steps.</param>
    /// <param name="registry">Tool binding registry.</param>
    /// <param name="workflowContext">Mutable workflow context.</param>
    public BranchDispatcherExecutor(
        string executorId,
        string stepId,
        string conditionVar,
        Dictionary<string, List<BranchToolStep>> branches,
        ToolBindingRegistry registry,
        WorkflowContext workflowContext)
        : base(executorId)
    {
        _stepId = stepId;
        _conditionVar = conditionVar;
        _branches = branches;
        _registry = registry;
        _workflowContext = workflowContext;
    }

    /// <summary>
    /// Wires the image-reference handler into the protocol route builder.
    /// </summary>
    /// <param name="builder">Protocol builder from the host.</param>
    /// <returns>The same builder instance.</returns>
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder builder)
    {
        builder.RouteBuilder.AddHandler<string, string>(HandleAsync);
        return builder;
    }

    /// <summary>
    /// Reads the condition from context, runs the matching branch tools in order, and returns the final image reference.
    /// </summary>
    /// <param name="imageReference">Current image reference from the workflow edge.</param>
    /// <param name="context">Workflow runtime context for events.</param>
    /// <returns>Image reference after the branch chain, or input if no branch matches.</returns>
    private async ValueTask<string> HandleAsync(string imageReference, IWorkflowContext context)
    {
        // Strip $context. so lookup uses the same key as WorkflowContext.Get (plain key).
        var conditionKey = _conditionVar.StartsWith("$context.")
            ? _conditionVar["$context.".Length..]
            : _conditionVar;

        var conditionValue = _workflowContext.Get(conditionKey) ?? "default";
        Debug.WriteLine($"[Branch] condition={_conditionVar} value=\"{conditionValue}\"");

        if (!_branches.TryGetValue(conditionValue, out var steps))
        {
            if (!_branches.TryGetValue("default", out steps))
            {
                Debug.WriteLine($"[Branch] No matching branch for \"{conditionValue}\", passing through");
                return imageReference;
            }
            Debug.WriteLine($"[Branch] Using default branch");
        }
        else
        {
            Debug.WriteLine($"[Branch] Taking \"{conditionValue}\" branch ({steps.Count} steps)");
        }

        string currentRef = imageReference;
        foreach (var step in steps)
        {
            var stepInput = currentRef;
            var args = ResolveArgs(step.Args, currentRef);
            var sw = Stopwatch.StartNew();

            try
            {
                var output = await _registry.InvokeAsync(step.ToolName, args);
                sw.Stop();

                if (step.Outputs != null)
                {
                    foreach (var (key, pattern) in step.Outputs)
                    {
                        var value = pattern == "$result" ? output : pattern;
                        _workflowContext.Set(key, value);
                    }
                }

                currentRef = step.Passthrough ? currentRef : output;

                await context.AddEventAsync(new StepCompletedEvent(
                    step.StepId, step.ToolName, stepInput, currentRef,
                    sw.ElapsedMilliseconds, true, null));
            }
            catch (Exception ex)
            {
                sw.Stop();
                await context.AddEventAsync(new StepCompletedEvent(
                    step.StepId, step.ToolName, stepInput, "",
                    sw.ElapsedMilliseconds, false, ex.Message));
                throw;
            }
        }

        return currentRef;
    }

    /// <summary>
    /// Merges branch step defaults with workflow context and the current image reference.
    /// </summary>
    /// <param name="defaults">Argument template from the branch step.</param>
    /// <param name="imageReference">Current image reference.</param>
    /// <returns>Resolved argument dictionary for the tool.</returns>
    private Dictionary<string, object?> ResolveArgs(Dictionary<string, object?> defaults, string imageReference)
        => WorkflowArgResolver.Resolve(defaults, _workflowContext, imageReference);
}
