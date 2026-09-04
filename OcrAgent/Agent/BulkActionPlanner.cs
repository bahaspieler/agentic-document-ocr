using System.Text;
using Microsoft.Extensions.AI;
using OcrAgent.Plugins;

namespace OcrAgent.Agent;

/// <summary>Selects and validates one reusable preprocessing plan for a complete batch.</summary>
public sealed class BulkActionPlanner
{
    private const int MaximumSteps = 8;

    private static readonly HashSet<string> AllowedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "ZoomInImage",
        "RotateImage",
        "CorrectPerspective",
        "DeskewImage",
        "AutoOrient",
        "ResizeImage",
        "AddBorderPadding",
        "AutoCrop",
        "EnhanceContrast",
        "AdjustBrightness",
        "DenoiseImage",
        "CorrectColor",
        "InvertImage",
        "SharpenImage",
        "NormalizeIllumination",
        "BinarizeImage",
        "ApplyMorphology",
        "RemoveDocumentBackground"
    };

    private readonly IReadOnlyDictionary<string, string> _toolDescriptions;
    private readonly string _workflowDescriptions;
    private readonly IReadOnlySet<string> _workflowNames;

    /// <summary>Creates a planner from the registered plugin and workflow catalogue.</summary>
    /// <param name="aggregator">Registered OCR tools and workflows.</param>
    public BulkActionPlanner(PluginAggregator aggregator)
    {
        ArgumentNullException.ThrowIfNull(aggregator);
        _toolDescriptions = aggregator.GetAllTools()
            .OfType<AIFunction>()
            .Where(tool => AllowedTools.Contains(tool.Name))
            .ToDictionary(
                tool => tool.Name,
                tool => tool.Description ?? "",
                StringComparer.OrdinalIgnoreCase);
        _workflowDescriptions = aggregator.Workflows.ListWorkflows();
        _workflowNames = _workflowDescriptions.Split('\n')
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .Select(line => line[2..].Split(':', 2)[0].Trim())
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Asks the model once for a plan that will be applied unchanged to every image.</summary>
    /// <param name="command">Natural-language bulk command.</param>
    /// <param name="cancellationToken">Stops model planning.</param>
    /// <returns>A validated ordered plan.</returns>
    public async Task<BulkActionPlan> PlanAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var collector = new BulkPlanCollector();
        var submitTool = AIFunctionFactory.Create(collector.SubmitBulkPlan);
        var workflowsAllowed = AllowsWorkflows(command);
        var planner = AgentService.Create(
            new[] { submitTool },
            BuildInstructions(workflowsAllowed));
        await planner.RunOneShotAsync(command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return Validate(
            collector.Plan ?? throw new InvalidOperationException(
                "The planning agent did not submit a bulk plan."),
            _toolDescriptions.Keys,
            workflowsAllowed ? _workflowNames : []);
    }

    /// <summary>Validates plan shape and tool allow-list membership without invoking a model.</summary>
    /// <param name="plan">Candidate plan.</param>
    /// <param name="registeredTools">Currently registered image-transform tools.</param>
    /// <param name="registeredWorkflows">Currently registered workflow identifiers.</param>
    /// <returns>The validated plan.</returns>
    public static BulkActionPlan Validate(
        BulkActionPlan plan,
        IEnumerable<string> registeredTools,
        IEnumerable<string>? registeredWorkflows = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var tools = new HashSet<string>(registeredTools, StringComparer.OrdinalIgnoreCase);
        var workflows = new HashSet<string>(
            registeredWorkflows ?? [], StringComparer.OrdinalIgnoreCase);
        if (plan.Steps.Count is < 1 or > MaximumSteps)
            throw new InvalidOperationException(
                $"A bulk plan must contain between 1 and {MaximumSteps} operations.");

        var workflowCount = plan.Steps.Count(step =>
            step.Mode.Equals("workflow", StringComparison.OrdinalIgnoreCase));
        if (workflowCount > 0 && plan.Steps.Count != 1)
            throw new InvalidOperationException(
                "A workflow must be the only operation in a bulk plan.");

        foreach (var step in plan.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Name))
                throw new InvalidOperationException("Every bulk-plan operation needs a name.");
            if (step.Mode.Equals("tool", StringComparison.OrdinalIgnoreCase))
            {
                if (!AllowedTools.Contains(step.Name) || !tools.Contains(step.Name))
                    throw new InvalidOperationException(
                        $"Tool '{step.Name}' is not an allowed image-transform operation.");
            }
            else if (step.Mode.Equals("workflow", StringComparison.OrdinalIgnoreCase))
            {
                if (!workflows.Contains(step.Name))
                    throw new InvalidOperationException(
                        $"Workflow '{step.Name}' is not registered.");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported bulk-plan mode '{step.Mode}'.");
            }

            if (step.Arguments?.Keys.Any(IsImageArgument) == true)
                throw new InvalidOperationException(
                    $"Operation '{step.Name}' may not replace the per-item image reference.");
        }

        return plan;
    }

    private string BuildInstructions(bool workflowsAllowed)
    {
        var catalogue = new StringBuilder();
        foreach (var (name, description) in _toolDescriptions.OrderBy(item => item.Key))
            catalogue.AppendLine($"- tool {name}: {description}");
        if (workflowsAllowed)
        {
            catalogue.AppendLine("Available workflows:");
            catalogue.AppendLine(_workflowDescriptions);
        }
        else
        {
            catalogue.AppendLine(
                "No workflow is eligible because this command requests preprocessing only.");
        }

        return AgentInstructions.ForBulkPlanner + Environment.NewLine + catalogue;
    }

    /// <summary>Returns whether a command explicitly requests workflow-level processing.</summary>
    /// <param name="command">Natural-language batch command.</param>
    /// <returns>Whether workflows may be offered to the planning agent.</returns>
    public static bool AllowsWorkflows(string command) =>
        !string.IsNullOrWhiteSpace(command) &&
        (command.Contains("workflow", StringComparison.OrdinalIgnoreCase) ||
         command.Contains("extract receipt", StringComparison.OrdinalIgnoreCase) ||
         command.Contains("receipt extraction", StringComparison.OrdinalIgnoreCase) ||
         command.Contains("run ocr", StringComparison.OrdinalIgnoreCase) ||
         command.Contains("perform ocr", StringComparison.OrdinalIgnoreCase));

    private static bool IsImageArgument(string name) =>
        name.Equals("imageReference", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("filePath", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("inputFile", StringComparison.OrdinalIgnoreCase);
}
