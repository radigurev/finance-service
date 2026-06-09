using Finance.Common.Enums;
using Finance.Country.Abstractions;

namespace Finance.Invoices.API.Tests.Fixtures;

/// <summary>
/// Configurable in-memory <see cref="ICountryStrategy"/> substitute for the Invoices unit tests
/// (SDD-INV-001 §6.2-§6.4, SDD-INT-WH-001 §6). It rounds tax to two decimals away-from-zero (the BG rule),
/// recognizes any non-negative rate by default (overridable via <see cref="RecognizedRates"/>), and formats a
/// deterministic, type-prefixed document number so the country-owned numbering (SDD-CTRY-001 §5) is
/// observable without depending on the real <c>BulgariaStrategy</c>. It counts the tax-rounding and
/// number-format calls so a test can assert the core delegates rather than inlining the rule.
/// </summary>
public sealed class FakeInvoiceCountryStrategy : ICountryStrategy
{
    /// <summary>The deterministic base currency this fake books invoices in.</summary>
    public const string BaseCurrency = "BGN";

    private readonly IReadOnlyList<PostingRuleTemplate> _templates;

    /// <summary>Creates the fake with no posting-rule templates (the Invoice service never reads them).</summary>
    public FakeInvoiceCountryStrategy()
        : this([])
    {
    }

    /// <summary>Creates the fake exposing the supplied posting-rule templates.</summary>
    /// <param name="templates">The default posting-rule templates to expose.</param>
    public FakeInvoiceCountryStrategy(IReadOnlyList<PostingRuleTemplate> templates)
    {
        _templates = templates;
    }

    /// <inheritdoc />
    public string CountryCode => "BG";

    /// <inheritdoc />
    public string BaseCurrencyCode => BaseCurrency;

    /// <inheritdoc />
    public decimal StandardTaxRate => 0.20m;

    /// <summary>The set of rates this fake recognizes; <c>null</c> recognizes any non-negative rate.</summary>
    public IReadOnlySet<decimal>? RecognizedRates { get; set; }

    /// <summary>The number of times <see cref="ApplyTaxRounding"/> was invoked.</summary>
    public int ApplyTaxRoundingCallCount { get; private set; }

    /// <summary>The number of times <see cref="GenerateDocumentNumber"/> was invoked.</summary>
    public int GenerateDocumentNumberCallCount { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<PostingRuleTemplate> GetDefaultPostingRules() => _templates;

    /// <inheritdoc />
    public decimal ApplyTaxRounding(decimal amount)
    {
        ApplyTaxRoundingCallCount++;
        return Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    /// <inheritdoc />
    public bool IsValidTaxRate(decimal rate)
    {
        if (rate < 0m)
        {
            return false;
        }

        return RecognizedRates is null || RecognizedRates.Contains(rate);
    }

    /// <inheritdoc />
    public string GenerateDocumentNumber(InvoiceDocumentType documentType, long sequenceValue)
    {
        GenerateDocumentNumberCallCount++;
        return $"{PrefixFor(documentType)}-2026-{sequenceValue:000000}";
    }

    private static string PrefixFor(InvoiceDocumentType documentType) => documentType switch
    {
        InvoiceDocumentType.PurchaseInvoice => "PINV",
        InvoiceDocumentType.SaleInvoice => "SINV",
        InvoiceDocumentType.CreditNote => "CN",
        InvoiceDocumentType.DebitNote => "DN",
        _ => "INV"
    };
}
