using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Journal;
using FluentValidation;

namespace Finance.Journal.API.Validators;

/// <summary>
/// FluentValidation shape rules for a single <see cref="JournalEntryLineRequest"/> (SDD-FIN-001 §2.4,
/// §3.1): exactly one of debit/credit is a positive amount (debit-XOR-credit, no zero, no negatives) and
/// the currency code is three uppercase letters.
/// </summary>
public sealed class JournalEntryLineRequestValidator : AbstractValidator<JournalEntryLineRequest>
{
    /// <summary>Configures the per-line shape rules.</summary>
    public JournalEntryLineRequestValidator()
    {
        RuleFor(line => line)
            .Must(HaveExactlyOnePositiveSide)
            .WithErrorCode(JournalErrorCodes.LINE_HAS_NO_AMOUNT)
            .When(line => !BothSidesPositive(line));

        RuleFor(line => line)
            .Must(line => !BothSidesPositive(line))
            .WithErrorCode(JournalErrorCodes.LINE_DEBIT_AND_CREDIT_SET);

        RuleFor(line => line.CurrencyCode)
            .Matches("^[A-Z]{3}$")
            .WithErrorCode(JournalErrorCodes.INVALID_LINE_CURRENCY);
    }

    private static bool BothSidesPositive(JournalEntryLineRequest line)
    {
        return line.DebitAmount > 0m && line.CreditAmount > 0m;
    }

    private static bool HaveExactlyOnePositiveSide(JournalEntryLineRequest line)
    {
        bool debitPositive = line.DebitAmount > 0m;
        bool creditPositive = line.CreditAmount > 0m;
        bool noNegatives = line.DebitAmount >= 0m && line.CreditAmount >= 0m;
        return noNegatives && (debitPositive ^ creditPositive);
    }
}
