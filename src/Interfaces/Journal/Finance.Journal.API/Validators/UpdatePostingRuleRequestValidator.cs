using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Posting;
using FluentValidation;

namespace Finance.Journal.API.Validators;

/// <summary>
/// FluentValidation shape rules for <see cref="UpdatePostingRuleRequest"/> (SDD-FIN-006 §3.1): at least
/// one line, per-line shape, and a required row-version token. The stateful invariants (structural
/// balance, concurrency) run in the service.
/// </summary>
public sealed class UpdatePostingRuleRequestValidator : AbstractValidator<UpdatePostingRuleRequest>
{
    /// <summary>Configures the update-request shape rules.</summary>
    public UpdatePostingRuleRequestValidator()
    {
        RuleFor(request => request.RowVersion)
            .NotEmpty()
            .WithErrorCode(CommonErrorCodes.CONCURRENT_MODIFICATION);

        RuleFor(request => request.Lines)
            .Must(lines => lines is { Count: >= 1 })
            .WithErrorCode(PostingErrorCodes.POSTING_RULE_HAS_NO_LINES);

        RuleForEach(request => request.Lines)
            .SetValidator(new CreatePostingRuleLineRequestValidator());
    }
}
