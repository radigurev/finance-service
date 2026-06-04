using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Posting;
using FluentValidation;

namespace Finance.Journal.API.Validators;

/// <summary>
/// FluentValidation shape rules for <see cref="ApplyPostingRuleRequest"/> (SDD-FIN-006 §3.1): a required
/// rule key, a non-empty amount map of finite non-negative values, a 3-letter currency code, and a
/// required entry date. The stateful invariants (rule resolution, missing source, balance) run in the
/// engine.
/// </summary>
public sealed class ApplyPostingRuleRequestValidator : AbstractValidator<ApplyPostingRuleRequest>
{
    /// <summary>Configures the apply-request shape rules.</summary>
    public ApplyPostingRuleRequestValidator()
    {
        RuleFor(request => request.RuleKey)
            .NotEmpty()
            .WithErrorCode(PostingErrorCodes.INVALID_POSTING_RULE_KEY);

        RuleFor(request => request.Amounts)
            .Must(amounts => amounts is { Count: >= 1 } && amounts.Values.All(value => value >= 0m))
            .WithErrorCode(PostingErrorCodes.MISSING_POSTING_AMOUNT);

        RuleFor(request => request.CurrencyCode)
            .NotEmpty().WithErrorCode(JournalErrorCodes.INVALID_LINE_CURRENCY)
            .Length(3).WithErrorCode(JournalErrorCodes.INVALID_LINE_CURRENCY);

        RuleFor(request => request.EntryDate)
            .Must(date => date != default)
            .WithErrorCode(JournalErrorCodes.INVALID_ENTRY_DATE);
    }
}
