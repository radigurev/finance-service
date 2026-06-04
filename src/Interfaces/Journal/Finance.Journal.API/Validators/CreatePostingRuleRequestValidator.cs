using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Posting;
using FluentValidation;

namespace Finance.Journal.API.Validators;

/// <summary>
/// FluentValidation shape rules for <see cref="CreatePostingRuleRequest"/> (SDD-FIN-006 §3.1): a required
/// uppercase machine key (≤ 50 chars), at least one line, and per-line shape. The stateful invariants
/// (duplicate key, structural balance) run through the validation chain in the service.
/// </summary>
public sealed class CreatePostingRuleRequestValidator : AbstractValidator<CreatePostingRuleRequest>
{
    /// <summary>Configures the create-request shape rules.</summary>
    public CreatePostingRuleRequestValidator()
    {
        RuleFor(request => request.RuleKey)
            .NotEmpty().WithErrorCode(PostingErrorCodes.INVALID_POSTING_RULE_KEY)
            .MaximumLength(50).WithErrorCode(PostingErrorCodes.INVALID_POSTING_RULE_KEY)
            .Must(key => key == key.ToUpperInvariant()).WithErrorCode(PostingErrorCodes.INVALID_POSTING_RULE_KEY);

        RuleFor(request => request.Lines)
            .Must(lines => lines is { Count: >= 1 })
            .WithErrorCode(PostingErrorCodes.POSTING_RULE_HAS_NO_LINES);

        RuleForEach(request => request.Lines)
            .SetValidator(new CreatePostingRuleLineRequestValidator());
    }
}
