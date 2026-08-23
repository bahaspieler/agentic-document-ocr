using OcrAgent.Extraction.Models;
using OcrAgent.Extraction.RuleBased;

namespace UnitTests.Cloud;

[TestClass]
public sealed class ReceiptValidatorTests
{
    [TestMethod]
    public void Validate_CriticalArithmeticFailureCapsScoreAndRequiresReview()
    {
        var receipt = ValidReceipt();
        receipt.Totals.Total = 99m;

        var result = ReceiptValidator.Validate(receipt);

        Assert.IsLessThanOrEqualTo(69, result.ValidationScore);
        Assert.AreEqual("needs_review", result.ReviewStatus);
        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void Validate_LowOcrConfidenceDoesNotChangeConsistencyScore()
    {
        var high = ValidReceipt();
        var low = ValidReceipt();
        low.Source.OcrConfidence = 12;

        var highResult = ReceiptValidator.Validate(high);
        var lowResult = ReceiptValidator.Validate(low);

        Assert.AreEqual(highResult.ValidationScore, lowResult.ValidationScore);
        Assert.AreEqual("checks_passed", highResult.ReviewStatus);
        Assert.AreEqual("needs_review", lowResult.ReviewStatus);
    }

    [TestMethod]
    public void Validate_WarningsRemoveBonusAndRequireReview()
    {
        var receipt = ValidReceipt();
        receipt.Totals.Currency = "";

        var result = ReceiptValidator.Validate(receipt);

        CollectionAssert.Contains(result.Warnings, "Currency not detected");
        Assert.IsLessThan(100, result.ValidationScore);
        Assert.AreEqual("needs_review", result.ReviewStatus);
    }

    [TestMethod]
    public void Validate_PerfectConsistentReceiptPassesChecks()
    {
        var result = ReceiptValidator.Validate(ValidReceipt());

        Assert.AreEqual(100, result.ValidationScore);
        Assert.AreEqual("checks_passed", result.ReviewStatus);
        Assert.IsTrue(result.IsValid);
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
