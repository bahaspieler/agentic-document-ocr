using System.ComponentModel;
using ModelContextProtocol.Server;

namespace OcrAgent.Mcp;

/// <summary>
/// The flows worth naming, offered to clients as MCP prompts.
/// </summary>
/// <remarks>
/// Server instructions tell a model how to use the tools once it has decided what to do. A prompt is
/// the other half: the user picks one and the flow is spelled out, which is how someone who does not
/// know the tool surface gets a correct pipeline on the first try instead of a hand-stepped one.
/// <para>
/// Each of these is a flow the tools support but do not make obvious: that extraction is one call
/// rather than twelve, that manual work starts from a quality assessment and chains references, and
/// that a finished run can be read back from its id.
/// </para>
/// </remarks>
internal static class OcrAgentPrompts
{
    /// <summary>Fallback when the catalogue holds no receipt extraction workflow to name.</summary>
    private const string FallbackExtractionWorkflow = "receipt_extract_v3";

    /// <summary>Builds the prompt set for a host.</summary>
    /// <param name="workflowIds">Ids in the catalogue, used to name the extraction workflow.</param>
    /// <param name="runHistoryAvailable">
    /// Whether the run history tools are registered. The run prompt is left out when they are not,
    /// since it would otherwise describe a flow whose tools the client cannot see.
    /// </param>
    /// <returns>Prompts to register with the server.</returns>
    public static IEnumerable<McpServerPrompt> Create(
        IEnumerable<string> workflowIds, bool runHistoryAvailable)
    {
        var defaultWorkflowId = NewestExtractionWorkflow(workflowIds);

        yield return McpServerPrompt.Create(
            (
                [Description("Reference of the receipt image to extract.")] string imageReference,
                [Description("Workflow to run. Leave unset for the current recommended pipeline.")]
                string? workflowId = null
            ) => Reflow($"""
                Extract the structured data from the receipt image {imageReference}.

                Call RunWorkflow once with workflowId '{workflowId ?? defaultWorkflowId}' and that
                reference. The workflow performs the preprocessing and the OCR itself, so do not call
                the individual tools, and do not call it more than once. If it reports an error, tell
                me what the error was rather than retrying the steps by hand.

                Then report the run id, the reference of the processed image, and the fields the parse
                filled in: merchant, date, total, tax, and line items. Say which fields came back
                empty. Leave out step timings, filenames, and raw JSON.
                """),
            new McpServerPromptCreateOptions
            {
                Name = "extract_receipt",
                Description = "Extract merchant, date, totals, and line items from a receipt image in one workflow run.",
            });

        yield return McpServerPrompt.Create(
            (
                [Description("Reference of the image to improve.")] string imageReference
            ) => Reflow($"""
                Improve the image {imageReference} for OCR, choosing the steps yourself rather than
                running a predefined workflow.

                Start with AssessQuality on that reference and let what it reports decide what follows:
                fix geometry before appearance, and skip any correction the assessment says is not
                needed. Pass the quality profile it returns to every tool that accepts one.

                Chain the references. Each tool returns a new reference, and that reference is the
                input to the next tool. Do not pass {imageReference} to anything after the first step.

                Finish with the final reference and one line per step saying what it was for.
                """),
            new McpServerPromptCreateOptions
            {
                Name = "improve_image_for_ocr",
                Description = "Assess an image and apply only the corrections it needs, one tool at a time.",
            });

        if (!runHistoryAvailable)
            yield break;

        yield return McpServerPrompt.Create(
            (
                [Description("Run id to explain, as reported by a workflow or listed by ListRuns.")]
                string runId
            ) => Reflow($"""
                Explain what happened in run {runId}.

                Look it up with GetRun. Report what was run and on which input, whether it succeeded,
                how long it took, the reference it produced, and the steps in order with the tool each
                one used. Point out any step that failed and what it said.

                If there is no such run, say so and use ListRuns to show me the most recent ones.
                """),
            new McpServerPromptCreateOptions
            {
                Name = "explain_run",
                Description = "Read back a past job from its run id and describe what it did.",
            });
    }

    /// <summary>
    /// The highest-versioned <c>receipt_extract_vN</c> workflow in the catalogue.
    /// </summary>
    /// <remarks>
    /// Naming the workflow outright is the point of the prompt, and the catalogue has already reached
    /// v3, so the version to name is read from the definitions rather than written down here where it
    /// would keep pointing at an older pipeline after a newer one ships.
    /// </remarks>
    /// <param name="workflowIds">Ids in the catalogue.</param>
    /// <returns>The workflow to suggest.</returns>
    private static string NewestExtractionWorkflow(IEnumerable<string> workflowIds)
    {
        var newest = workflowIds
            .Select(id => (Id: id, Version: ExtractionVersionOf(id)))
            .Where(candidate => candidate.Version > 0)
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => candidate.Id)
            .FirstOrDefault();

        return newest ?? FallbackExtractionWorkflow;
    }

    /// <summary>
    /// Joins the lines of each paragraph, leaving the blank lines between them.
    /// </summary>
    /// <remarks>
    /// The text below is wrapped to fit this file, and a client shows a prompt to a person before it
    /// ever reaches a model. Without this, that person reads a paragraph broken every eleventh word.
    /// </remarks>
    /// <param name="text">Prompt text as written in source.</param>
    /// <returns>The same text with each paragraph on one line.</returns>
    private static string Reflow(string text) =>
        string.Join("\n\n", text
            .Replace("\r\n", "\n")
            .Split("\n\n")
            .Select(paragraph => string.Join(' ', paragraph
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()))));

    /// <summary>The version of a receipt extraction workflow id, or 0 when it is not one.</summary>
    /// <param name="workflowId">Catalogue id.</param>
    /// <returns>Trailing version number.</returns>
    private static int ExtractionVersionOf(string workflowId)
    {
        const string prefix = "receipt_extract_v";

        return workflowId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(workflowId[prefix.Length..], out var version)
                ? version
                : 0;
    }
}
