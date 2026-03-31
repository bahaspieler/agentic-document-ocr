using Microsoft.Extensions.AI;

namespace OcrAgent.Workflows.Engine;

/// <summary>
/// Holds callable <see cref="AIFunction"/> instances by name for workflow tool steps.
/// </summary>
public sealed class ToolBindingRegistry
{
    private readonly Dictionary<string, AIFunction> _bindings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds or replaces a binding keyed by <see cref="AIFunction.Name"/> (case-insensitive).
    /// </summary>
    /// <param name="function">Tool implementation to expose.</param>
    public void Register(AIFunction function)
    {
        _bindings[function.Name] = function;
    }

    /// <summary>
    /// Invokes the named function with the given argument dictionary.
    /// </summary>
    /// <param name="toolName">Registered tool name.</param>
    /// <param name="args">Arguments passed to the underlying function.</param>
    /// <returns>String result or empty string when null.</returns>
    /// <exception cref="InvalidOperationException">When no binding exists for <paramref name="toolName"/>.</exception>
    public async Task<string> InvokeAsync(string toolName, Dictionary<string, object?> args)
    {
        if (!_bindings.TryGetValue(toolName, out var function))
            throw new InvalidOperationException($"No tool binding found for '{toolName}'");

        var funcArgs = new AIFunctionArguments(args);
        var result = await function.InvokeAsync(funcArgs);

        return result?.ToString() ?? "";
    }

    /// <summary>
    /// Returns whether a tool was registered.
    /// </summary>
    /// <param name="toolName">Name to check.</param>
    /// <returns>True if present.</returns>
    public bool HasTool(string toolName) => _bindings.ContainsKey(toolName);

    /// <summary>
    /// Enumerates registered tool names.
    /// </summary>
    /// <returns>All keys in the registry.</returns>
    public IEnumerable<string> GetToolNames() => _bindings.Keys;
}
