using System.ComponentModel.DataAnnotations;

namespace OcrAgent.Sql;

/// <summary>Configuration for durable conversations, users, and receipt data in Azure SQL.</summary>
public sealed class SqlConfiguration
{
    /// <summary>When false, the application uses no-op stores and needs no database.</summary>
    public bool Enabled { get; set; }

    /// <summary>Encrypted Azure SQL connection string, supplied through configuration secrets.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>Creates the Phase 7 tables and indexes when they do not exist.</summary>
    public bool EnsureSchema { get; set; } = true;

    /// <summary>Validates settings required when SQL is enabled.</summary>
    public void Validate()
    {
        if (Enabled && string.IsNullOrWhiteSpace(ConnectionString))
            throw new ValidationException(
                "Sql:ConnectionString is required when Sql:Enabled is true. Set OCR_Sql__ConnectionString.");
    }
}
