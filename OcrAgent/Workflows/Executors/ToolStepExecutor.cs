using System.Diagnostics;
using Microsoft.Agents.AI.Workflows;
using OcrAgent.Workflows.Engine;
using OcrAgent.Workflows.Events;

namespace OcrAgent.Workflows.Executors;

/// <summary>
/// Runs a single bound tool step: resolves arguments, invokes the registry, writes optional context outputs, and emits <see cref="StepCompletedEvent"/>.
/// </summary>
internal sealed class ToolStepExecutor : Executor
{
    private readonly ToolBindingRegistry _registry;
    private readonly WorkflowContext _workflowContext;
    private readonly string _toolName;
    private readonly Dictionary<string, object?> _defaultArgs;
    private readonly string _stepId;
    private readonly bool _passthrough;
    private readonly Dictionary<string, string>? _outputs;

    /// <summary>
    /// Creates an executor for one workflow tool step.
    /// </summary>
    /// <param name="executorId">Host executor id.</param>
    /// <param name="stepId">Workflow step id for tracing.</param>
    /// <param name="toolName">Registered tool name.</param>
    /// <param name="defaultArgs">Argument template merged with context and image reference.</param>
    /// <param name="registry">Tool binding registry.</param>
    /// <param name="workflowContext">Mutable workflow context.</param>
    /// <param name="passthrough">When true, output reference stays the input image reference.</param>
    /// <param name="outputs">Optional context keys; use <c>$result</c> to store tool output string.</param>
    public ToolStepExecutor(
        string executorId,
        string stepId,
        string toolName,
        Dictionary<string, object?> defaultArgs,
        ToolBindingRegistry registry,
        WorkflowContext workflowContext,
        bool passthrough = false,
        Dictionary<string, string>? outputs = null)
        : base(executorId)
    {
        _stepId = stepId;
        _toolName = toolName;
        _defaultArgs = defaultArgs;
        _registry = registry;
        _workflowContext = workflowContext;
        _passthrough = passthrough;
        _outputs = outputs;
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
    /// Invokes the tool, updates context outputs, and returns the next image reference (or passthrough).
    /// </summary>
    /// <param name="imageReference">Current image reference from the workflow edge.</param>
    /// <param name="context">Workflow runtime context for events.</param>
    /// <returns>Output image reference or unchanged input when passthrough.</returns>
    private async ValueTask<string> HandleAsync(string imageReference, IWorkflowContext context)
    {
        var args = ResolveArgs(imageReference);

        var sw = Stopwatch.StartNew();
        string toolOutput;
        try
        {
            toolOutput = await _registry.InvokeAsync(_toolName, args);
            sw.Stop();

            WriteOutputs(toolOutput);

            string outputRef = _passthrough ? imageReference : toolOutput;

            await context.AddEventAsync(new StepCompletedEvent(
                _stepId, _toolName, imageReference, outputRef, sw.ElapsedMilliseconds, true, null));

            return outputRef;
        }
        catch (Exception ex)
        {
            sw.Stop();
            await context.AddEventAsync(new StepCompletedEvent(
                _stepId, _toolName, imageReference, "", sw.ElapsedMilliseconds, false, ex.Message));
            throw;
        }
    }

    /// <summary>
    /// Merges default args with workflow context and the current image reference.
    /// </summary>
    /// <param name="imageReference">Current image reference.</param>
    /// <returns>Resolved argument dictionary for the tool.</returns>
    private Dictionary<string, object?> ResolveArgs(string imageReference)
        => WorkflowArgResolver.Resolve(_defaultArgs, _workflowContext, imageReference);

    /// <summary>
    /// Writes configured output keys into workflow context from the tool result or literals.
    /// </summary>
    /// <param name="toolOutput">String returned by the tool.</param>
    private void WriteOutputs(string toolOutput)
    {
        if (_outputs == null) return;

        foreach (var (key, pattern) in _outputs)
        {
            var value = pattern == "$result" ? toolOutput : pattern;
            _workflowContext.Set(key, value);
        }
    }
}
