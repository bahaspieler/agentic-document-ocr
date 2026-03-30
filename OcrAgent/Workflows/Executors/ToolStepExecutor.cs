using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using OcrAgent.Workflows.Engine;
using OcrAgent.Workflows.Events;

namespace OcrAgent.Workflows.Executors;

internal sealed class ToolStepExecutor : Executor
{
    private readonly ToolBindingRegistry _registry;
    private readonly WorkflowContext _workflowContext;
    private readonly string _toolName;
    private readonly Dictionary<string, object?> _defaultArgs;
    private readonly string _stepId;
    private readonly bool _passthrough;
    private readonly Dictionary<string, string>? _outputs;

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

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder builder)
    {
        builder.RouteBuilder.AddHandler<string, string>(HandleAsync);
        return builder;
    }

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

    private Dictionary<string, object?> ResolveArgs(string imageReference)
    {
        var args = new Dictionary<string, object?>(_defaultArgs.Count + 1);

        foreach (var (key, value) in _defaultArgs)
        {
            var strVal = value switch
            {
                string s => s,
                JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
                _ => null,
            };

            if (strVal != null && strVal.StartsWith("$context."))
            {
                var contextKey = strVal["$context.".Length..];
                args[key] = _workflowContext.Get(contextKey);
            }
            else
            {
                args[key] = value;
            }
        }

        args["imageReference"] = imageReference;
        return args;
    }

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
