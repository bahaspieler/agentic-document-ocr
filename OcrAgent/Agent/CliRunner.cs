using OcrAgent.Plugins;
using OcrAgent.Runs;
using OcrAgent.Storage;
using OcrAgent.Workflows.Engine;
using OcrAgent.Workflows.Model;

namespace OcrAgent.Agent;

/// <summary>
/// Non-interactive command mode: workflows, single-tool calls, image registration, run history, and listings.
/// </summary>
public sealed class CliRunner
{
    private readonly IImageStore _store;
    private readonly PluginAggregator _aggregator;
    private readonly ToolBindingRegistry _toolRegistry;
    private readonly WorkflowRunner _workflowRunner;
    private readonly WorkflowCatalog _workflowCatalog;
    private readonly IRunStore _runStore;

    /// <summary>Loads workflow definitions from the app base and wires a runner to the store and tools.</summary>
    /// <param name="store">Backing store for images.</param>
    /// <param name="aggregator">Supplies the tool registry used by workflows and the tool command.</param>
    /// <param name="runStore">Receives run records and answers the <c>runs</c> command.</param>
    public CliRunner(IImageStore store, PluginAggregator aggregator, IRunStore? runStore = null)
    {
        _store = store;
        _aggregator = aggregator;
        _toolRegistry = aggregator.GetToolRegistry();
        _runStore = runStore ?? NullRunStore.Instance;

        var definitionsPath = Path.Combine(AppContext.BaseDirectory, "Workflows", "Definitions");
        _workflowCatalog = new WorkflowCatalog(definitionsPath);
        var compiler = new WorkflowCompiler(_toolRegistry);
        _workflowRunner = new WorkflowRunner(_workflowCatalog, compiler, store, _runStore);
    }

    /// <summary>Routes on <paramref name="args"/>[0] to workflow, tool, register, list, or help.</summary>
    /// <param name="args">Arguments after <c>dotnet run --</c>; first token is the subcommand.</param>
    /// <returns>Exit code (0 on success).</returns>
    public async Task<int> RunAsync(string[] args)
    {
        var command = args[0].ToLowerInvariant();

        return command switch
        {
            "workflow" or "wf" => await RunWorkflowCommand(args[1..]),
            "tool" => await RunToolCommand(args[1..]),
            "register" => await RunRegisterCommand(args[1..]),
            "runs" => await RunRunsCommand(args[1..]),
            "list-tools" => RunListTools(),
            "list-workflows" => RunListWorkflows(),
            "help" => ShowHelp(),
            _ => ShowHelp($"Unknown command: '{command}'")
        };
    }

