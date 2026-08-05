using Finance.Common.Enums;
using Finance.Country.Abstractions;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// Configurable in-memory <see cref="ICountryStrategy"/> substitute for the Payments unit tests
/// (SDD-PAY-001 §6.3, SDD-PAY-002 §6.3, SDD-PAY-003 §6.4). It rounds monetary amounts to two decimals
/// away-from-zero (the BG rule) and formats a deterministic, type-prefixed document number so the country-owned
/// numbering (SDD-CTRY-001 §5) is observable without depending on the real <c>BulgariaStrategy</c>. It counts the
/// rounding and number-format calls so a test can assert the core DELEGATES rather than inlining the rule.
/// </summary>
public sealed class FakePaymentCountryStrategy : ICountryStrategy
{
    /// <summary>The deterministic base currency this fake books payments in.</summary>
    public const string BaseCurrency = "BGN";

    /// <summary>The year the fake stamps into a formatted document number.</summary>
    public int DocumentNumberYear { get; set; } = 2026;

    /// <summary>The number of times <see cref="ApplyTaxRounding"/> was invoked.</summary>
    public int ApplyTaxRoundingCallCount { get; private set; }

    /// <summary>The number of times a <c>GenerateDocumentNumber</c> overload was invoked.</summary>
    public int GenerateDocumentNumberCallCount { get; private set; }

    /// <summary>The sequence values handed to the payment-typed <c>GenerateDocumentNumber</c>, in call order.</summary>
    public List<long> RequestedSequenceValues { get; } = [];

    /// <inheritdoc />
    public string CountryCode => "BG";

    /// <inheritdoc />
    public string BaseCurrencyCode => BaseCurrency;

    /// <inheritdoc />
    public decimal StandardTaxRate => 0.20m;

    /// <inheritdoc />
    public IReadOnlyList<PostingRuleTemplate> GetDefaultPostingRules() => [];

    /// <inheritdoc />
    public decimal ApplyTaxRounding(decimal amount)
    {
        ApplyTaxRoundingCallCount++;
        return Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    /// <inheritdoc />
    public bool IsValidTaxRate(decimal rate) => rate >= 0m;

    /// <inheritdoc />
    public string GenerateDocumentNumber(InvoiceDocumentType documentType, long sequenceValue)
    {
        GenerateDocumentNumberCallCount++;
        return $"{PrefixFor(documentType)}-{DocumentNumberYear}-{sequenceValue:000000}";
    }

    /// <inheritdoc />
    public string GenerateDocumentNumber(PaymentDocumentType documentType, long sequenceValue)
    {
        GenerateDocumentNumberCallCount++;
        RequestedSequenceValues.Add(sequenceValue);
        return $"{PrefixFor(documentType)}-{DocumentNumberYear}-{sequenceValue:000000}";
    }

    private static string PrefixFor(PaymentDocumentType documentType) => documentType switch
    {
        PaymentDocumentType.CustomerReceipt => "RCT",
        PaymentDocumentType.SupplierPayment => "PAY",
        _ => "PMT"
    };

    private static string PrefixFor(InvoiceDocumentType documentType) => documentType switch
    {
        InvoiceDocumentType.PurchaseInvoice => "PINV",
        InvoiceDocumentType.SaleInvoice => "SINV",
        InvoiceDocumentType.CreditNote => "CN",
        InvoiceDocumentType.DebitNote => "DN",
        _ => "INV"
    };
}
