using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using OcrAgent.Workflows.Events;
using OcrAgent.Workflows.Model;

namespace OcrAgent.Workflows.Engine;

public sealed class WorkflowRunner
{
    private readonly WorkflowCatalog _catalog;
    private readonly WorkflowCompiler _compiler;

    public WorkflowRunner(WorkflowCatalog catalog, WorkflowCompiler compiler)
    {
        _catalog = catalog;
        _compiler = compiler;
    }

    public async Task<WorkflowRunResult> RunAsync(
        string workflowId, string imageRef, string? overridesJson = null)
    {
        var definition = _catalog.Get(workflowId);
        var overrides = overridesJson != null
            ? JsonSerializer.Deserialize<WorkflowOverrides>(overridesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            : null;

        var workflow = _compiler.Compile(definition, overrides);

        var result = new WorkflowRunResult
        {
            WorkflowId = definition.Id,
            Version = definition.Version,
            InputImageRef = imageRef,
        };

        var totalSw = Stopwatch.StartNew();
        int stepCount = definition.Steps.Count(s => s.Enabled);
        int completed = 0;

        try
        {
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
                        result.OutputImageRef = output.Data?.ToString() ?? "";
                        break;

                    case WorkflowErrorEvent error:
                        result.Error = error.Exception?.Message;
                        break;
                }
            }

            result.Success = string.IsNullOrEmpty(result.Error);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }

        totalSw.Stop();
        result.TotalDurationMs = totalSw.ElapsedMilliseconds;
        return result;
    }
}