    private async Task<int> RunWorkflowCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run -- workflow <workflowId> <imageReference|filePath> [options]");
            Console.WriteLine("  Options:");
            Console.WriteLine("    --overrides <json>           Custom workflow overrides JSON");
            Console.WriteLine("  Examples:");
            Console.WriteLine("    dotnet run -- workflow receipt_extract_v3 10");
            Console.WriteLine("    dotnet run -- workflow receipt_extract_v3 ./receipt.jpg");
            return 1;
        }

        var workflowId = args[0];
        var imageArg = args[1];
        string? overridesJson = null;

        for (int i = 2; i < args.Length - 1; i++)
        {
            if (args[i] == "--overrides")
                overridesJson = args[i + 1];
        }

        var imageRef = ResolveImageReference(imageArg);

        Console.WriteLine($"Running workflow '{workflowId}' on image '{imageRef}'...");
        Console.WriteLine(new string('-', 60));

        WorkflowRunResult result;
        try
        {
            result = await _workflowRunner.RunAsync(workflowId, imageRef, overridesJson);
        }
        catch (Exception ex)
        {
            // The workflow could not start at all: unknown id, or overrides that did not parse.
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine("Run 'dotnet run -- list-workflows' to see the available workflow IDs.");
            return 1;
        }

        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"Success: {result.Success}");
        Console.WriteLine($"Duration: {result.TotalDurationMs}ms");
        Console.WriteLine($"Input: {result.InputImageRef}");
        Console.WriteLine($"Output: {result.OutputImageRef}");
        Console.WriteLine($"Run ID: {result.RunId}");
        Console.WriteLine($"Output dir: {result.OutputDirectory}/");
        Console.WriteLine($"Steps: {result.Trace.Count}");

        if (!string.IsNullOrEmpty(result.Error))
            Console.WriteLine($"Error: {result.Error}");

        Console.WriteLine();
        Console.WriteLine("Step trace:");
        foreach (var step in result.Trace)
        {
            var status = step.Success ? "OK" : "FAIL";
            Console.WriteLine($"  [{status}] {step.StepId} ({step.ToolName}): {step.InputRef} -> {step.OutputRef} ({step.DurationMs}ms)");
            if (!string.IsNullOrEmpty(step.Error))
                Console.WriteLine($"         Error: {step.Error}");
        }

        if (result.Context.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Workflow context:");
            foreach (var (key, value) in result.Context)
            {
                var display = value.Length > 300 ? value[..300] + "..." : value;
                Console.WriteLine($"  {key}: {display}");
            }
        }

        if (!string.IsNullOrEmpty(result.FinalOutput) && result.FinalOutput != result.OutputImageRef)
        {
            Console.WriteLine();
            Console.WriteLine("Final output:");
            Console.WriteLine(result.FinalOutput.Length > 2000 ? result.FinalOutput[..2000] + "..." : result.FinalOutput);
        }

        return result.Success ? 0 : 1;
    }

    private async Task<int> RunToolCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run -- tool <toolName> <imageReference|filePath> [key=value ...]");
            Console.WriteLine("  Example: dotnet run -- tool AssessQuality RECEIPT001");
            Console.WriteLine("  Example: dotnet run -- tool EnhanceContrast RECEIPT001 method=auto_lab");
            Console.WriteLine("  Example: dotnet run -- tool DenoiseImage ./Storage/receipt.jpg method=auto");
            return 1;
        }

        var toolName = args[0];
        var imageArg = args[1];

        if (!_toolRegistry.HasTool(toolName))
        {
            Console.WriteLine($"Error: Tool '{toolName}' not found.");
            Console.WriteLine($"Available tools: {string.Join(", ", _toolRegistry.GetToolNames())}");
            return 1;
        }

        var imageRef = ResolveImageReference(imageArg);

        var toolArgs = new Dictionary<string, object?> { ["imageReference"] = imageRef };

        for (int i = 2; i < args.Length; i++)
        {
            var eqIdx = args[i].IndexOf('=');
            if (eqIdx > 0)
            {
                var key = args[i][..eqIdx];
                var val = args[i][(eqIdx + 1)..];
                toolArgs[key] = val;
            }
        }

        Console.WriteLine($"Invoking tool '{toolName}' on '{imageRef}'...");
        Console.WriteLine($"  Args: {string.Join(", ", toolArgs.Where(kv => kv.Key != "imageReference").Select(kv => $"{kv.Key}={kv.Value}"))}");
        Console.WriteLine(new string('-', 60));

        try
        {
            var result = await _toolRegistry.InvokeAsync(toolName, toolArgs);
            Console.WriteLine();
            Console.WriteLine("Result:");
            Console.WriteLine(result);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> RunRegisterCommand(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: dotnet run -- register <filePath> [referenceId]");
            Console.WriteLine("  Example: dotnet run -- register ./Storage/receipt.jpg RECEIPT001");
            return 1;
        }

        var filePath = Path.GetFullPath(args[0]);
        var desiredRef = args.Length > 1 ? args[1] : null;

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"Error: File not found: {filePath}");
            return 1;
        }

        try
        {
            // Through the tool rather than straight to the store, so a registration is recorded the
            // same way here as it is when an MCP client or the agent asks for one.
            var reference = await _aggregator.RegisterImageAsync(filePath, desiredRef);
            Console.WriteLine($"Registered: {reference}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> RunRunsCommand(string[] args)
    {
        if (!_runStore.IsEnabled)
        {
            Console.WriteLine("Run history is not configured.");
            Console.WriteLine("Set Runs:Provider to 'azuretable' (OCR_Runs__Provider) and supply AZURE_STORAGE_CONNECTION_STRING.");
            return 1;
        }

        var subcommand = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        if (subcommand == "get")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: dotnet run -- runs get <runId>");
                return 1;
            }

            var record = await _runStore.GetAsync(args[1]);
            if (record == null)
            {
                Console.WriteLine($"No run found with ID '{args[1]}'.");
                return 1;
            }

            Console.WriteLine(RunQueryTool.Describe(record, includeTrace: true));
            return 0;
        }

        if (subcommand != "list")
        {
            Console.WriteLine($"Unknown runs subcommand: '{subcommand}'. Use 'list' or 'get'.");
            return 1;
        }

        int top = 20;
        string? mode = null;

        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--top" && int.TryParse(args[i + 1], out var parsed))
                top = parsed;
            else if (args[i] == "--mode")
                mode = args[i + 1];
        }

        var records = await _runStore.ListAsync(top, mode);
        if (records.Count == 0)
        {
            Console.WriteLine("No runs recorded yet.");
            return 0;
        }

        Console.WriteLine($"{"RUN ID",-38} {"MODE",-9} {"NAME",-28} {"RESULT",-7} {"MS",7} STARTED");
        foreach (var record in records)
        {
            var name = record.Name.Length > 28 ? record.Name[..27] + "~" : record.Name;
            Console.WriteLine(
                $"{record.RunId,-38} {record.Mode,-9} {name,-28} " +
                $"{(record.Success ? "OK" : "FAILED"),-7} {record.DurationMs,7} " +
                $"{record.StartTimeUtc:yyyy-MM-dd HH:mm:ss}Z");
        }

        return 0;
    }

    private int RunListTools()
    {
        Console.WriteLine("Available tools:");
        foreach (var name in _toolRegistry.GetToolNames().OrderBy(n => n))
            Console.WriteLine($"  - {name}");
        return 0;
    }

    private int RunListWorkflows()
    {
        Console.WriteLine("Available workflows:");
        foreach (var (id, name, description) in _workflowCatalog.List())
            Console.WriteLine($"  - {id}: {name} -- {description}");
        if (_workflowCatalog.Count == 0)
            Console.WriteLine("  (none found)");
        return 0;
    }

    private string ResolveImageReference(string imageArg)
    {
        if (File.Exists(imageArg) || File.Exists(Path.GetFullPath(imageArg)))
        {
            var fullPath = Path.GetFullPath(imageArg);
            var refName = Path.GetFileNameWithoutExtension(fullPath).ToUpperInvariant();

            if (_store.Exists(refName))
                return refName;

            Console.WriteLine($"  Registering file '{fullPath}' as '{refName}'...");
            return _store.SaveFromFile(fullPath, refName);
        }

        return imageArg;
    }

    private static int ShowHelp(string? error = null)
    {
        if (error != null)
            Console.WriteLine($"Error: {error}\n");

        Console.WriteLine("OcrAgent CLI Mode");
        Console.WriteLine("=================");
        Console.WriteLine();
        Console.WriteLine("Usage: dotnet run -- <command> [arguments]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  workflow <id> <image> [--overrides <json>]   Run a preprocessing workflow");
        Console.WriteLine("  wf       <id> <image> [--overrides <json>]   Alias for 'workflow'");
        Console.WriteLine("  tool     <name> <image> [key=value ...]      Invoke a single plugin tool");
        Console.WriteLine("  register <filePath> [referenceId]            Register an image file");
        Console.WriteLine("  runs list [--top N] [--mode tool|workflow]   Show recorded runs, newest first");
        Console.WriteLine("  runs get <runId>                             Show one recorded run in full");
        Console.WriteLine("  list-tools                                   List all available tools");
        Console.WriteLine("  list-workflows                               List all available workflows");
        Console.WriteLine("  chat                                         Start interactive chat mode");
        Console.WriteLine("  help                                         Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run -- workflow receipt_extract_v3 RECEIPT001");
        Console.WriteLine("  dotnet run -- wf receipt_extract_v3 RECEIPT001");
        Console.WriteLine("  dotnet run -- tool AssessQuality RECEIPT003");
        Console.WriteLine("  dotnet run -- tool EnhanceContrast RECEIPT001 method=auto_lab");
        Console.WriteLine("  dotnet run -- register ./Storage/scan.jpg SCAN001");
        Console.WriteLine("  dotnet run -- runs list --top 10 --mode workflow");
        Console.WriteLine("  dotnet run -- list-tools");
        Console.WriteLine("  dotnet run                                    (starts chat mode)");

        return error != null ? 1 : 0;
    }
}
