using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OcrAgent.Extraction.Export;
using OcrAgent.Extraction.Models;
using OcrAgent.Runs;
using OcrAgent.Sql;
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
    private readonly IReceiptStore _receiptStore;

    /// <summary>
    /// Creates a receipt export plugin.
    /// </summary>
    /// <param name="store">Image store that supplies the active export directory.</param>
    /// <param name="receiptStore">Optional store used to index exported receipts.</param>
    public ExportReceiptPlugin(IImageStore store, IReceiptStore? receiptStore = null)
    {
        _jsonExporter = new ReceiptJsonExporter(store);
        _csvExporter = new ReceiptCsvExporter(store);
        _receiptStore = receiptStore ?? NullReceiptStore.Instance;
    }

    /// <summary>
    /// Exports structured receipt data to JSON and CSV and optionally indexes it.
    /// </summary>
    /// <param name="imageReference">Source image reference used as a fallback receipt identifier.</param>
    /// <param name="receiptJson">Structured receipt JSON to export.</param>
    /// <param name="workflowId">Optional workflow identifier recorded in the receipt source.</param>
    /// <param name="runId">Optional run identifier used as the receipt identifier.</param>
    /// <returns>A summary of the exported files and receipt totals.</returns>
    [Description("Exports parsed receipt data as JSON and CSV files (receipt.json, receipt_summary.csv, receipt_items.csv). " +
                 "Returns a summary of exported files.")]
    public async Task<string> ExportReceiptData(
        [Description("The reference of the image (used for file naming context).")] string imageReference,
        [Description("The structured receipt JSON from ParseReceipt.")] string receiptJson,
        [Description("Workflow ID (injected from context).")] string? workflowId = null,
        [Description("Run ID (injected from context).")] string? runId = null)
    {
        var receipt = JsonSerializer.Deserialize<ReceiptData>(receiptJson, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }) ?? throw new InvalidOperationException("Failed to deserialize receipt data.");

        // Run ids are URL-safe, stable, and unique. Image references can contain scope separators,
        // which makes them unsuitable as REST resource ids.
        receipt.ReceiptId = !string.IsNullOrEmpty(runId)
            ? runId
            : ImageReferenceGenerator.Split(imageReference).Leaf;

        if (!string.IsNullOrEmpty(workflowId))
            receipt.Source.WorkflowId = workflowId;
        if (!string.IsNullOrEmpty(runId))
            receipt.Source.RunId = runId;

        var jsonRef = _jsonExporter.Export(receipt);
        var (summaryRef, itemsRef) = _csvExporter.Export(receipt);

        try
        {
            await _receiptStore.SaveAsync(receipt, runId, RunScope.ConversationId).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Receipt indexing is bookkeeping. The JSON/CSV export above already succeeded, so a
            // transient SQL failure must not turn a successful OCR workflow into a failed one.
            Console.Error.WriteLine($"Warning: receipt '{receipt.ReceiptId}' was not indexed ({exception.Message})");
        }

        var summary = $"Exported receipt data for '{receipt.Merchant.Name ?? "unknown"}':\n" +
                      $"  JSON:         {jsonRef}\n" +
                      $"  Summary CSV:  {summaryRef}\n" +
                      $"  Items CSV:    {itemsRef}\n" +
                      $"  Total:        {receipt.Totals.Total?.ToString("C") ?? "N/A"}\n" +
                      $"  Items count:  {receipt.Items.Count}\n" +
                      $"  Validation:   {(receipt.Validation.IsValid ? "PASS" : "FAIL")}";

        return summary;
    }

    /// <summary>
    /// Gets the AI-callable receipt export tool.
    /// </summary>
    /// <returns>The tools exposed by this plugin.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.ExportReceiptData);
    }
}
