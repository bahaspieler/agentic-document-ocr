using System;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using OcrAgent.Runs;
using OcrAgent.Storage;
using OcrAgent.Workflows.Events;
using OcrAgent.Workflows.Model;

namespace OcrAgent.Workflows.Engine;

/// <summary>
/// Runs a catalog workflow for one image: scoped storage, compile, stream events into trace and result fields.
/// </summary>
public sealed class WorkflowRunner
{
    private readonly WorkflowCatalog _catalog;
    private readonly WorkflowCompiler _compiler;
    private readonly IImageStore? _store;
    private readonly IRunStore? _runStore;

    /// <summary>
    /// Creates a runner using the given catalog, compiler, and optional image store for per-run paths.
    /// </summary>
    /// <param name="catalog">Resolves workflow id to definition.</param>
    /// <param name="compiler">Builds the executable graph.</param>
    /// <param name="store">When set, receives a per-run subdirectory for writes.</param>
    /// <param name="runStore">When set, receives one record per completed run.</param>
    public WorkflowRunner(
        WorkflowCatalog catalog, WorkflowCompiler compiler, IImageStore? store = null, IRunStore? runStore = null)
    {
        _catalog = catalog;
        _compiler = compiler;
        _store = store;
        _runStore = runStore;
    }

    /// <summary>
    /// Executes one run and records it.
    /// </summary>
    /// <remarks>
    /// The run is marked as a job for its whole duration, so the individual steps it drives through
    /// the tool bindings are not recorded a second time in their own right.
    /// </remarks>
    /// <param name="workflowId">Catalog workflow id.</param>
    /// <param name="imageRef">Input image reference for the pipeline.</param>
    /// <param name="overridesJson">Optional JSON for <see cref="WorkflowOverrides"/>.</param>
    /// <param name="progress">
    /// When set, receives one report per completed step while the run is still going. Reports are made
    /// on the thread draining the event stream, so a sink that blocks slows the run down.
    /// </param>
    /// <returns>Trace, final text output, context snapshot, and timing.</returns>
    public async Task<WorkflowRunResult> RunAsync(
        string workflowId, string imageRef, string? overridesJson = null,
        IProgress<WorkflowStepProgress>? progress = null)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        using var scope = RunScope.Enter();

