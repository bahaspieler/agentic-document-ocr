using OcrAgent.Extraction.Models;
using OcrAgent.Extraction.RuleBased;

namespace OcrAgent.Tests;

public class ReceiptValidatorTests
{
    [Fact]
    public void CriticalArithmeticFailureCapsScoreAndRequiresReview()
    {
        var receipt = ValidReceipt();
        receipt.Totals.Total = 99m;

        var result = ReceiptValidator.Validate(receipt);

        Assert.True(result.ValidationScore <= 69);
        Assert.Equal("needs_review", result.ReviewStatus);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void LowOcrConfidenceDoesNotChangeConsistencyScore()
    {
        var high = ValidReceipt();
        var low = ValidReceipt();
        low.Source.OcrConfidence = 12;

        var highResult = ReceiptValidator.Validate(high);
        var lowResult = ReceiptValidator.Validate(low);

        Assert.Equal(highResult.ValidationScore, lowResult.ValidationScore);
        Assert.Equal("checks_passed", highResult.ReviewStatus);
        Assert.Equal("needs_review", lowResult.ReviewStatus);
    }

    [Fact]
    public void WarningsRemoveBonusAndRequireReview()
    {
        var receipt = ValidReceipt();
        receipt.Totals.Currency = "";

        var result = ReceiptValidator.Validate(receipt);

        Assert.Contains("Currency not detected", result.Warnings);
        Assert.True(result.ValidationScore < 100);
        Assert.Equal("needs_review", result.ReviewStatus);
    }

    [Fact]
    public void PerfectConsistentReceiptPassesChecks()
    {
        var result = ReceiptValidator.Validate(ValidReceipt());

        Assert.Equal(100, result.ValidationScore);
        Assert.Equal("checks_passed", result.ReviewStatus);
        Assert.True(result.IsValid);
    }

    private static ReceiptData ValidReceipt() => new()
    {
        ReceiptId = "receipt-test",
        Source = new SourceInfo { OcrConfidence = 92 },
        Merchant = new MerchantInfo
        {
            Name = "Test Market",
            NameConfidence = FieldConfidence.High()
        },
        Transaction = new TransactionInfo
        {
            DateTime = "2026-08-22T12:00:00Z",
            DateTimeConfidence = FieldConfidence.High()
        },
        Totals = new TotalsInfo
        {
            Currency = "EUR",
            Subtotal = 10m,
            Tax = 2m,
            Total = 12m,
            Paid = 12m,
            TotalConfidence = FieldConfidence.High()
        },
        Items =
        [
            new ReceiptLineItem
            {
                Index = 0,
                Description = "Item",
                Quantity = 1,
                UnitPrice = 10m,
                LineTotal = 10m,
                Confidence = FieldConfidence.High()
            }
        ]
    };
}
