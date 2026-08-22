using System.Diagnostics;
using OcrAgent.Storage;

namespace OcrAgent.Runs;

/// <summary>
/// Helpers shared by the two places that write run records: the workflow runner and the tool decorator.
/// </summary>
internal static class RunRecording
{
    /// <summary>
    /// Writes a record, swallowing any failure.
    /// </summary>
    /// <remarks>
    /// Recording is bookkeeping. A job that produced its output has succeeded whether or not the row
    /// describing it reached the table, so a storage problem here is reported and dropped rather than
    /// turned into a failed job.
    /// </remarks>
    /// <param name="store">Target store; null and the no-op store are ignored.</param>
    /// <param name="record">Record to write.</param>
    public static async Task SafeSaveAsync(IRunStore? store, RunRecord record)
    {
        if (store == null || !store.IsEnabled)
        {
            RunEventScope.Report(record);
            return;
        }

        try
        {
            await store.SaveAsync(record).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WARN] Could not record run {record.RunId}: {ex.Message}");
        }
        finally
        {
            RunEventScope.Report(record);
        }
    }

    /// <summary>Names the image store backing a run, for the record's provider column.</summary>
    /// <param name="store">Store in use, if any.</param>
    /// <returns>Provider id matching the storage configuration, or null.</returns>
    public static string? DescribeProvider(IImageStore? store) => store switch
    {
        null => null,
        AzureBlobImageStore => "azureblob",
        SftpImageStore => "sftp",
        LocalFileImageStore => "local",
        _ => store.GetType().Name
    };
}
