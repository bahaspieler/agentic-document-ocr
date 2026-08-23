using System.Text.Json;
using Microsoft.Extensions.AI;
using OcrAgent.Agent;
using OcrAgent.Hosting;
using OcrAgent.Plugins;
using OcrAgent.Runs;
using OcrAgent.Sql;
using OcrAgent.Storage;
using OcrAgent.Workflows.Model;

namespace OcrAgent.Execution;

/// <summary>Shared execution path for HTTP, queue, and other application hosts.</summary>
public sealed class ExecutionCore
{
    private readonly PluginAggregator _aggregator;
    private readonly IReceiptStore? _receiptStore;
    private readonly AgentService? _agentService;
    private readonly ArtifactDiscoveryService _artifacts;

    public ExecutionCore(EngineHost host, AgentService? agentService = null)
        : this(
            host?.Aggregator ?? throw new ArgumentNullException(nameof(host)),
            host.Store,
            host.RunStore,
            host.ReceiptStore,
            agentService)
    {
    }

    public ExecutionCore(
        PluginAggregator aggregator,
        IImageStore store,
        IRunStore runStore,
        IReceiptStore? receiptStore = null,
        AgentService? agentService = null)
    {
        _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(runStore);
        _receiptStore = receiptStore;
        _agentService = agentService;
        _artifacts = new ArtifactDiscoveryService(store);
    }

    /// <summary>Executes one request and reports the same events included in the returned result.</summary>
    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        Action<AgentStreamEvent>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var events = new List<AgentStreamEvent>();
        var runs = new List<RunRecord>();

        void Capture(AgentStreamEvent streamEvent)
        {
            events.Add(streamEvent);
            if (streamEvent.Run is not null &&
                runs.All(run => run.RunId != streamEvent.Run.RunId))
                runs.Add(streamEvent.Run);
            onEvent?.Invoke(streamEvent);
        }

        using var sourceScope = RunScope.EnterSource(
            string.IsNullOrWhiteSpace(request.Source) ? "queue" : request.Source);
        using var conversationScope =
            request.Mode != ExecutionMode.Agent &&
            !string.IsNullOrWhiteSpace(request.ConversationId)
                ? RunScope.EnterConversation(request.ConversationId)
                : null;
        using var runEvents = request.Mode == ExecutionMode.Agent
            ? null
            : RunEventScope.Enter(run => Capture(AgentStreamEvent.RunCompleted(run)));

        WorkflowRunResult? workflow = null;
        string output;

        switch (request.Mode)
        {
            case ExecutionMode.Agent:
                if (_agentService is null)
                    throw new InvalidOperationException("Agent execution is not configured.");
                output = await _agentService.RunOneShotAsync(
                    Require(request.Message, "Message"), Capture, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case ExecutionMode.Tool:
                output = await RunToolAsync(request, cancellationToken).ConfigureAwait(false);
                break;

            case ExecutionMode.Workflow:
                var progress = new InlineProgress<WorkflowStepProgress>(
                    step => Capture(AgentStreamEvent.WorkflowStep(step)));
                workflow = await _aggregator.Workflows.RunWorkflowDetailed(
                    Require(request.Name, "Name"),
                    Require(request.ImageReference, "ImageReference"),
                    request.OverridesJson,
                    progress).ConfigureAwait(false);
                await EnrichReceiptIdAsync(workflow, cancellationToken).ConfigureAwait(false);
                output = workflow.FinalOutput ?? workflow.OutputImageRef;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request), "Unsupported execution mode.");
        }

        var primaryRun = SelectPrimaryRun(runs);
        var artifacts = new List<AgentArtifact>();
        foreach (var run in runs)
        {
            foreach (var artifact in await _artifacts
                .DiscoverAsync(run, cancellationToken).ConfigureAwait(false))
            {
                artifacts.Add(artifact);
                Capture(AgentStreamEvent.ArtifactAvailable(artifact));
            }
        }

        return new ExecutionResult(
            request.Mode,
            output,
            workflow,
            events.ToArray(),
            runs.ToArray(),
            primaryRun,
            artifacts.ToArray());
    }

    public static RunRecord? SelectPrimaryRun(IReadOnlyList<RunRecord> runs) =>
        runs.LastOrDefault(run =>
            run.Mode.Equals("workflow", StringComparison.OrdinalIgnoreCase))
        ?? runs.LastOrDefault();

    private async Task<string> RunToolAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var name = Require(request.Name, "Name");
        var function = _aggregator.GetAllTools()
            .OfType<AIFunction>()
            .FirstOrDefault(candidate =>
                candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Tool '{name}' was not found.");

        var arguments = new AIFunctionArguments();
        if (request.Arguments is not null)
        {
            foreach (var (argumentName, value) in request.Arguments)
                arguments[argumentName] =
                    JsonSerializer.Deserialize<object?>(value.GetRawText());
        }
        if (!string.IsNullOrWhiteSpace(request.ImageReference) &&
            !arguments.ContainsKey("imageReference"))
            arguments["imageReference"] = request.ImageReference;

        var result = await function.InvokeAsync(arguments, cancellationToken)
            .ConfigureAwait(false);
        return result?.ToString() ?? "";
    }

    private async Task EnrichReceiptIdAsync(
        WorkflowRunResult workflow,
        CancellationToken cancellationToken)
    {
        if (_receiptStore is not { IsEnabled: true })
            return;

        var receipts = await _receiptStore.QueryAsync(
            top: 200, cancellationToken: cancellationToken).ConfigureAwait(false);
        workflow.ReceiptId = receipts.FirstOrDefault(
            receipt => receipt.RunId == workflow.RunId)?.ReceiptId
            ?? workflow.ReceiptId;
    }

    private static string Require(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{name} is required.", name);

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
