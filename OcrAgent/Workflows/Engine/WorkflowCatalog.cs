using System.Text.Json;
using OcrAgent.Workflows.Model;

namespace OcrAgent.Workflows.Engine;

public sealed class WorkflowCatalog
{
    private readonly Dictionary<string, WorkflowDefinition> _workflows = new(StringComparer.OrdinalIgnoreCase);

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

    public WorkflowDefinition Get(string id)
    {
        if (!_workflows.TryGetValue(id, out var def))
            throw new InvalidOperationException($"Workflow '{id}' not found. Available: {string.Join(", ", _workflows.Keys)}");
        return def;
    }

    public IEnumerable<(string Id, string Name, string Description)> List()
        => _workflows.Values.Select(w => (w.Id, w.Name, w.Description));

    public int Count => _workflows.Count;
}
