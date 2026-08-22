using OcrAgent.Extraction.Models;

namespace OcrAgent.Extraction.RuleBased;

/// <summary>
/// Cross-checks totals vs items and required fields; sets score and pass flag.
/// </summary>
public static class ReceiptValidator
{
    private const decimal Tolerance = 0.05m;

    /// <summary>Runs all checks and computes <see cref="ValidationInfo.ValidationScore"/>.</summary>
    /// <param name="receipt">Data after extraction.</param>
    /// <returns>Checks, warnings, normalized consistency score, and review state.</returns>
    public static ValidationInfo Validate(ReceiptData receipt)
    {
        var info = new ValidationInfo { IsValid = true };

        CheckSubtotalPlusTax(receipt.Totals, info);
        CheckItemsSum(receipt, info);
        CheckPaidVsTotal(receipt.Totals, info);
        CheckCurrencyConsistency(receipt, info);
        CheckRequiredFields(receipt, info);

        info.ValidationScore = ComputeScore(receipt, info);
        info.ReviewStatus = ComputeReviewStatus(receipt, info);
        info.IsValid = info.ReviewStatus == "checks_passed";

        return info;
    }

    /// <summary>Normalized completeness and consistency score. It is not OCR confidence.</summary>
    private static int ComputeScore(ReceiptData receipt, ValidationInfo info)
    {
        int score = 0;

        // Required fields present (up to 30 points).
        if (receipt.Totals.Total.HasValue) score += 10;
        if (!string.IsNullOrEmpty(receipt.Merchant?.Name)) score += 10;
        if (!string.IsNullOrEmpty(receipt.Transaction?.DateTime)) score += 10;

        // Applicable mathematical checks share 50 points. Missing checks do not create phantom
        // points, and a failed critical check can never be hidden by clamping an oversized total.
        if (info.Checks.Count > 0)
            score += (int)Math.Round(50d * info.Checks.Count(check => check.Passed) / info.Checks.Count);

        // Has line items (10 points).
        if (receipt.Items.Count > 0) score += 10;

        // No-warnings bonus (10 points).
        if (info.Warnings.Count == 0) score += 10;

        bool criticalFailure = info.Checks.Any(check =>
            !check.Passed &&
            check.Name is "subtotal_plus_tax_equals_total" or "items_sum_equals_subtotal");
        if (criticalFailure)
            score = Math.Min(score, 69);

        return Math.Clamp(score, 0, 100);
    }

    private static string ComputeReviewStatus(ReceiptData receipt, ValidationInfo info)
    {
        bool criticalFailure = info.Checks.Any(check =>
            !check.Passed &&
            check.Name is "subtotal_plus_tax_equals_total" or "items_sum_equals_subtotal");
        double fieldConfidence = new[]
        {
            receipt.Merchant.NameConfidence.Value,
            receipt.Transaction.DateTimeConfidence.Value,
            receipt.Totals.TotalConfidence.Value,
        }.Average();

        return !criticalFailure &&
               info.Warnings.Count == 0 &&
               info.ValidationScore >= 85 &&
               receipt.Source.OcrConfidence >= 70 &&
               fieldConfidence >= 0.55
            ? "checks_passed"
            : "needs_review";
    }

    /// <summary>Compares subtotal + tax to total within tolerance.</summary>
    private static void CheckSubtotalPlusTax(TotalsInfo totals, ValidationInfo info)
    {
        if (totals.Subtotal == null || totals.Tax == null || totals.Total == null)
        {
            if (totals.Total == null)
                info.Warnings.Add("Total amount not detected");
            return;
        }

        var computed = totals.Subtotal.Value + totals.Tax.Value;
        var diff = Math.Abs(computed - totals.Total.Value);

        info.Checks.Add(new ValidationCheck
        {
            Name = "subtotal_plus_tax_equals_total",
            Passed = diff <= Tolerance,
            Detail = $"Subtotal({totals.Subtotal:F2}) + Tax({totals.Tax:F2}) = {computed:F2}, Total = {totals.Total:F2}, diff = {diff:F2}"
        });
    }

    /// <summary>Sums line totals vs subtotal or total.</summary>
    private static void CheckItemsSum(ReceiptData receipt, ValidationInfo info)
    {
        if (receipt.Items.Count == 0) return;

        var itemsWithTotal = receipt.Items.Where(i => i.LineTotal.HasValue).ToList();
        if (itemsWithTotal.Count == 0) return;

        var itemsSum = itemsWithTotal.Sum(i => i.LineTotal!.Value);
        var target = receipt.Totals.Subtotal ?? receipt.Totals.Total;

        if (target == null) return;

        var diff = Math.Abs(itemsSum - target.Value);
        info.Checks.Add(new ValidationCheck
        {
            Name = "items_sum_equals_subtotal",
            Passed = diff <= Tolerance * itemsWithTotal.Count,
            Detail = $"Sum(items) = {itemsSum:F2}, Subtotal/Total = {target:F2}, diff = {diff:F2}"
        });
    }

    /// <summary>Ensures paid is not below total.</summary>
    private static void CheckPaidVsTotal(TotalsInfo totals, ValidationInfo info)
    {
        if (totals.Paid == null || totals.Total == null) return;

        info.Checks.Add(new ValidationCheck
        {
            Name = "paid_gte_total",
            Passed = totals.Paid.Value >= totals.Total.Value - Tolerance,
            Detail = $"Paid({totals.Paid:F2}) >= Total({totals.Total:F2})"
        });
    }

    /// <summary>Warns when currency string is empty.</summary>
    private static void CheckCurrencyConsistency(ReceiptData receipt, ValidationInfo info)
    {
        // All amounts should be in the same currency (basic check: currency field is set)
        if (string.IsNullOrEmpty(receipt.Totals.Currency))
        {
            info.Warnings.Add("Currency not detected");
        }
    }

    /// <summary>Warns on missing merchant name or transaction date/time.</summary>
    private static void CheckRequiredFields(ReceiptData receipt, ValidationInfo info)
    {
        if (string.IsNullOrEmpty(receipt.Merchant.Name))
            info.Warnings.Add("Merchant name not detected");

        if (string.IsNullOrEmpty(receipt.Transaction.DateTime))
            info.Warnings.Add("Transaction date/time not detected");
    }
}
