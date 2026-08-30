namespace OcrAgent.Sql;

/// <summary>Signals that a receipt changed before a correction could be saved.</summary>
public sealed class ReceiptConcurrencyException(string receiptId)
    : InvalidOperationException($"Receipt '{receiptId}' changed before this edit could be saved.");
