using ModelContextProtocol;
using OcrAgent.Workflows.Model;

namespace OcrAgent.Mcp;

/// <summary>
/// Turns the engine's step reports into MCP progress notifications.
/// </summary>
/// <remarks>
/// Forwarding happens on the calling thread rather than through <see cref="Progress{T}"/>, which posts
/// to a captured context or the thread pool. A report queued that way can be delivered after the tool
/// has already returned its result, and a progress notification for a finished call is at best noise.
/// </remarks>
internal sealed class WorkflowProgressAdapter : IProgress<WorkflowStepProgress>
{
    private readonly IProgress<ProgressNotificationValue> _notifications;

    /// <summary>Wraps the sink the MCP SDK injected into a tool invocation.</summary>
    /// <param name="notifications">Receives one notification per step.</param>
    public WorkflowProgressAdapter(IProgress<ProgressNotificationValue> notifications)
    {
        _notifications = notifications;
    }

    /// <inheritdoc />
    public void Report(WorkflowStepProgress value)
    {
        // Total is sent on every notification because a client has no other way to learn how many steps
        // the compiled workflow has: the step count depends on the definition and on the overrides the
        // caller passed, so it is not known until the run is under way.
        _notifications.Report(new ProgressNotificationValue
        {
            Progress = value.Completed,
            Total = value.Total,
            Message = value.Success
                ? $"{value.StepId} ({value.ToolName})"
                : $"{value.StepId} ({value.ToolName}) failed",
        });
    }
}
