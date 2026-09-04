using System.ComponentModel;
using System.Text.Json;

namespace OcrAgent.Agent;

/// <summary>Captures the single structured plan submitted by the batch-planning agent.</summary>
internal sealed class BulkPlanCollector
{
    /// <summary>Gets the plan submitted by the agent.</summary>
    public BulkActionPlan? Plan { get; private set; }

    /// <summary>Validates and captures one ordered batch plan.</summary>
    /// <param name="stepsJson">JSON array of mode, name, and optional arguments objects.</param>
    /// <returns>Confirmation that the plan was accepted.</returns>
    [Description("Submit exactly one ordered preprocessing plan for the complete batch.")]
    public string SubmitBulkPlan(
        [Description(
            "JSON array. Each item has mode ('tool' or 'workflow'), name, and optional arguments object. " +
            "Use several tool items for a chain, or one workflow item by itself.")]
        string stepsJson)
    {
        if (Plan is not null)
            throw new InvalidOperationException("A bulk plan was already submitted.");

        var steps = JsonSerializer.Deserialize<List<BulkPlanStep>>(
            stepsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("The submitted bulk plan was empty.");
        Plan = new BulkActionPlan(steps);
        return $"Accepted {steps.Count} batch operation(s).";
    }
}
