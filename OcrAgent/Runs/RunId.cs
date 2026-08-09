using System.Globalization;
using System.Security.Cryptography;

namespace OcrAgent.Runs;

/// <summary>
/// Time-sortable run identifiers (RFC 9562 UUID version 7) and the Table Storage row keys derived
/// from them.
/// </summary>
/// <remarks>
/// The engine multi-targets net8.0 and net9.0 and <c>Guid.CreateVersion7</c> exists only on the
/// latter, so the id is built here instead. That keeps ids identical whichever host mints them, and
/// it keeps the millisecond timestamp readable out of an existing id, which is what allows
/// <see cref="ToRowKey"/> to address a stored record without a lookup.
/// </remarks>
public static class RunId
{
    // rand_a, reused as the intra-millisecond counter described in RFC 9562 section 6.2.
    private const int CounterMax = 0xFFF;

    private static readonly object Gate = new();
    private static long _lastMs = -1;
    private static int _counter;

    /// <summary>Creates a new identifier in the usual hyphenated form.</summary>
    /// <returns>A UUIDv7 formatted with <c>"D"</c>.</returns>
    public static string NewId() => NewGuid().ToString("D");

    /// <summary>Creates a new UUIDv7.</summary>
    /// <returns>An id that sorts by creation time.</returns>
    public static Guid NewGuid()
    {
        long timestamp;
        int counter;

        lock (Gate)
        {
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (timestamp > _lastMs)
            {
                _lastMs = timestamp;
                _counter = 0;
            }
            else
            {
                // Same millisecond, or a clock that stepped backwards. Advance the counter so ids
                // stay strictly increasing, and borrow the next millisecond once it is exhausted.
                if (_counter >= CounterMax)
                {
                    _lastMs++;
                    _counter = 0;
                }
                else
                {
                    _counter++;
                }

                timestamp = _lastMs;
            }

            counter = _counter;
        }

        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes.Slice(8));

        bytes[0] = (byte)(timestamp >> 40);
        bytes[1] = (byte)(timestamp >> 32);
        bytes[2] = (byte)(timestamp >> 24);
        bytes[3] = (byte)(timestamp >> 16);
        bytes[4] = (byte)(timestamp >> 8);
        bytes[5] = (byte)timestamp;
        bytes[6] = (byte)(0x70 | (counter >> 8));
        bytes[7] = (byte)counter;
        bytes[8] = (byte)(0x80 | (bytes[8] & 0x3F));

        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>Reads the creation time embedded in an identifier.</summary>
    /// <param name="runId">A UUIDv7 produced by <see cref="NewId"/>.</param>
    /// <returns>The instant the id was created, to the millisecond.</returns>
    /// <exception cref="ArgumentException">The value is not a UUIDv7.</exception>
    public static DateTimeOffset GetTimestamp(string runId)
    {
        if (!TryGetTimestamp(runId, out var timestamp))
            throw new ArgumentException($"Not a valid run id: '{runId}'", nameof(runId));

        return timestamp;
    }

    /// <summary>Reads the creation time embedded in an identifier without throwing.</summary>
    /// <param name="runId">Candidate identifier.</param>
    /// <param name="timestamp">Creation time when the value is a UUIDv7.</param>
    /// <returns>True when <paramref name="runId"/> is a UUIDv7.</returns>
    public static bool TryGetTimestamp(string runId, out DateTimeOffset timestamp)
    {
        timestamp = default;

        if (string.IsNullOrWhiteSpace(runId) || !Guid.TryParse(runId, out var guid))
            return false;

        Span<byte> bytes = stackalloc byte[16];
        if (!guid.TryWriteBytes(bytes, bigEndian: true, out _))
            return false;

        if ((bytes[6] & 0xF0) != 0x70)
            return false;

        long unixMs = ((long)bytes[0] << 40)
                      | ((long)bytes[1] << 32)
                      | ((long)bytes[2] << 24)
                      | ((long)bytes[3] << 16)
                      | ((long)bytes[4] << 8)
                      | bytes[5];

        timestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        return true;
    }

    /// <summary>
    /// Builds the row key a record is stored under.
    /// </summary>
    /// <remarks>
    /// Table Storage sorts row keys ascending as strings, so the key leads with the tick count
    /// counted down from <see cref="DateTime.MaxValue"/>: listing a partition then returns the
    /// newest runs first without a sort. The timestamp comes from the id itself rather than from the
    /// clock, so the key can be rebuilt later from the id alone and reads stay point reads.
    /// </remarks>
    /// <param name="runId">A UUIDv7 produced by <see cref="NewId"/>.</param>
    /// <returns>Row key for the record.</returns>
    /// <exception cref="ArgumentException">The value is not a UUIDv7.</exception>
    public static string ToRowKey(string runId)
    {
        var descending = DateTime.MaxValue.Ticks - GetTimestamp(runId).UtcTicks;
        return string.Create(CultureInfo.InvariantCulture, $"{descending:D19}_{runId}");
    }
}
