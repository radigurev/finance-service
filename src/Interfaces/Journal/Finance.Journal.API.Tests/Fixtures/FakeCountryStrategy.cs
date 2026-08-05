using Finance.Common.Enums;
using Finance.Country.Abstractions;

namespace Finance.Journal.API.Tests.Fixtures;

/// <summary>
/// Configurable in-memory <see cref="ICountryStrategy"/> substitute for the posting-rule seeder tests
/// (SDD-FIN-006 §6.3). Lets a test supply an arbitrary set of default posting-rule templates (including an
/// empty set or a structurally unbalanceable template) without depending on the real
/// <c>BulgariaStrategy</c>, so seeder behavior is exercised in isolation.
/// </summary>
public sealed class FakeCountryStrategy : ICountryStrategy
{
    private readonly IReadOnlyList<PostingRuleTemplate> _templates;

    /// <summary>Creates a fake strategy returning the supplied templates.</summary>
    /// <param name="templates">The default posting-rule templates to expose.</param>
    public FakeCountryStrategy(IReadOnlyList<PostingRuleTemplate> templates)
    {
        _templates = templates;
    }

    /// <inheritdoc />
    public string CountryCode => "BG";

    /// <inheritdoc />
    public string BaseCurrencyCode => "BGN";

    /// <inheritdoc />
    public decimal StandardTaxRate => 0.20m;

    /// <inheritdoc />
    public IReadOnlyList<PostingRuleTemplate> GetDefaultPostingRules() => _templates;

    /// <inheritdoc />
    public decimal ApplyTaxRounding(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <inheritdoc />
    public bool IsValidTaxRate(decimal rate) => rate >= 0m;

    /// <inheritdoc />
    public string GenerateDocumentNumber(InvoiceDocumentType documentType, long sequenceValue) =>
        $"{documentType}-{sequenceValue}";

    /// <inheritdoc />
    public string GenerateDocumentNumber(PaymentDocumentType documentType, long sequenceValue) =>
        $"{documentType}-{sequenceValue}";

    /// <summary>Builds a structurally balanceable template (one debit, one credit) for the given key.</summary>
    /// <param name="ruleKey">The template's rule key.</param>
    /// <returns>A balanceable template.</returns>
    public static PostingRuleTemplate BalanceableTemplate(string ruleKey) => new()
    {
        RuleKey = ruleKey,
        Description = ruleKey + " template.",
        CountryCode = "BG",
        Lines =
        [
            new PostingRuleLineTemplate
            {
                AccountSelector = "411",
                DebitOrCredit = PostingDebitOrCredit.Debit,
                AmountSource = PostingAmountSource.Gross
            },
            new PostingRuleLineTemplate
            {
                AccountSelector = "701",
                DebitOrCredit = PostingDebitOrCredit.Credit,
                AmountSource = PostingAmountSource.Net
            }
        ]
    };

    /// <summary>Builds a structurally unbalanceable template (debit-only) for the given key.</summary>
    /// <param name="ruleKey">The template's rule key.</param>
    /// <returns>An unbalanceable template.</returns>
    public static PostingRuleTemplate UnbalanceableTemplate(string ruleKey) => new()
    {
        RuleKey = ruleKey,
        Description = ruleKey + " template.",
        CountryCode = "BG",
        Lines =
        [
            new PostingRuleLineTemplate
            {
                AccountSelector = "411",
                DebitOrCredit = PostingDebitOrCredit.Debit,
                AmountSource = PostingAmountSource.Gross
            }
        ]
    };
}
