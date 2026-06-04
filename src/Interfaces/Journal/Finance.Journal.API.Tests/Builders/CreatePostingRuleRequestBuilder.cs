using Finance.Country.Abstractions;
using Finance.ServiceModel.Posting;

namespace Finance.Journal.API.Tests.Builders;

/// <summary>
/// Builds <see cref="CreatePostingRuleRequest"/> instances for the posting-rule CRUD tests (SDD-FIN-006
/// §6.2). The default produces a valid, structurally balanceable two-line rule (one debit, one credit);
/// tests override only what they exercise.
/// </summary>
public sealed class CreatePostingRuleRequestBuilder
{
    private string _ruleKey = "SALE_INVOICE";
    private string _description = "Sale invoice posting rule.";
    private List<CreatePostingRuleLineRequest> _lines =
    [
        new()
        {
            AccountSelector = "411",
            DebitOrCredit = PostingDebitOrCredit.Debit,
            AmountSource = PostingAmountSource.Gross
        },
        new()
        {
            AccountSelector = "701",
            DebitOrCredit = PostingDebitOrCredit.Credit,
            AmountSource = PostingAmountSource.Net
        }
    ];

    /// <summary>Creates a builder seeded with valid defaults.</summary>
    /// <returns>A new builder.</returns>
    public static CreatePostingRuleRequestBuilder Create() => new();

    /// <summary>Overrides the rule key.</summary>
    /// <param name="ruleKey">The machine key to use.</param>
    /// <returns>This builder.</returns>
    public CreatePostingRuleRequestBuilder WithRuleKey(string ruleKey)
    {
        _ruleKey = ruleKey;
        return this;
    }

    /// <summary>Overrides the description.</summary>
    /// <param name="description">The description to use.</param>
    /// <returns>This builder.</returns>
    public CreatePostingRuleRequestBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    /// <summary>Replaces the rule's lines.</summary>
    /// <param name="lines">The lines to use.</param>
    /// <returns>This builder.</returns>
    public CreatePostingRuleRequestBuilder WithLines(params CreatePostingRuleLineRequest[] lines)
    {
        _lines = [.. lines];
        return this;
    }

    /// <summary>Replaces the lines with a single debit line (structurally unbalanceable).</summary>
    /// <returns>This builder.</returns>
    public CreatePostingRuleRequestBuilder WithAllDebitLines()
    {
        _lines =
        [
            Line("411", PostingDebitOrCredit.Debit, PostingAmountSource.Gross),
            Line("412", PostingDebitOrCredit.Debit, PostingAmountSource.Net)
        ];
        return this;
    }

    /// <summary>Replaces the lines with credit-only lines (structurally unbalanceable).</summary>
    /// <returns>This builder.</returns>
    public CreatePostingRuleRequestBuilder WithAllCreditLines()
    {
        _lines =
        [
            Line("701", PostingDebitOrCredit.Credit, PostingAmountSource.Net),
            Line("4532", PostingDebitOrCredit.Credit, PostingAmountSource.Tax)
        ];
        return this;
    }

    /// <summary>Replaces the lines with an empty set.</summary>
    /// <returns>This builder.</returns>
    public CreatePostingRuleRequestBuilder WithNoLines()
    {
        _lines = [];
        return this;
    }

    /// <summary>Materializes the configured create request.</summary>
    /// <returns>The built request.</returns>
    public CreatePostingRuleRequest Build() => new()
    {
        RuleKey = _ruleKey,
        Description = _description,
        Lines = _lines
    };

    private static CreatePostingRuleLineRequest Line(
        string accountSelector,
        PostingDebitOrCredit debitOrCredit,
        PostingAmountSource amountSource) => new()
    {
        AccountSelector = accountSelector,
        DebitOrCredit = debitOrCredit,
        AmountSource = amountSource
    };
}
