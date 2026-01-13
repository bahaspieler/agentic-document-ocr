using System.Security.Cryptography;

namespace OcrAgent.Storage;

public static class ImageReferenceGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no confusing chars

    public static string NewRef(int length = 8)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);

        char[] chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];

        return new string(chars);
    }
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