using Microsoft.Agents.AI.Workflows;
using OcrAgent.Workflows.Engine;
using OcrAgent.Workflows.Events;
using System.Diagnostics;

namespace OcrAgent.Workflows.Executors;

internal sealed class ToolStepExecutor : Executor
{
    private readonly ToolBindingRegistry _registry;
    private readonly string _toolName;
    private readonly Dictionary<string, object?> _defaultArgs;
    private readonly string _stepId;

    public ToolStepExecutor(
        string executorId,
        string stepId,
        string toolName,
        Dictionary<string, object?> defaultArgs,
        ToolBindingRegistry registry)
        : base(executorId)
    {
        _stepId = stepId;
        _toolName = toolName;
        _defaultArgs = defaultArgs;
        _registry = registry;
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder builder)
    {
        builder.RouteBuilder.AddHandler<string, string>(HandleAsync);
        return builder;
    }

    private async ValueTask<string> HandleAsync(string imageReference, IWorkflowContext context)
    {
        var args = new Dictionary<string, object?>(_defaultArgs)
        {
            ["imageReference"] = imageReference
        };

        var sw = Stopwatch.StartNew();
        string outputRef;
        try
        {
            outputRef = await _registry.InvokeAsync(_toolName, args);
            sw.Stop();
            await context.AddEventAsync(new StepCompletedEvent(
                _stepId, _toolName, imageReference, outputRef, sw.ElapsedMilliseconds, true, null));
        }
        catch (Exception ex)
        {
            sw.Stop();
            await context.AddEventAsync(new StepCompletedEvent(
                _stepId, _toolName, imageReference, "", sw.ElapsedMilliseconds, false, ex.Message));
            throw;
        }

        return outputRef;
    }
}
