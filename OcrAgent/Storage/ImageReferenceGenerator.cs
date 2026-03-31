using System.Security.Cryptography;

namespace OcrAgent.Storage;

/// <summary>
/// Generates random image ids and validates/normalizes caller-supplied references.
/// </summary>
public static class ImageReferenceGenerator
{
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

    /// <summary>Trims the reference and rejects empty or invalid characters.</summary>
    /// <param name="reference">Raw reference string.</param>
    /// <returns>Trimmed reference.</returns>
    public static string Normalize(string reference)
    {
        reference = reference.Trim();
        if (reference.Length == 0) throw new ArgumentException("Reference is empty.");

        foreach (char c in reference)
        {
            if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                throw new ArgumentException($"Invalid character in reference: '{c}'");
        }
        return reference;
    }
}