        try
        {
            var result = await ExecuteAsync(workflowId, imageRef, overridesJson, progress);
            await RunRecording.SafeSaveAsync(_runStore, ToRecord(result, imageRef, startedUtc));
            return result;
        }
        catch (Exception ex)
        {
            // Reached when the workflow could not start at all, such as an unknown id or malformed
            // overrides. The attempt is still worth a row.
            await RunRecording.SafeSaveAsync(_runStore, new RunRecord
            {
                RunId = RunId.NewId(),
                Mode = "workflow",
                Name = workflowId,
                Source = RunScope.Source,
                ConversationId = RunScope.ConversationId,
                InputReference = imageRef,
                Success = false,
                Error = ex.Message,
                StartTimeUtc = startedUtc,
                EndTimeUtc = DateTimeOffset.UtcNow,
                DurationMs = (long)(DateTimeOffset.UtcNow - startedUtc).TotalMilliseconds,
                StorageProvider = RunRecording.DescribeProvider(_store),
            });

            throw;
        }
    }

    /// <summary>
    /// Runs the pipeline: new run id, optional overrides JSON, compiles, streams step events into <see cref="WorkflowRunResult.Trace"/>.
    /// </summary>
    private async Task<WorkflowRunResult> ExecuteAsync(
        string workflowId, string imageRef, string? overridesJson,
        IProgress<WorkflowStepProgress>? progress)
    {
        var definition = _catalog.Get(workflowId);
        var overrides = overridesJson != null
            ? JsonSerializer.Deserialize<WorkflowOverrides>(overridesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            : null;

        var runId = RunId.NewId();
        var subdirectory = Path.Combine($"wf_{definition.Id}", runId);

        var result = new WorkflowRunResult
        {
            WorkflowId = definition.Id,
            Version = definition.Version,
            InputImageRef = imageRef,
            RunId = runId,
            OutputDirectory = subdirectory,
        };

        var totalSw = Stopwatch.StartNew();
        int completed = 0;

        // Isolate this run's uploads/downloads under wf_<id>/<runId> when a store is configured.
        _store?.SetActiveSubdirectory(subdirectory);
        try
        {
            var context = new WorkflowContext();

            context.Set("workflowId", definition.Id);
            context.Set("runId", runId);

            if (overrides?.InitialContext != null)
            {
                foreach (var (key, value) in overrides.InitialContext)
                    context.Set(key, value);
            }

            if (definition.Budget?.MaxVariants > 0)
                context.Set("maxVariants", definition.Budget.MaxVariants.ToString());

            var (workflow, stepCount) = _compiler.Compile(definition, overrides, context);

            var run = await InProcessExecution.RunStreamingAsync(workflow, imageRef);

            // StepCompletedEvent rows populate Trace and debug progress; output/error events fill the result object.
            await foreach (var evt in run.WatchStreamAsync())
            {
                switch (evt)
                {
                    case StepCompletedEvent step:
                        completed++;
                        Debug.WriteLine(
                            $"[{completed}/{stepCount}] {step.ToolName}: " +
                            $"{step.InputRef} -> {step.OutputRef} ({step.DurationMs}ms)");
                        result.Trace.Add(new StepTraceEntry
                        {
                            StepId = step.StepId,
                            ToolName = step.ToolName,
                            InputRef = step.InputRef,
                            OutputRef = step.OutputRef,
                            DurationMs = step.DurationMs,
                            Success = step.Success,
                            Error = step.Error,
                        });
                        progress?.Report(new WorkflowStepProgress(
                            completed,
                            stepCount,
                            step.StepId,
                            step.ToolName,
                            step.Success,
                            step.InputRef,
                            step.OutputRef,
                            step.DurationMs,
                            step.Error));
                        break;

                    case WorkflowOutputEvent output:
                        result.FinalOutput = output.Data?.ToString() ?? "";
                        result.ReceiptId = TryGetReceiptId(result.FinalOutput);
                        break;

                    case WorkflowErrorEvent error:
                        result.Error = error.Exception?.Message;
                        break;
                }
            }

            // Determine OutputImageRef from last non-passthrough step trace
            var lastImageStep = result.Trace.LastOrDefault(t => t.Success && t.OutputRef != t.InputRef);
            result.OutputImageRef = lastImageStep?.OutputRef
                                    ?? result.Trace.LastOrDefault(t => t.Success)?.OutputRef
                                    ?? "";

            result.Context = new Dictionary<string, string>(context.GetAll());
            if (result.Context.TryGetValue("receiptJson", out var receiptJson))
                result.ReceiptId ??= TryGetReceiptId(receiptJson);
            result.Success = string.IsNullOrEmpty(result.Error);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }
        finally
        {
            _store?.SetActiveSubdirectory(null);
        }

        totalSw.Stop();
        result.TotalDurationMs = totalSw.ElapsedMilliseconds;
        return result;
    }

    private static string? TryGetReceiptId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '{')
            return null;
        try
        {
            using var json = JsonDocument.Parse(value);
            return json.RootElement.TryGetProperty("receiptId", out var id)
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Maps a finished run onto the record written to the run store.</summary>
    /// <param name="result">Outcome of the run.</param>
    /// <param name="imageRef">Reference the run started from.</param>
    /// <param name="startedUtc">When the run began.</param>
    /// <returns>Record describing the run, with the step trace inlined as JSON.</returns>
    private RunRecord ToRecord(WorkflowRunResult result, string imageRef, DateTimeOffset startedUtc) => new()
    {
        RunId = result.RunId,
        Mode = "workflow",
        Name = result.WorkflowId,
        Version = result.Version,
        Source = RunScope.Source,
        ConversationId = RunScope.ConversationId,
        TurnIdempotencyKey = RunScope.TurnIdempotencyKey,
        InputReference = imageRef,
        OutputReference = result.OutputImageRef,
        OutputDirectory = result.OutputDirectory,
        StepCount = result.Trace.Count,
        Success = result.Success,
        Error = result.Error,
        StartTimeUtc = startedUtc,
        EndTimeUtc = startedUtc.AddMilliseconds(result.TotalDurationMs),
        DurationMs = result.TotalDurationMs,
        StorageProvider = RunRecording.DescribeProvider(_store),
        TraceJson = JsonSerializer.Serialize(result.Trace),
    };
}
