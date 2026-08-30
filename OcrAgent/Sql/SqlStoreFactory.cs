namespace OcrAgent.Sql;

/// <summary>Validates SQL configuration, installs the schema, and creates the stores.</summary>
public static class SqlStoreFactory
{
    public static SqlStores Create(SqlConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.Enabled)
            return SqlStores.Disabled;

        configuration.Validate();
        var database = new SqlDatabase(configuration);
        if (configuration.EnsureSchema)
            database.EnsureSchemaAsync().GetAwaiter().GetResult();

        return new SqlStores(
            new SqlSessionStore(database),
            new SqlUserStore(database),
            new SqlReceiptStore(database));
    }

    /// <summary>Disables SQL features if startup cannot reach or initialize the database.</summary>
    public static SqlStores CreateOrNull(SqlConfiguration configuration)
    {
        try
        {
            return Create(configuration);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Warning: SQL features are disabled ({exception.Message})");
            return SqlStores.Disabled;
        }
    }
}
