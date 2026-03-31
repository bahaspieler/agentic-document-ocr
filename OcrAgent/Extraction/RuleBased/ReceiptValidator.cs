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
    /// <returns>Checks, warnings, score, and IsValid ≥ 50.</returns>
    public static ValidationInfo Validate(ReceiptData receipt)
    {
        var info = new ValidationInfo { IsValid = true };

        CheckSubtotalPlusTax(receipt.Totals, info);
        CheckItemsSum(receipt, info);
        CheckPaidVsTotal(receipt.Totals, info);
        CheckCurrencyConsistency(receipt, info);
        CheckRequiredFields(receipt, info);

        info.ValidationScore = ComputeScore(receipt, info);
        info.IsValid = info.ValidationScore >= 50;

        return info;
    }

    /// <summary>Weighted sum from fields, checks, items, and warnings.</summary>
    private static int ComputeScore(ReceiptData receipt, ValidationInfo info)
    {
        int score = 0;

        // Required fields present (up to 60 points)
        if (receipt.Totals.Total.HasValue) score += 20;
        if (!string.IsNullOrEmpty(receipt.Merchant?.Name)) score += 20;
        if (!string.IsNullOrEmpty(receipt.Transaction?.DateTime)) score += 20;

        // Mathematical checks passed (up to 45 points)
        foreach (var check in info.Checks)
        {
            if (check.Passed) score += 15;
        }

        // Has line items
        if (receipt.Items.Count > 0) score += 10;

        // No-warnings bonus
        if (info.Warnings.Count == 0) score += 5;

        return Math.Clamp(score, 0, 100);
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
