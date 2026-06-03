using Finance.Common.ErrorCodes;
using Finance.Journal.API.Validation;
using FluentValidation;

namespace Finance.Journal.API.Validators;

/// <summary>
/// FluentValidation shape rules over the whole <see cref="JournalEntryValidationContext"/>
/// (SDD-FIN-001 §2.5, §3.1): at least two lines, and every line passes the per-line shape rules. Invoked
/// by <c>IJournalEntryValidator</c> before the cross-aggregate chain so a shape failure short-circuits.
/// </summary>
public sealed class JournalEntryShapeValidator : AbstractValidator<JournalEntryValidationContext>
{
    /// <summary>Configures the entry-level shape rules.</summary>
    public JournalEntryShapeValidator()
    {
        RuleFor(context => context.Lines)
            .Must(lines => lines is { Count: >= 2 })
            .WithErrorCode(JournalErrorCodes.MIN_TWO_LINES_REQUIRED);

        RuleForEach(context => context.Lines)
            .SetValidator(new JournalEntryLineRequestValidator());
    }
}
