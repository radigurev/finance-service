using System.Globalization;
using Finance.Common.Enums;
using Finance.Country.Abstractions;

namespace Finance.Country.BG;

/// <summary>
/// The Bulgarian <see cref="ICountryStrategy"/> (SDD-CTRY-001 §2.4): <c>CountryCode = "BG"</c>,
/// <c>BaseCurrencyCode = "BGN"</c>, and a small handful of НСС-flavoured default posting-rule templates.
/// Stateless and pure (Strategy pattern, plain DI; no factory/resolver in v1).
/// <para><b>SAMPLE / SEED DATA — pending accountant sign-off.</b> The posting-rule templates below
/// (and the exact НСС account codes 411/701/4532/601/4531/401/503) are an illustrative starting point,
/// NOT a claim of regulatory correctness. They MUST be validated by an accountant and are refined by the
/// fuller BG strategy (SDD-CTRY-BG-001) before production use (FINANCE-MICROSERVICES-PLAN §10 risk #1).
/// Once seeded into the SDD-FIN-006 rule store they are editable reference data.</para>
/// </summary>
public sealed class BulgariaStrategy : ICountryStrategy
{
    private const string Bg = "BG";
    private const int TaxDecimals = 2;
    private const int NumberPadding = 6;

    private static readonly IReadOnlyList<PostingRuleTemplate> DefaultRules = BuildDefaultRules();

    private const decimal StandardRate = 0.20m;

    private static readonly IReadOnlyList<decimal> ValidTaxRates = [0.20m, 0.09m, 0.00m];

    /// <inheritdoc />
    public string CountryCode => Bg;

    /// <inheritdoc />
    public string BaseCurrencyCode => "BGN";

    /// <inheritdoc />
    public decimal StandardTaxRate => StandardRate;

    /// <inheritdoc />
    public IReadOnlyList<PostingRuleTemplate> GetDefaultPostingRules() => DefaultRules;

    /// <inheritdoc />
    public decimal ApplyTaxRounding(decimal amount)
    {
        return Math.Round(amount, TaxDecimals, MidpointRounding.AwayFromZero);
    }

    /// <inheritdoc />
    public bool IsValidTaxRate(decimal rate)
    {
        if (rate < 0m)
        {
            return false;
        }

        foreach (decimal validRate in ValidTaxRates)
        {
            if (rate == validRate)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public string GenerateDocumentNumber(InvoiceDocumentType documentType, long sequenceValue)
    {
        string prefix = PrefixFor(documentType);
        string year = DateTimeOffset.UtcNow.Year.ToString(CultureInfo.InvariantCulture);
        string padded = sequenceValue.ToString(CultureInfo.InvariantCulture).PadLeft(NumberPadding, '0');
        return string.Concat(prefix, "-", year, "-", padded);
    }

    private static string PrefixFor(InvoiceDocumentType documentType) => documentType switch
    {
        InvoiceDocumentType.PurchaseInvoice => "ФПок",
        InvoiceDocumentType.SaleInvoice => "ФПр",
        InvoiceDocumentType.CreditNote => "КИ",
        InvoiceDocumentType.DebitNote => "ДИ",
        _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, null)
    };

    private static IReadOnlyList<PostingRuleTemplate> BuildDefaultRules() =>
    [
        BuildSaleInvoiceRule(),
        BuildPurchaseInvoiceRule(),
        BuildCustomerPaymentRule()
    ];

    private static PostingRuleTemplate BuildSaleInvoiceRule() => new()
    {
        RuleKey = "SALE_INVOICE",
        Description = "Sale invoice: debit customers (gross), credit sales revenue (net) and output VAT (tax).",
        CountryCode = Bg,
        Lines =
        [
            Line("411", PostingDebitOrCredit.Debit, PostingAmountSource.Gross),
            Line("701", PostingDebitOrCredit.Credit, PostingAmountSource.Net),
            Line("4532", PostingDebitOrCredit.Credit, PostingAmountSource.Tax)
        ]
    };

    private static PostingRuleTemplate BuildPurchaseInvoiceRule() => new()
    {
        RuleKey = "PURCHASE_INVOICE",
        Description = "Purchase invoice: debit expense/goods (net) and input VAT (tax), credit suppliers (gross).",
        CountryCode = Bg,
        Lines =
        [
            Line("601", PostingDebitOrCredit.Debit, PostingAmountSource.Net),
            Line("4531", PostingDebitOrCredit.Debit, PostingAmountSource.Tax),
            Line("401", PostingDebitOrCredit.Credit, PostingAmountSource.Gross)
        ]
    };

    private static PostingRuleTemplate BuildCustomerPaymentRule() => new()
    {
        RuleKey = "CUSTOMER_PAYMENT",
        Description = "Customer payment: debit bank (gross), credit customers (gross).",
        CountryCode = Bg,
        Lines =
        [
            Line("503", PostingDebitOrCredit.Debit, PostingAmountSource.Gross),
            Line("411", PostingDebitOrCredit.Credit, PostingAmountSource.Gross)
        ]
    };

    private static PostingRuleLineTemplate Line(
        string accountSelector,
        PostingDebitOrCredit debitOrCredit,
        PostingAmountSource amountSource) => new()
    {
        AccountSelector = accountSelector,
        DebitOrCredit = debitOrCredit,
        AmountSource = amountSource
    };
}
