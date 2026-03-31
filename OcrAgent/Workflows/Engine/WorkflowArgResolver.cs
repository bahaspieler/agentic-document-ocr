using System.Text.Json;

namespace OcrAgent.Workflows.Engine;

/// <summary>
/// Resolves <c>$context.*</c> placeholders in step arguments and sets the run image reference.
/// </summary>
internal static class WorkflowArgResolver
{
    /// <summary>
    /// Copies <paramref name="defaults"/>, replaces <c>$context.*</c> string values from context, and adds <c>imageReference</c>.
    /// </summary>
    /// <param name="defaults">Merged step args from definition and overrides.</param>
    /// <param name="context">Run context used for placeholder values.</param>
    /// <param name="imageReference">Input image ref for the current step chain.</param>
    /// <returns>Resolved arguments including <c>imageReference</c>.</returns>
    public static Dictionary<string, object?> Resolve(
        Dictionary<string, object?> defaults,
        WorkflowContext context,
        string imageReference)
    {
        var args = new Dictionary<string, object?>(defaults.Count + 1);

        foreach (var (key, value) in defaults)
        {
            var strVal = value switch
            {
                string s => s,
                JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
                _ => null,
            };

            if (strVal != null && strVal.StartsWith("$context."))
            {
                var contextKey = strVal["$context.".Length..];
                args[key] = context.Get(contextKey);
            }
            else
            {
                args[key] = value;
            }
        }

        args["imageReference"] = imageReference;
        return args;
    }
}
