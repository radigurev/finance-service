using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Journal;
using FluentValidation;

namespace Finance.Journal.API.Validators;

/// <summary>
/// FluentValidation shape rules for <see cref="ReverseJournalEntryRequest"/> (SDD-FIN-002 §3.1): a
/// non-empty reason (reversal is on the SDD-AUDIT-001 mandatory-reason list) and a supplied row version.
/// </summary>
public sealed class ReverseJournalEntryRequestValidator : AbstractValidator<ReverseJournalEntryRequest>
{
    /// <summary>Configures the reverse-request shape rules.</summary>
    public ReverseJournalEntryRequestValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty().WithErrorCode(JournalErrorCodes.REVERSAL_REASON_REQUIRED);

        RuleFor(request => request.RowVersion)
            .NotEmpty().WithErrorCode(CommonErrorCodes.CONCURRENT_MODIFICATION);
    }
}
