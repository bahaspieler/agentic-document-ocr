using Azure.Data.Tables;

namespace OcrAgent.Runs;

/// <summary>
/// <see cref="IRunStore"/> backed by an Azure Table Storage table.
/// </summary>
/// <remarks>
/// Every record for one deployment shares a partition key, so listing history is a single partition
/// query. The row key leads with a descending tick count derived from the run id (see
/// <see cref="RunId.ToRowKey"/>), which makes that query return newest first without sorting, while
/// still allowing <see cref="GetAsync"/> to address a record directly instead of scanning for it.
/// </remarks>
public sealed class AzureTableRunStore : IRunStore
{
    // Table Storage caps a string property at 64 KiB. These stay well under it and keep rows small
    // enough that listing history does not pull megabytes of trace data.
    private const int MaxErrorChars = 8_000;
    private const int MaxArgumentsChars = 4_000;
    private const int MaxTraceChars = 30_000;

    private readonly TableClient _table;
    private readonly string _partitionKey;

    /// <summary>Connects to the table and creates it when configured to.</summary>
    /// <param name="config">Connection string, table name, and group id.</param>
    public AzureTableRunStore(RunStoreConfiguration config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        config.Validate();

        _table = new TableClient(config.ConnectionString, config.TableName);
        if (config.CreateTableIfNotExists)
            _table.CreateIfNotExists();

        _partitionKey = config.GroupId;
    }

    /// <summary>Table this store reads from and writes to.</summary>
    public string TableName => _table.Name;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public async Task SaveAsync(RunRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));

        await _table.UpsertEntityAsync(ToEntity(record), TableUpdateMode.Replace, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RunRecord?> GetAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId) || !RunId.TryGetTimestamp(runId, out _))
            return null;

        var response = await _table
            .GetEntityIfExistsAsync<TableEntity>(_partitionKey, RunId.ToRowKey(runId), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return response.HasValue && response.Value is { } entity ? FromEntity(entity) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RunRecord>> ListAsync(
        int top = 20, string? mode = null, CancellationToken cancellationToken = default)
    {
        if (top < 1) top = 1;

        var filter = string.IsNullOrWhiteSpace(mode)
            ? TableClient.CreateQueryFilter($"PartitionKey eq {_partitionKey}")
            : TableClient.CreateQueryFilter($"PartitionKey eq {_partitionKey} and Mode eq {mode!.ToLowerInvariant()}");

        var records = new List<RunRecord>(top);

        await foreach (var entity in _table
            .QueryAsync<TableEntity>(filter, maxPerPage: top, cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        {
            records.Add(FromEntity(entity));
            if (records.Count >= top)
                break;
        }

        return records;
    }

    private TableEntity ToEntity(RunRecord record)
    {
        var entity = new TableEntity(_partitionKey, RunId.ToRowKey(record.RunId))
        {
            ["RunId"] = record.RunId,
            ["Mode"] = record.Mode,
            ["Name"] = record.Name,
            ["Source"] = record.Source,
            ["InputReference"] = record.InputReference,
            ["OutputReference"] = record.OutputReference,
            ["StepCount"] = record.StepCount,
            ["Success"] = record.Success,
            ["StartTimeUtc"] = record.StartTimeUtc,
            ["EndTimeUtc"] = record.EndTimeUtc,
            ["DurationMs"] = record.DurationMs,
        };

        SetIfPresent(entity, "Version", record.Version);
        SetIfPresent(entity, "OutputDirectory", record.OutputDirectory);
        SetIfPresent(entity, "StorageProvider", record.StorageProvider);
        SetIfPresent(entity, "Error", record.Error, MaxErrorChars);
        SetIfPresent(entity, "ArgumentsJson", record.ArgumentsJson, MaxArgumentsChars);
        SetIfPresent(entity, "TraceJson", record.TraceJson, MaxTraceChars);

        return entity;
    }

    private RunRecord FromEntity(TableEntity entity) => new()
    {
        RunId = entity.GetString("RunId") ?? "",
        Mode = entity.GetString("Mode") ?? "",
        Name = entity.GetString("Name") ?? "",
        Version = entity.GetString("Version"),
        Source = entity.GetString("Source") ?? "",
        GroupId = entity.PartitionKey,
        InputReference = entity.GetString("InputReference") ?? "",
        OutputReference = entity.GetString("OutputReference") ?? "",
        OutputDirectory = entity.GetString("OutputDirectory"),
        StepCount = entity.GetInt32("StepCount") ?? 0,
        Success = entity.GetBoolean("Success") ?? false,
        Error = entity.GetString("Error"),
        StartTimeUtc = entity.GetDateTimeOffset("StartTimeUtc") ?? default,
        EndTimeUtc = entity.GetDateTimeOffset("EndTimeUtc") ?? default,
        DurationMs = entity.GetInt64("DurationMs") ?? 0,
        StorageProvider = entity.GetString("StorageProvider"),
        ArgumentsJson = entity.GetString("ArgumentsJson"),
        TraceJson = entity.GetString("TraceJson"),
    };

    private static void SetIfPresent(TableEntity entity, string name, string? value, int maxChars = int.MaxValue)
    {
        if (string.IsNullOrEmpty(value))
            return;

        entity[name] = value!.Length > maxChars ? value[..maxChars] + "...[truncated]" : value;
    }
}
