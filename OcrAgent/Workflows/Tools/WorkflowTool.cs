using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Agent;
using OcrAgent.Workflows.Engine;
using OcrAgent.Workflows.Model;

namespace OcrAgent.Workflows.Tools;

/// <summary>
/// Exposes catalog listing and workflow execution as AI-callable functions for an agent host.
/// </summary>
public sealed class WorkflowTool
{
    /// <summary>What the run-workflow tool does, for whichever surface describes it.</summary>
    /// <remarks>
    /// A host that cannot use the <see cref="AIFunction"/> built here still has to describe the same
    /// operation to a model, and two copies of the wording would drift. The constants are the single
    /// source: they are what the attributes below expand to, and what such a host reuses.
    /// </remarks>
    public const string RunWorkflowDescription =
        "Runs a predefined preprocessing workflow on an image. " +
        "Returns the final processed image reference, execution trace, and any output data (e.g. OCR results).";

    /// <summary>Description of the workflow id parameter.</summary>
    public const string WorkflowIdDescription = "The workflow ID (e.g., 'receipt_extract_v3').";

    /// <summary>Description of the input image reference parameter.</summary>
    public const string ImageReferenceDescription = "The image reference to process.";

    /// <summary>Description of the overrides parameter.</summary>
    public const string OverridesJsonDescription =
        "Optional JSON overrides to customize step parameters or disable steps.";

    private readonly WorkflowRunner _runner;
    private readonly WorkflowCatalog _catalog;

    /// <summary>The definitions this tool can run.</summary>
    /// <remarks>
    /// Read by a host that has to name a workflow somewhere other than in a tool call, so the name it
    /// uses comes from the definitions on disk rather than from a second list of its own.
    /// </remarks>
    public WorkflowCatalog Catalog => _catalog;

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
    /// <remarks>
    /// Exactly the three parameters a caller can supply, because this is the signature a tool schema is
    /// generated from and a progress sink is not something a model can pass. Hosts that have one call
    /// <see cref="RunWorkflowWithProgress"/> instead.
    /// </remarks>
    /// <param name="workflowId">Workflow definition id.</param>
    /// <param name="imageReference">Input image reference.</param>
    /// <param name="overridesJson">Optional JSON for step and budget overrides.</param>
    /// <returns>Formatted result or failure message.</returns>
    [Description(RunWorkflowDescription)]
    public Task<string> RunWorkflow(
        [Description(WorkflowIdDescription)] string workflowId,
        [Description(ImageReferenceDescription)] string imageReference,
        [Description(OverridesJsonDescription)] string? overridesJson = null)
        => RunWorkflowWithProgress(workflowId, imageReference, overridesJson, AgentProgressScope.Current);

    /// <summary>
    /// Runs a workflow, reporting each completed step, and returns the same summary as
    /// <see cref="RunWorkflow"/>.
    /// </summary>
    /// <param name="workflowId">Workflow definition id.</param>
    /// <param name="imageReference">Input image reference.</param>
    /// <param name="overridesJson">Optional JSON for step and budget overrides.</param>
    /// <param name="progress">Sink for step completions, or null to run without reporting.</param>
    /// <returns>Formatted result or failure message.</returns>
    public async Task<string> RunWorkflowWithProgress(
        string workflowId, string imageReference, string? overridesJson,
        IProgress<WorkflowStepProgress>? progress)
    {
        var result = await RunWorkflowDetailed(
            workflowId, imageReference, overridesJson, progress).ConfigureAwait(false);

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

    /// <summary>Runs a workflow and returns its typed result for REST and other application hosts.</summary>
    public Task<WorkflowRunResult> RunWorkflowDetailed(
        string workflowId,
        string imageReference,
        string? overridesJson = null,
        IProgress<WorkflowStepProgress>? progress = null) =>
        _runner.RunAsync(workflowId, imageReference, overridesJson, progress);

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
