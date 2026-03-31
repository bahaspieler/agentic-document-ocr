using System.Collections.Concurrent;

namespace OcrAgent.Workflows.Engine;

/// <summary>
/// String key-value state for one workflow run; safe for concurrent step updates.
/// </summary>
public sealed class WorkflowContext
{
    private readonly ConcurrentDictionary<string, string> _data = new();

    /// <summary>
    /// Sets or overwrites a context key.
    /// </summary>
    /// <param name="key">Context property name.</param>
    /// <param name="value">String value to store.</param>
    public void Set(string key, string value) => _data[key] = value;

    /// <summary>
    /// Reads a key if present.
    /// </summary>
    /// <param name="key">Context property name.</param>
    /// <returns>Value or null when missing.</returns>
    public string? Get(string key) => _data.GetValueOrDefault(key);

    /// <summary>
    /// Returns whether <paramref name="key"/> exists.
    /// </summary>
    /// <param name="key">Context property name.</param>
    /// <returns>True if the key is set.</returns>
    public bool Has(string key) => _data.ContainsKey(key);

    /// <summary>
    /// Exposes all entries for copying into run results.
    /// </summary>
    /// <returns>Live read-only view of the dictionary.</returns>
    public IReadOnlyDictionary<string, string> GetAll() => _data;
}
