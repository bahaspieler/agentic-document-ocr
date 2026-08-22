namespace OcrAgent.Workflows.Model;

/// <summary>
/// One step of a workflow finishing, reported while the run is still in progress.
/// </summary>
/// <remarks>
/// A receipt pipeline takes several seconds and a caller watching it should not have to wait for the
/// summary to learn where it is. This type says what happened in the engine's own terms; translating
/// it into a transport's progress message is the host's job, which is what keeps the engine free of
/// any protocol.
/// </remarks>
/// <param name="Completed">Steps finished so far, counting this one.</param>
/// <param name="Total">Steps the compiled workflow will run.</param>
/// <param name="StepId">Workflow step id from the definition.</param>
/// <param name="ToolName">Tool the step invoked.</param>
/// <param name="Success">Whether the step completed without error.</param>
public sealed record WorkflowStepProgress(
    int Completed,
    int Total,
    string StepId,
    string ToolName,
    bool Success);
