using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Workflows.Engine;

namespace OcrAgent.Workflows.Tools;

/// <summary>
/// Exposes catalog listing and workflow execution as AI-callable functions for an agent host.
/// </summary>
public sealed class WorkflowTool
{
    private readonly WorkflowRunner _runner;
    private readonly WorkflowCatalog _catalog;

    /// <summary>
    /// Creates the tool facade backed by a runner and catalog.
    /// </summary>
    /// <param name="runner">Executes workflows by id.</param>
    /// <param name="catalog">Lists available workflow definitions.</param>
    public WorkflowTool(WorkflowRunner runner, WorkflowCatalog catalog)
    {
        _runner = runner;
        _catalog = catalog;
    }

    /// <summary>
    /// Returns a text list of workflow ids, names, and descriptions from the catalog.
    /// </summary>
    /// <returns>Human-readable listing or a message when empty.</returns>
    [Description("Lists all available preprocessing workflows with their IDs, names, and descriptions.")]
    public string ListWorkflows()
    {
        var workflows = _catalog.List().ToList();
        if (workflows.Count == 0)
            return "No workflows available.";

        return string.Join("\n", workflows.Select(w =>
            $"- {w.Id}: {w.Name} -- {w.Description}"));
    }

    /// <summary>
    /// Runs a workflow and returns a summary string (trace, paths, context, errors).
    /// </summary>
    /// <param name="workflowId">Workflow definition id.</param>
    /// <param name="imageReference">Input image reference.</param>
    /// <param name="overridesJson">Optional JSON for step and budget overrides.</param>
    /// <returns>Formatted result or failure message.</returns>
    [Description("Runs a predefined preprocessing workflow on an image. " +
                 "Returns the final processed image reference, execution trace, and any output data (e.g. OCR results).")]
    public async Task<string> RunWorkflow(
        [Description("The workflow ID (e.g., 'receipt_extract_v3').")] string workflowId,
        [Description("The image reference to process.")] string imageReference,
        [Description("Optional JSON overrides to customize step parameters or disable steps.")] string? overridesJson = null)
    {
        var result = await _runner.RunAsync(workflowId, imageReference, overridesJson);

        if (!result.Success)
            return $"Workflow failed: {result.Error}";

        var summary = $"Workflow '{result.WorkflowId}' completed in {result.TotalDurationMs}ms.\n" +
                      $"Run ID: {result.RunId}\n" +
                      $"Output directory: {result.OutputDirectory}\n" +
                      $"Output image: {result.OutputImageRef}\n" +
                      $"Steps executed: {result.Trace.Count}\n" +
                      string.Join("\n", result.Trace.Select(t =>
                          $"  - {t.ToolName}: {t.InputRef} -> {t.OutputRef} ({t.DurationMs}ms)" +
                          (t.Success ? "" : $" [FAILED: {t.Error}]")));

        if (!string.IsNullOrEmpty(result.FinalOutput) && result.FinalOutput != result.OutputImageRef)
            summary += $"\n\nFinal output data:\n{result.FinalOutput}";

        if (result.Context.Count > 0)
        {
            summary += "\n\nWorkflow context:";
            foreach (var (key, value) in result.Context)
            {
                var display = value.Length > 200 ? value[..200] + "..." : value;
                summary += $"\n  {key}: {display}";
            }
        }

        return summary;
    }

    /// <summary>
    /// Builds <see cref="AITool"/> instances for listing and running workflows.
    /// </summary>
    /// <returns>Enumerable of function-backed tools.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.ListWorkflows);
        yield return AIFunctionFactory.Create(this.RunWorkflow);
    }
}
