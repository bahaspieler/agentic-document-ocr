namespace OcrAgent.Agent;

/// <summary>Ordered preprocessing operations selected once for an entire image batch.</summary>
public sealed record BulkActionPlan(IReadOnlyList<BulkPlanStep> Steps);
