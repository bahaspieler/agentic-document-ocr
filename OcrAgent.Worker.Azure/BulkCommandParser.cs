using System.Text.RegularExpressions;

namespace OcrAgent.Worker.Azure;

/// <summary>Recognizes bulk intent and maps a command to an available dataset folder.</summary>
public static partial class BulkCommandParser
{
    /// <summary>Returns whether a command requests processing of every image in one folder.</summary>
    /// <param name="command">Natural-language queue command.</param>
    /// <returns><see langword="true"/> when bulk intent is explicit.</returns>
    public static bool IsBulkCommand(string command) =>
        !string.IsNullOrWhiteSpace(command) &&
        AllImagesRegex().IsMatch(command);

    /// <summary>Finds the uniquely named dataset mentioned in a bulk command.</summary>
    /// <param name="command">Natural-language queue command.</param>
    /// <param name="datasetNames">Dataset directory names available below images/.</param>
    /// <returns>The matching dataset name.</returns>
    public static string ResolveDatasetName(
        string command,
        IEnumerable<string> datasetNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var matches = datasetNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => Regex.IsMatch(
                command,
                $@"(?<![\p{{L}}\p{{N}}_-]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}_-])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                "The bulk command does not name an available dataset folder."),
            _ => throw new InvalidOperationException(
                "The bulk command matches more than one dataset folder.")
        };
    }

    [GeneratedRegex(
        @"\b(?:all|every)\b[\s\S]{0,40}\b(?:images?|files?|pictures?|receipts?)\b|\b(?:images?|files?|pictures?|receipts?)\b[\s\S]{0,40}\b(?:all|every)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AllImagesRegex();
}
