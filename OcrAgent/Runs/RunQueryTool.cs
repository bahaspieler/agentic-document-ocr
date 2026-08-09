using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;

namespace OcrAgent.Runs;

/// <summary>
/// Exposes run history as AI-callable functions, so an agent or MCP client can look up what has
/// already been executed.
/// </summary>
public sealed class RunQueryTool
{
    private readonly IRunStore _store;

    /// <summary>Creates the tool over a run store.</summary>
    /// <param name="store">Store holding the records.</param>
    public RunQueryTool(IRunStore store)
    {
        _store = store;
    }

    /// <summary>Reads one run back by id.</summary>
    /// <param name="runId">Id reported when the job ran.</param>
    /// <returns>Readable summary, or a message when nothing matches.</returns>
    [Description("Looks up a previously executed job by its run ID and returns what it did: the tool or workflow, the input and output image references, timing, and any error.")]
    public async Task<string> GetRun(
        [Description("The run ID reported when the job ran.")] string runId)
    {
        var record = await _store.GetAsync(runId);

        if (record == null)
            return $"No run found with ID '{runId}'.";

        return Describe(record, includeTrace: true);
    }

    /// <summary>Lists recent runs, newest first.</summary>
    /// <param name="top">Maximum number of runs to return.</param>
    /// <param name="mode">Optional filter: tool or workflow.</param>
    /// <returns>One line per run, or a message when there are none.</returns>
    [Description("Lists recently executed jobs, newest first, with their run IDs, what ran, and whether they succeeded.")]
    public async Task<string> ListRuns(
        [Description("How many runs to return (default 10).")] int top = 10,
        [Description("Optional filter: 'tool' for single plugin calls, 'workflow' for pipeline runs.")] string? mode = null)
    {
        var records = await _store.ListAsync(top, mode);

        if (records.Count == 0)
            return "No runs recorded yet.";

        var text = new StringBuilder();
        foreach (var record in records)
        {
            text.AppendLine(
                $"- {record.RunId} | {record.Mode}:{record.Name} | " +
                $"{(record.Success ? "OK" : "FAILED")} | {record.DurationMs}ms | " +
                $"{record.StartTimeUtc:yyyy-MM-dd HH:mm:ss}Z | {record.InputReference} -> {record.OutputReference}");
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>Builds the tools for listing and reading run history.</summary>
    /// <returns>Enumerable of function-backed tools.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.GetRun);
        yield return AIFunctionFactory.Create(this.ListRuns);
    }

    /// <summary>Renders one record as readable text for the CLI and the agent.</summary>
    /// <param name="record">Record to describe.</param>
    /// <param name="includeTrace">Include the workflow step trace when present.</param>
    /// <returns>Multi-line summary.</returns>
    public static string Describe(RunRecord record, bool includeTrace)
    {
        var text = new StringBuilder();

        text.AppendLine($"Run ID:    {record.RunId}");
        text.AppendLine($"Mode:      {record.Mode}");
        text.AppendLine($"Name:      {record.Name}{(record.Version == null ? "" : $" (v{record.Version})")}");
        text.AppendLine($"Source:    {record.Source}");
        text.AppendLine($"Started:   {record.StartTimeUtc:yyyy-MM-dd HH:mm:ss}Z");
        text.AppendLine($"Duration:  {record.DurationMs}ms");
        text.AppendLine($"Success:   {record.Success}");
        text.AppendLine($"Input:     {record.InputReference}");
        text.AppendLine($"Output:    {record.OutputReference}");
        text.AppendLine($"Steps:     {record.StepCount}");

        if (!string.IsNullOrEmpty(record.OutputDirectory))
            text.AppendLine($"Directory: {record.OutputDirectory}");

        if (!string.IsNullOrEmpty(record.StorageProvider))
            text.AppendLine($"Storage:   {record.StorageProvider}");

        if (!string.IsNullOrEmpty(record.ArgumentsJson))
            text.AppendLine($"Arguments: {record.ArgumentsJson}");

        if (!string.IsNullOrEmpty(record.Error))
            text.AppendLine($"Error:     {record.Error}");

        if (includeTrace && !string.IsNullOrEmpty(record.TraceJson))
            text.AppendLine($"Trace:     {record.TraceJson}");

        return text.ToString().TrimEnd();
    }
}
