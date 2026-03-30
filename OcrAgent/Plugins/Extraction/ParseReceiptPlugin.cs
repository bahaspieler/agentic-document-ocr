using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OcrAgent.Extraction;
using OcrAgent.Extraction.RuleBased;
using OcrAgent.Ocr;
using OcrAgent.Storage;

namespace OcrAgent.Plugins.Extraction;

/// <summary>
/// Workflow tool that parses OCR JSON output into structured receipt data
/// using rule-based extraction (zone classification, item/totals parsing).
/// </summary>
public sealed class ParseReceiptPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;
    private readonly IReceiptExtractor _extractor = new RuleBasedReceiptExtractor();

    public ParseReceiptPlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Parses OCR JSON output into structured receipt data (merchant, items, totals, validation) using rule-based extraction.")]
    public async Task<string> ParseReceipt(
        [Description("The reference of the image.")] string imageReference,
        [Description("The raw OCR JSON output from RunOcr.")] string ocrJson)
    {
        var ocrResult = JsonSerializer.Deserialize<OcrResult>(ocrJson)
            ?? throw new InvalidOperationException("Failed to deserialize OCR result.");

        var result = await _extractor.ExtractAsync(ocrResult);
        result.Receipt.ReceiptId = imageReference;
        result.Receipt.Source.InputImageRef = imageReference;

        return JsonSerializer.Serialize(result.Receipt, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.ParseReceipt);
    }
}
