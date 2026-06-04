using Finance.Country.Abstractions;
using Finance.Journal.DBModel.Models;

namespace Finance.Journal.API.Tests.Builders;

/// <summary>
/// Builds <see cref="PostingRule"/> entities for seeding the posting-engine resolution tests (SDD-FIN-006
/// §6.1). The default produces an active, structurally balanceable <c>SALE_INVOICE</c> rule whose lines map
/// <c>Gross</c> debit / <c>Net</c> + <c>Tax</c> credit; tests override the key, active flag, or lines.
/// </summary>
public sealed class PostingRuleBuilder
{
    private string _ruleKey = "SALE_INVOICE";
    private string _description = "Sale invoice posting rule.";
    private string _countryCode = "BG";
    private bool _isActive = true;
    private List<PostingRuleLine> _lines =
    [
        Line(1, "411", PostingDebitOrCredit.Debit, PostingAmountSource.Gross),
        Line(2, "701", PostingDebitOrCredit.Credit, PostingAmountSource.Net),
        Line(3, "4532", PostingDebitOrCredit.Credit, PostingAmountSource.Tax)
    ];

    /// <summary>Creates a builder seeded with valid defaults.</summary>
    /// <returns>A new builder.</returns>
    public static PostingRuleBuilder Create() => new();

    /// <summary>Overrides the rule key.</summary>
    /// <param name="ruleKey">The machine key to use.</param>
    /// <returns>This builder.</returns>
    public PostingRuleBuilder WithRuleKey(string ruleKey)
    {
        _ruleKey = ruleKey;
        return this;
    }

    /// <summary>Sets the active flag.</summary>
    /// <param name="isActive">Whether the rule is active.</param>
    /// <returns>This builder.</returns>
    public PostingRuleBuilder WithIsActive(bool isActive)
    {
        _isActive = isActive;
        return this;
    }

    /// <summary>Replaces the rule's lines, assigning sequential line numbers.</summary>
    /// <param name="lines">The (selector, side, source) line tuples.</param>
    /// <returns>This builder.</returns>
    public PostingRuleBuilder WithLines(params (string Selector, PostingDebitOrCredit Side, PostingAmountSource Source)[] lines)
    {
        int lineNumber = 1;
        _lines = lines
            .Select(line => Line(lineNumber++, line.Selector, line.Side, line.Source))
            .ToList();
        return this;
    }

    /// <summary>Materializes the configured posting-rule entity.</summary>
    /// <returns>The built entity.</returns>
    public PostingRule Build() => new()
    {
        RuleKey = _ruleKey,
        Description = _description,
        CountryCode = _countryCode,
        IsActive = _isActive,
        CreatedAt = DateTimeOffset.UtcNow,
        Lines = _lines
    };

    private static PostingRuleLine Line(
        int lineNumber,
        string accountSelector,
        PostingDebitOrCredit debitOrCredit,
        PostingAmountSource amountSource) => new()
    {
        LineNumber = lineNumber,
        AccountSelector = accountSelector,
        DebitOrCredit = debitOrCredit,
        AmountSource = amountSource
    };
}
