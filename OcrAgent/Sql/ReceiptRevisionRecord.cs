namespace OcrAgent.Sql;

/// <summary>Immutable audit entry for one corrected receipt revision.</summary>
public sealed record ReceiptRevisionRecord(
    string ReceiptId,
    int RevisionNumber,
    string EditorIdentity,
    DateTime CreatedUtc,
    string CorrectedJson,
    string? ChangeSummary);
