using Finance.Common.ErrorCodes;
using Finance.Country.Abstractions;
using Finance.ServiceModel.Posting;
using FluentValidation;

namespace Finance.Journal.API.Validators;

/// <summary>
/// FluentValidation shape rules for a single <see cref="CreatePostingRuleLineRequest"/> (SDD-FIN-006 §3.1):
/// a non-empty account selector and valid <c>DebitOrCredit</c>/<c>AmountSource</c> enums.
/// </summary>
public sealed class CreatePostingRuleLineRequestValidator : AbstractValidator<CreatePostingRuleLineRequest>
{
    /// <summary>Configures the line-request shape rules.</summary>
    public CreatePostingRuleLineRequestValidator()
    {
        RuleFor(line => line.AccountSelector)
            .NotEmpty()
            .WithErrorCode(PostingErrorCodes.INVALID_POSTING_RULE_LINE);

        RuleFor(line => line.DebitOrCredit)
            .IsInEnum()
            .WithErrorCode(PostingErrorCodes.INVALID_POSTING_RULE_LINE);

        RuleFor(line => line.AmountSource)
            .IsInEnum()
            .WithErrorCode(PostingErrorCodes.INVALID_POSTING_RULE_LINE);
    }
}
