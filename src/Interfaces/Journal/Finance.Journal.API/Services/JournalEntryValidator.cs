using Finance.Common.Results;
using Finance.Common.Validation;
using Finance.Journal.API.Interfaces;
using Finance.Journal.API.Validation;
using FluentValidation;
using FluentValidation.Results;

namespace Finance.Journal.API.Services;

/// <summary>
/// The single double-entry validation surface (SDD-FIN-001 §2.8). Runs the FluentValidation shape rules
/// (min-two-lines, debit-XOR-credit, no-zero, currency shape) first, then the cross-aggregate chain
/// (balance, account postability, currency validity, base-amount reconciliation), surfacing the first
/// violated domain code as a <see cref="Result"/>. It is pure with respect to lifecycle.
/// </summary>
public sealed class JournalEntryValidator : IJournalEntryValidator
{
    private readonly IValidator<JournalEntryValidationContext> _shapeValidator;
    private readonly ValidationChain<JournalEntryValidationContext> _chain;

    /// <summary>Creates a new <see cref="JournalEntryValidator"/>.</summary>
    /// <param name="shapeValidator">The FluentValidation shape validator over the validation context.</param>
    /// <param name="chain">The cross-aggregate validation chain (balance, accounts, currency, base amount).</param>
    public JournalEntryValidator(
        IValidator<JournalEntryValidationContext> shapeValidator,
        ValidationChain<JournalEntryValidationContext> chain)
    {
        _shapeValidator = shapeValidator;
        _chain = chain;
    }

    /// <inheritdoc />
    public async Task<Result> ValidateAsync(JournalEntryValidationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        ValidationResult shapeResult =
            await _shapeValidator.ValidateAsync(context, cancellationToken).ConfigureAwait(false);
        if (!shapeResult.IsValid)
        {
            ValidationFailure failure = shapeResult.Errors[0];
            return Result.Failure(failure.ErrorCode, failure.ErrorMessage);
        }

        ChainValidationResult chainResult =
            await _chain.ValidateAsync(context, cancellationToken).ConfigureAwait(false);
        if (!chainResult.IsValid)
        {
            return Result.Failure(chainResult.ErrorCode!, chainResult.Detail);
        }

        return Result.Success();
    }
}
