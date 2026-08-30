namespace OcrAgent.Extraction.RuleBased;

/// <summary>Vertical band on the receipt for routing lines to extractors.</summary>
public enum ReceiptZone
{
    /// <summary>Top: merchant and address.</summary>
    Header,

    /// <summary>Middle: product lines.</summary>
    Body,

    /// <summary>Keywords and amounts before the footer.</summary>
    Totals,

    /// <summary>Bottom: trailing text after totals.</summary>
    Footer,
}
