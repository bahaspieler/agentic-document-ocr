using System.Security.Cryptography;

namespace OcrAgent.Storage;

/// <summary>
/// Generates random image ids and validates, splits, and qualifies caller-supplied references.
/// </summary>
/// <remarks>
/// A reference carries the scope it was written under, so a workflow output stays resolvable after
/// the run that produced it has released its write scope. Segments are separated by
/// <see cref="ScopeSeparator"/>: everything before the last one is the scope, the last one is the
/// name of the file within it.
/// </remarks>
public static class ImageReferenceGenerator
{
    /// <summary>Separates the scope segments of a reference from its leaf name.</summary>
    public const char ScopeSeparator = '/';

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no confusing chars

    /// <summary>Creates a cryptographically random alphanumeric reference.</summary>
    /// <param name="length">Number of characters in the id.</param>
    /// <returns>A new reference string.</returns>
    public static string NewRef(int length = 8)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);

        char[] chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];

        return new string(chars);
    }

    /// <summary>Trims the reference and rejects empty segments or invalid characters.</summary>
    /// <remarks>
    /// Each segment allows letters, digits, hyphen, and underscore, which leaves no way to spell a
    /// parent directory: <c>.</c> is not an allowed character, so a scope cannot walk out of the
    /// store it belongs to.
    /// </remarks>
    /// <param name="reference">Raw reference string, optionally scope-qualified.</param>
    /// <returns>Trimmed reference.</returns>
    public static string Normalize(string reference)
    {
        reference = reference.Trim();
        if (reference.Length == 0) throw new ArgumentException("Reference is empty.");

        foreach (var segment in reference.Split(ScopeSeparator))
        {
            if (segment.Length == 0)
                throw new ArgumentException($"Empty segment in reference: '{reference}'");

            foreach (char c in segment)
            {
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                    throw new ArgumentException($"Invalid character in reference: '{c}'");
            }
        }

        return reference;
    }

    /// <summary>Separates the scope a reference names from the file name within it.</summary>
    /// <param name="reference">Raw reference string, optionally scope-qualified.</param>
    /// <returns>The scope, null when the reference names none, and the leaf name.</returns>
    public static (string? Scope, string Leaf) Split(string reference)
    {
        reference = Normalize(reference);

        int separator = reference.LastIndexOf(ScopeSeparator);
        return separator < 0
            ? (null, reference)
            : (reference[..separator], reference[(separator + 1)..]);
    }

    /// <summary>Prefixes a leaf name with the scope it was written under.</summary>
    /// <param name="scope">Store subdirectory, or null at the store root.</param>
    /// <param name="leaf">File name within the scope.</param>
    /// <returns>The qualified reference, or <paramref name="leaf"/> unchanged at the root.</returns>
    public static string Qualify(string? scope, string leaf)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return leaf;

        return $"{scope.Replace('\\', ScopeSeparator).Trim(ScopeSeparator)}{ScopeSeparator}{leaf}";
    }
}
