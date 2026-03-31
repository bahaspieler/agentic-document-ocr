using System.Text.Json;
using OcrAgent.Workflows.Model;

namespace OcrAgent.Workflows.Engine;

/// <summary>
/// Loads <c>*.json</c> workflow definitions from a folder and looks them up by id (case-insensitive).
/// </summary>
public sealed class WorkflowCatalog
{
    private readonly Dictionary<string, WorkflowDefinition> _workflows = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads all JSON files in <paramref name="definitionsPath"/>; skips missing directories; indexes by <see cref="WorkflowDefinition.Id"/>.
    /// </summary>
    /// <param name="definitionsPath">Directory containing workflow JSON files.</param>
    public WorkflowCatalog(string definitionsPath)
    {
        if (!Directory.Exists(definitionsPath)) return;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (var file in Directory.GetFiles(definitionsPath, "*.json"))
        {
            var json = File.ReadAllText(file);
            var def = JsonSerializer.Deserialize<WorkflowDefinition>(json, options);
            if (def != null && !string.IsNullOrEmpty(def.Id))
                _workflows[def.Id] = def;
        }
    }

    /// <summary>
    /// Returns a loaded definition or throws with available ids.
    /// </summary>
    /// <param name="id">Workflow id from JSON.</param>
    /// <returns>Matching definition.</returns>
    public WorkflowDefinition Get(string id)
    {
        if (!_workflows.TryGetValue(id, out var def))
            throw new InvalidOperationException($"Workflow '{id}' not found. Available: {string.Join(", ", _workflows.Keys)}");
        return def;
    }

    /// <summary>
    /// Lists id, display name, and description for each loaded workflow.
    /// </summary>
    /// <returns>Sequence of metadata tuples.</returns>
    public IEnumerable<(string Id, string Name, string Description)> List()
        => _workflows.Values.Select(w => (w.Id, w.Name, w.Description));

    /// <summary>
    /// Number of workflows loaded after construction.
    /// </summary>
    public int Count => _workflows.Count;
}
