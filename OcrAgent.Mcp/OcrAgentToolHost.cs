using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using OcrAgent.Hosting;
using OcrAgent.Plugins;
using OcrAgent.Runs;
using OcrAgent.Storage;
using OcrAgent.Workflows.Tools;

namespace OcrAgent.Mcp;

/// <summary>
/// Bootstraps the OcrAgent engine once and exposes its tool surface as MCP server tools.
/// </summary>
public sealed class OcrAgentToolHost : IDisposable
{
    private readonly EngineHost _engine;

    /// <summary>Backing image store shared by every tool.</summary>
    public IImageStore Store => _engine.Store;

    /// <summary>Registry of all preprocessing, OCR, extraction, and workflow tools.</summary>
    public PluginAggregator Aggregator => _engine.Aggregator;

    /// <summary>Store that receives one record per job executed through this host.</summary>
    public IRunStore RunStore => _engine.RunStore;

    private OcrAgentToolHost(EngineHost engine)
    {
        _engine = engine;
    }

    /// <summary>Builds the engine for the MCP entry point.</summary>
    /// <returns>A ready-to-use host whose tools can be exposed over MCP.</returns>
    public static OcrAgentToolHost Create() => new(EngineHost.Create("mcp"));

    /// <summary>
    /// Wraps every <see cref="AIFunction"/> exposed by the engine as an MCP server tool.
    /// This surfaces RegisterImage, all preprocessing/OCR/extraction plugins, and the
    /// workflow tools with their existing names, descriptions, and JSON schemas.
    /// </summary>
    /// <remarks>
    /// RunWorkflow is built from a delegate instead, and its generated function is left out so it is not
    /// registered twice. See <see cref="CreateRunWorkflowTool"/> for why the delegate is necessary.
    /// </remarks>
    /// <returns>MCP tools to register with the server.</returns>
    public IEnumerable<McpServerTool> CreateMcpTools()
    {
        var wrapped = _engine.Aggregator.GetAllTools()
            .OfType<AIFunction>()
            .Where(function => function.Name != nameof(_engine.Aggregator.Workflows.RunWorkflow))
            .Select(function => McpServerTool.Create(function));

        return wrapped.Append(CreateRunWorkflowTool());
    }

    /// <summary>
    /// Builds the prompts describing the flows this server is for.
    /// </summary>
    /// <returns>MCP prompts to register with the server.</returns>
    public IEnumerable<McpServerPrompt> CreateMcpPrompts() =>
        OcrAgentPrompts.Create(
            _engine.Aggregator.Workflows.Catalog.List().Select(workflow => workflow.Id),
            _engine.RunStore.IsEnabled);

    /// <summary>
    /// Builds the RunWorkflow tool from a delegate so the SDK can hand it a progress sink.
    /// </summary>
    /// <remarks>
    /// A receipt pipeline runs for several seconds across a dozen steps, and a client watching one
    /// should see where it is rather than only that a call is outstanding. Progress requires an
    /// <see cref="IProgress{T}"/> parameter that the SDK fills in, and that injection only happens for
    /// tools created from a delegate: <c>McpServerTool.Create(AIFunction)</c> takes the schema the
    /// function already carries and performs no MCP-specific binding, so an added parameter there would
    /// surface to the model as an argument it cannot supply.
    /// <para>
    /// The parameter descriptions come from the same constants the generated function's attributes use,
    /// so this tool and the one the console agent sees cannot describe the operation differently.
    /// </para>
    /// </remarks>
    /// <returns>The RunWorkflow tool, reporting one notification per completed step.</returns>
    private McpServerTool CreateRunWorkflowTool()
    {
        var workflows = _engine.Aggregator.Workflows;

        return McpServerTool.Create(
            (
                [Description(WorkflowTool.WorkflowIdDescription)] string workflowId,
                [Description(WorkflowTool.ImageReferenceDescription)] string imageReference,
                IProgress<ProgressNotificationValue> progress,
                [Description(WorkflowTool.OverridesJsonDescription)] string? overridesJson = null
            ) => workflows.RunWorkflowWithProgress(
                workflowId, imageReference, overridesJson, new WorkflowProgressAdapter(progress)),
            new McpServerToolCreateOptions
            {
                Name = nameof(workflows.RunWorkflow),
                Description = WorkflowTool.RunWorkflowDescription,
            });
    }

    /// <summary>Disposes the engine.</summary>
    public void Dispose() => _engine.Dispose();
}
