using System;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using OcrAgent.Storage;
using OcrAgent.Workflows.Events;
using OcrAgent.Workflows.Model;

namespace OcrAgent.Workflows.Engine;

public sealed class WorkflowRunner
{
    private readonly WorkflowCatalog _catalog;
    private readonly WorkflowCompiler _compiler;
    private readonly IImageStore? _store;

    public WorkflowRunner(WorkflowCatalog catalog, WorkflowCompiler compiler, IImageStore? store = null)
    {
        _catalog = catalog;
        _compiler = compiler;
        _store = store;
    }

    public async Task<WorkflowRunResult> RunAsync(
        string workflowId, string imageRef, string? overridesJson = null)
    {
        var definition = _catalog.Get(workflowId);
        var overrides = overridesJson != null
            ? JsonSerializer.Deserialize<WorkflowOverrides>(overridesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            : null;

        var runId = Guid.CreateVersion7().ToString("D");
        var subdirectory = $"wf_{definition.Id}/{runId}";

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

            await foreach (var evt in run.WatchStreamAsync())
            {
                switch (evt)
                {
                    case StepCompletedEvent step:
                        completed++;
                        Console.WriteLine(
                            $"  [{completed}/{stepCount}] {step.ToolName}: " +
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
                        break;

                    case WorkflowOutputEvent output:
                        result.FinalOutput = output.Data?.ToString() ?? "";
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
}
