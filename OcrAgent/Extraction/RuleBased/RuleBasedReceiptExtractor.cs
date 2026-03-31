using System.Diagnostics;
using OcrAgent.Extraction.Models;
using OcrAgent.Ocr;

namespace OcrAgent.Extraction.RuleBased;

/// <summary>
/// Rule pipeline: normalize tokens, assign vertical zones, run field extractors, validate.
/// </summary>
public sealed class RuleBasedReceiptExtractor : IReceiptExtractor
{
    /// <param name="ocrResult">Engine output with lines and words.</param>
    /// <param name="imagePath">Unused; reserved for image-aware extractors.</param>
    /// <returns>Receipt data, overall confidence, name, and elapsed ms.</returns>
    public Task<ExtractionResult> ExtractAsync(OcrResult ocrResult, string? imagePath = null)
    {
        var sw = Stopwatch.StartNew();

        var normalized = OcrTokenNormalizer.Normalize(ocrResult);

        // Vertical zones (header / body / totals / footer), then each extractor reads its zone(s).
        var zoned = ZoneClassifier.Classify(normalized);

        var merchant = MerchantFieldExtractor.Extract(zoned);
        var transaction = DateTimeFieldExtractor.Extract(zoned);
        var totals = TotalsFieldExtractor.Extract(zoned);
        var items = LineItemFieldExtractor.Extract(zoned);

        var receipt = new ReceiptData
        {
            Source = new SourceInfo
            {
                OcrEngineName = ocrResult.EngineName,
                OcrConfidence = ocrResult.Confidence,
                ExtractionMode = "rules",
            },
            Merchant = merchant,
            Transaction = transaction,
            Totals = totals,
            Items = items,
        };

        receipt.Validation = ReceiptValidator.Validate(receipt);

        sw.Stop();

        double overallConfidence = ComputeOverallConfidence(receipt);

        return Task.FromResult(new ExtractionResult
        {
            Receipt = receipt,
            OverallConfidence = overallConfidence,
            ExtractorName = "RuleBasedReceiptExtractor",
            DurationMs = sw.ElapsedMilliseconds,
        });
    }

    /// <summary>Mean of key field confidences plus a small bonus when validation passes.</summary>
    /// <param name="receipt">Filled receipt after extraction and validation.</param>
    /// <returns>0–1 aggregate or 0 when no scores exist.</returns>
    private static double ComputeOverallConfidence(ReceiptData receipt)
    {
        var scores = new List<double>();

        if (receipt.Merchant.NameConfidence.Value > 0)
            scores.Add(receipt.Merchant.NameConfidence.Value);
        if (receipt.Transaction.DateTimeConfidence.Value > 0)
            scores.Add(receipt.Transaction.DateTimeConfidence.Value);
        if (receipt.Totals.TotalConfidence.Value > 0)
            scores.Add(receipt.Totals.TotalConfidence.Value);

        if (receipt.Items.Count > 0)
        {
            var avgItemConf = receipt.Items.Average(i => i.Confidence.Value);
            scores.Add(avgItemConf);
        }

        if (receipt.Validation.IsValid)
            scores.Add(0.9);

        return scores.Count > 0 ? scores.Average() : 0.0;
    }
}
