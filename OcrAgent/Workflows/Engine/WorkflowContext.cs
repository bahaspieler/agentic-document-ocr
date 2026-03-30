using System.Collections.Concurrent;

namespace OcrAgent.Workflows.Engine;

public sealed class WorkflowContext
{
    private readonly ConcurrentDictionary<string, string> _data = new();

    public void Set(string key, string value) => _data[key] = value;

    public string? Get(string key) => _data.GetValueOrDefault(key);

    public bool Has(string key) => _data.ContainsKey(key);

    public IReadOnlyDictionary<string, string> GetAll() => _data;
}
