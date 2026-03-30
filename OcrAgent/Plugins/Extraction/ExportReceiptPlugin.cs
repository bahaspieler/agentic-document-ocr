using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OcrAgent.Extraction.Export;
using OcrAgent.Extraction.Models;
using OcrAgent.Storage;

namespace OcrAgent.Plugins.Extraction;

/// <summary>
/// Workflow tool that exports parsed receipt data as JSON and CSV files
/// into the active storage subdirectory.
/// </summary>
public sealed class ExportReceiptPlugin : IImageProcessingTool
{
    private readonly ReceiptJsonExporter _jsonExporter;
    private readonly ReceiptCsvExporter _csvExporter;

    public ExportReceiptPlugin(IImageStore store)
    {
        _jsonExporter = new ReceiptJsonExporter(store);
        _csvExporter = new ReceiptCsvExporter(store);
    }

    [Description("Exports parsed receipt data as JSON and CSV files (receipt.json, receipt_summary.csv, receipt_items.csv). " +
                 "Returns a summary of exported files.")]
    public Task<string> ExportReceiptData(
        [Description("The reference of the image (used for file naming context).")] string imageReference,
        [Description("The structured receipt JSON from ParseReceipt.")] string receiptJson,
        [Description("Workflow ID (injected from context).")] string? workflowId = null,
        [Description("Run ID (injected from context).")] string? runId = null)
    {
        var receipt = JsonSerializer.Deserialize<ReceiptData>(receiptJson, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }) ?? throw new InvalidOperationException("Failed to deserialize receipt data.");

        if (string.IsNullOrEmpty(receipt.ReceiptId))
            receipt.ReceiptId = imageReference;

        if (!string.IsNullOrEmpty(workflowId))
            receipt.Source.WorkflowId = workflowId;
        if (!string.IsNullOrEmpty(runId))
            receipt.Source.RunId = runId;

        var jsonRef = _jsonExporter.Export(receipt);
        var (summaryRef, itemsRef) = _csvExporter.Export(receipt);

        var summary = $"Exported receipt data for '{receipt.Merchant.Name ?? "unknown"}':\n" +
                      $"  JSON:         {jsonRef}\n" +
                      $"  Summary CSV:  {summaryRef}\n" +
                      $"  Items CSV:    {itemsRef}\n" +
                      $"  Total:        {receipt.Totals.Total?.ToString("C") ?? "N/A"}\n" +
                      $"  Items count:  {receipt.Items.Count}\n" +
                      $"  Validation:   {(receipt.Validation.IsValid ? "PASS" : "FAIL")}";

        return Task.FromResult(summary);
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.ExportReceiptData);
    }
}
