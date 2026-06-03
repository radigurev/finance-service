using Finance.Common.ErrorCodes;
using Finance.Common.Validation;

namespace Finance.Journal.API.Validation;

/// <summary>
/// Cross-aggregate validator asserting the double-entry balance invariant (SDD-FIN-001 §2.3): the sum of
/// base-currency debits MUST equal the sum of base-currency credits, to the cent. Any non-zero residual
/// (even <c>0.01</c>) fails with <c>UNBALANCED_ENTRY</c>. The check runs on the <c>Base*</c> amounts, never
/// the transactional amounts, so a multi-currency entry that balances in base currency is balanced.
/// </summary>
public sealed class BalanceValidator : IChainValidator<JournalEntryValidationContext>
{
    /// <inheritdoc />
    public Task<ChainValidationResult> ValidateAsync(
        JournalEntryValidationContext request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        decimal totalBaseDebits = 0m;
        decimal totalBaseCredits = 0m;
        foreach (ServiceModel.Journal.JournalEntryLineRequest line in request.Lines)
        {
            totalBaseDebits += decimal.Round(line.BaseDebitAmount, 2, MidpointRounding.AwayFromZero);
            totalBaseCredits += decimal.Round(line.BaseCreditAmount, 2, MidpointRounding.AwayFromZero);
        }

        if (decimal.Round(totalBaseDebits - totalBaseCredits, 2, MidpointRounding.AwayFromZero) != 0m)
        {
            return Task.FromResult(ChainValidationResult.Failure(
                JournalErrorCodes.UNBALANCED_ENTRY,
                $"Base-currency debits ({totalBaseDebits:0.00}) do not equal base-currency credits ({totalBaseCredits:0.00})."));
        }

        return Task.FromResult(ChainValidationResult.Success());
    }
}
