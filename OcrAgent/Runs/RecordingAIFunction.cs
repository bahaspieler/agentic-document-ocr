using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OcrAgent.Storage;

namespace OcrAgent.Runs;

/// <summary>
/// Wraps a tool so that calling it leaves a run record.
/// </summary>
/// <remarks>
/// Sitting on the tool itself rather than inside each host means the CLI, the chat agent, and the
/// MCP server all record the same way without knowing about it. Name, description, and JSON schema
/// come from the wrapped function untouched, so the tool surface a model sees does not change.
/// <para>
/// Calls raised while a job is already running are passed straight through: a workflow records
/// itself once, with its steps in the trace, rather than once per step.
/// </para>
/// </remarks>
internal sealed class RecordingAIFunction : DelegatingAIFunction
{
    // Workflow runs are recorded by WorkflowRunner, which has the typed result. The rest read
    // existing state and produce nothing worth a row.
    private static readonly HashSet<string> NotRecorded = new(StringComparer.OrdinalIgnoreCase)
    {
        "RunWorkflow",
        "ListWorkflows",
        "GetRun",
        "ListRuns",
    };

    private const int MaxArgumentsJsonChars = 4_000;

    private readonly IRunStore _runStore;
    private readonly IImageStore? _imageStore;

    private RecordingAIFunction(AIFunction inner, IRunStore runStore, IImageStore? imageStore)
        : base(inner)
    {
        _runStore = runStore;
        _imageStore = imageStore;
    }

    /// <summary>
    /// Wraps a tool for recording, or returns it unchanged when its calls are not recorded.
    /// </summary>
    /// <param name="inner">Tool to wrap.</param>
    /// <param name="runStore">Store that receives the records.</param>
    /// <param name="imageStore">Image store in use, named in the record.</param>
    /// <returns>The wrapped tool, or <paramref name="inner"/>.</returns>
    public static AIFunction Wrap(AIFunction inner, IRunStore? runStore, IImageStore? imageStore)
    {
        if (runStore == null || !runStore.IsEnabled || NotRecorded.Contains(inner.Name))
            return inner;

        return new RecordingAIFunction(inner, runStore, imageStore);
    }

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (RunScope.IsInsideJob)
            return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);

        using var scope = RunScope.Enter();

        var record = new RunRecord
        {
            RunId = RunId.NewId(),
            Mode = "tool",
            Name = Name,
            Source = RunScope.Source,
            InputReference = InputReferenceOf(arguments),
            StepCount = 1,
            StartTimeUtc = DateTimeOffset.UtcNow,
            StorageProvider = RunRecording.DescribeProvider(_imageStore),
            OutputDirectory = _imageStore?.GetActiveSubdirectory(),
            ArgumentsJson = DescribeArguments(arguments),
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            record.Success = true;
            record.OutputReference = OutputReferenceOf(result);

            await Complete(record, stopwatch).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            record.Success = false;
            record.Error = ex.Message;

            await Complete(record, stopwatch).ConfigureAwait(false);
            throw;
        }
    }

    private Task Complete(RunRecord record, Stopwatch stopwatch)
    {
        record.DurationMs = stopwatch.ElapsedMilliseconds;
        record.EndTimeUtc = record.StartTimeUtc.AddMilliseconds(record.DurationMs);

        return RunRecording.SafeSaveAsync(_runStore, record);
    }

    private static string InputReferenceOf(AIFunctionArguments arguments)
    {
        if (arguments.TryGetValue("imageReference", out var reference) && reference != null)
            return reference.ToString() ?? "";

        // RegisterImage takes a path instead, which is still the thing the call started from.
        if (arguments.TryGetValue("filePath", out var path) && path != null)
            return path.ToString() ?? "";

        return "";
    }

    /// <summary>
    /// Returns the result when it is an image reference, and nothing when it is data.
    /// </summary>
    /// <remarks>
    /// Preprocessing tools return the reference they produced, but analysis and OCR tools return
    /// JSON or text. References are short and limited to the characters
    /// <see cref="ImageReferenceGenerator.Normalize"/> accepts, which is enough to tell them apart.
    /// </remarks>
    private static string OutputReferenceOf(object? result)
    {
        var text = result?.ToString()?.Trim();

        if (string.IsNullOrEmpty(text) || text!.Length > 200)
            return "";

        try
        {
            ImageReferenceGenerator.Normalize(text);
            return text;
        }
        catch (ArgumentException)
        {
            return "";
        }
    }

    private static string? DescribeArguments(AIFunctionArguments arguments)
    {
        var described = arguments
            .Where(pair => pair.Key != "imageReference" && pair.Value != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value?.ToString());

        if (described.Count == 0)
            return null;

        try
        {
            var json = JsonSerializer.Serialize(described);
            return json.Length > MaxArgumentsJsonChars ? json[..MaxArgumentsJsonChars] : json;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
