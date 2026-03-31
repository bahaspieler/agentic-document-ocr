using System.Text.Json;
using OcrAgent.Extraction.Models;
using OcrAgent.Storage;

namespace OcrAgent.Extraction.Export;

/// <summary>
/// Writes indented JSON to the store write folder as <c>receipt.json</c> (direct file IO).
/// </summary>
public sealed class ReceiptJsonExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IImageStore _store;

    /// <param name="store">Target for <see cref="IImageStore.GetWriteDirectory"/>.</param>
    public ReceiptJsonExporter(IImageStore store) => _store = store;

    /// <summary>Serializes <paramref name="receipt"/> and writes <c>receipt.json</c>.</summary>
    /// <param name="receipt">Structured receipt to export.</param>
    /// <returns>Relative filename <c>receipt.json</c>.</returns>
    public string Export(ReceiptData receipt)
    {
        var json = JsonSerializer.Serialize(receipt, JsonOptions);
        var dir = _store.GetWriteDirectory();
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "receipt.json");
        File.WriteAllText(filePath, json);
        return "receipt.json";
    }
}
