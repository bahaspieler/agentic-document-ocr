namespace OcrAgent.Execution;

/// <summary>The kind of engine operation requested by a transport.</summary>
public enum ExecutionMode
{
    /// <summary>Runs a natural-language agent request.</summary>
    Agent,

    /// <summary>Invokes one registered tool directly.</summary>
    Tool,

    /// <summary>Runs a named preprocessing workflow.</summary>
    Workflow,
}
