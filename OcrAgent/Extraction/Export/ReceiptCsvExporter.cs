using System.Globalization;
using System.Text;
using OcrAgent.Extraction.Models;
using OcrAgent.Storage;

namespace OcrAgent.Extraction.Export;

/// <summary>
/// One summary row per receipt and one items file with one row per line item.
/// </summary>
public sealed class ReceiptCsvExporter
{
    private readonly IImageStore _store;

    /// <summary>Creates an exporter that writes CSV files through the supplied image store.</summary>
    /// <param name="store">Target for <see cref="IImageStore.GetWriteDirectory"/>.</param>
    public ReceiptCsvExporter(IImageStore store) => _store = store;

    /// <summary>Writes summary and items CSVs under the store directory.</summary>
    /// <param name="receipt">Structured receipt to export.</param>
    /// <returns>Relative paths for summary and items files.</returns>
    public (string summaryRef, string itemsRef) Export(ReceiptData receipt)
    {
        var summaryRef = ExportSummary(receipt);
        var itemsRef = ExportItems(receipt);
        return (summaryRef, itemsRef);
    }

    /// <summary>Header plus one data row to <c>receipt_summary.csv</c>.</summary>
    /// <param name="receipt">Source for merchant, transaction, totals, validation.</param>
    /// <returns>Relative filename <c>receipt_summary.csv</c>.</returns>
    private string ExportSummary(ReceiptData receipt)
    {
        var sb = new StringBuilder();

        sb.AppendLine("receiptId,merchantName,merchantAddress,merchantPhone,transactionDate," +
                       "subtotal,tax,total,discount,tip,paid,paymentMethod,currency," +
                       "ocrConfidence,validationStatus");

        sb.Append(CsvEscape(receipt.ReceiptId)); sb.Append(',');
        sb.Append(CsvEscape(receipt.Merchant.Name ?? "")); sb.Append(',');
        sb.Append(CsvEscape(string.Join("; ", receipt.Merchant.AddressLines))); sb.Append(',');
        sb.Append(CsvEscape(receipt.Merchant.Phone ?? "")); sb.Append(',');
        sb.Append(CsvEscape(receipt.Transaction.DateTime ?? "")); sb.Append(',');
        sb.Append(FormatDecimal(receipt.Totals.Subtotal)); sb.Append(',');
        sb.Append(FormatDecimal(receipt.Totals.Tax)); sb.Append(',');
        sb.Append(FormatDecimal(receipt.Totals.Total)); sb.Append(',');
        sb.Append(FormatDecimal(receipt.Totals.Discount)); sb.Append(',');
        sb.Append(FormatDecimal(receipt.Totals.Tip)); sb.Append(',');
        sb.Append(FormatDecimal(receipt.Totals.Paid)); sb.Append(',');
        sb.Append(CsvEscape(receipt.Totals.PaymentMethod ?? "")); sb.Append(',');
        sb.Append(CsvEscape(receipt.Totals.Currency)); sb.Append(',');
        sb.Append(receipt.Source.OcrConfidence.ToString("F2", CultureInfo.InvariantCulture)); sb.Append(',');
        sb.Append(receipt.Validation.IsValid ? "valid" : "invalid");
        sb.AppendLine();

        var dir = _store.GetWriteDirectory();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "receipt_summary.csv"), sb.ToString());
        return "receipt_summary.csv";
    }

    /// <summary>Header plus one row per item to <c>receipt_items.csv</c>.</summary>
    /// <param name="receipt">Source for <see cref="ReceiptData.Items"/>.</param>
    /// <returns>Relative filename <c>receipt_items.csv</c>.</returns>
    private string ExportItems(ReceiptData receipt)
    {
        var sb = new StringBuilder();

        sb.AppendLine("receiptId,itemIndex,description,quantity,unitPrice,lineTotal,taxIndicator");

        foreach (var item in receipt.Items)
        {
            sb.Append(CsvEscape(receipt.ReceiptId)); sb.Append(',');
            sb.Append(item.Index); sb.Append(',');
            sb.Append(CsvEscape(item.Description)); sb.Append(',');
            sb.Append(FormatDecimal(item.Quantity)); sb.Append(',');
            sb.Append(FormatDecimal(item.UnitPrice)); sb.Append(',');
            sb.Append(FormatDecimal(item.LineTotal)); sb.Append(',');
            sb.Append(CsvEscape(item.TaxIndicator ?? ""));
            sb.AppendLine();
        }

        var dir = _store.GetWriteDirectory();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "receipt_items.csv"), sb.ToString());
        return "receipt_items.csv";
    }

    /// <summary>Quotes when the value contains comma, quote, or newline.</summary>
    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    /// <summary>Two decimal places invariant, or empty when null.</summary>
    private static string FormatDecimal(decimal? value) =>
        value.HasValue ? value.Value.ToString("F2", CultureInfo.InvariantCulture) : "";
}
