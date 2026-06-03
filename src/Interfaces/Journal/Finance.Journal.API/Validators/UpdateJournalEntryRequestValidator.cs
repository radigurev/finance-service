using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Journal;
using FluentValidation;

namespace Finance.Journal.API.Validators;

/// <summary>
/// FluentValidation shape rules for <see cref="UpdateJournalEntryRequest"/> (SDD-FIN-002 §3.1): a
/// required accounting date, at least two lines, per-line shape, and a supplied row version for
/// optimistic concurrency.
/// </summary>
public sealed class UpdateJournalEntryRequestValidator : AbstractValidator<UpdateJournalEntryRequest>
{
    /// <summary>Configures the update-request shape rules.</summary>
    public UpdateJournalEntryRequestValidator()
    {
        RuleFor(request => request.EntryDate)
            .Must(date => date != default)
            .WithErrorCode(JournalErrorCodes.INVALID_ENTRY_DATE);

        RuleFor(request => request.Lines)
            .Must(lines => lines is { Count: >= 2 })
            .WithErrorCode(JournalErrorCodes.MIN_TWO_LINES_REQUIRED);

        RuleForEach(request => request.Lines)
            .SetValidator(new JournalEntryLineRequestValidator());

        RuleFor(request => request.RowVersion)
            .NotEmpty().WithErrorCode(CommonErrorCodes.CONCURRENT_MODIFICATION);
    }
}
