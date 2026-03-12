using Microsoft.Extensions.AI;

namespace OcrAgent.Workflows.Engine;

public sealed class ToolBindingRegistry
{
    private readonly Dictionary<string, AIFunction> _bindings = new(StringComparer.OrdinalIgnoreCase);

    public void Register(AIFunction function)
    {
        _bindings[function.Name] = function;
    }

    public async Task<string> InvokeAsync(string toolName, Dictionary<string, object?> args)
    {
        if (!_bindings.TryGetValue(toolName, out var function))
            throw new InvalidOperationException($"No tool binding found for '{toolName}'");

        var funcArgs = new AIFunctionArguments(args);
        var result = await function.InvokeAsync(funcArgs);

        return result?.ToString() ?? "";
    }

    public bool HasTool(string toolName) => _bindings.ContainsKey(toolName);

    public IEnumerable<string> GetToolNames() => _bindings.Keys;
}
