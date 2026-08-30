namespace OcrAgent.Sql;

/// <summary>The three SQL-backed capabilities initialized as one unit.</summary>
public sealed record SqlStores(
    ISessionStore Sessions,
    IUserStore Users,
    IReceiptStore Receipts)
{
    public static SqlStores Disabled { get; } =
        new(NullSessionStore.Instance, NullUserStore.Instance, NullReceiptStore.Instance);
}
