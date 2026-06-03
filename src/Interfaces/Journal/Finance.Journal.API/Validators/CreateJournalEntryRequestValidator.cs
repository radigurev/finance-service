using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Journal;
using FluentValidation;

namespace Finance.Journal.API.Validators;

/// <summary>
/// FluentValidation shape rules for <see cref="CreateJournalEntryRequest"/> (SDD-FIN-002 §3.1): a
/// required accounting date, at least two lines, and per-line debit-XOR-credit / currency shape. The
/// stateful invariants (balance, account postability, currency validity) run through the
/// <c>IJournalEntryValidator</c> chain in the service.
/// </summary>
public sealed class CreateJournalEntryRequestValidator : AbstractValidator<CreateJournalEntryRequest>
{
    /// <summary>Configures the create-request shape rules.</summary>
    public CreateJournalEntryRequestValidator()
    {
        RuleFor(request => request.EntryDate)
            .Must(date => date != default)
            .WithErrorCode(JournalErrorCodes.INVALID_ENTRY_DATE);

        RuleFor(request => request.Lines)
            .Must(lines => lines is { Count: >= 2 })
            .WithErrorCode(JournalErrorCodes.MIN_TWO_LINES_REQUIRED);

        RuleForEach(request => request.Lines)
            .SetValidator(new JournalEntryLineRequestValidator());
    }
}
