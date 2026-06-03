using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.ServiceModel.Journal;

namespace Finance.Journal.API.Validation;

/// <summary>
/// Cross-aggregate validator asserting per-line base-amount reconciliation and rate rules
/// (SDD-FIN-001 §2.7). For a base-currency line the rate MUST be exactly <c>1.000000</c>; for a foreign
/// line the rate MUST be <c>&gt; 0</c> and each <c>Base*Amount</c> MUST equal
/// <c>transactionalAmount × rate</c> rounded to two decimals, within half a cent. Any mismatch — or a
/// zero/negative rate on a foreign line — fails with <c>INVALID_LINE_BASE_AMOUNT</c>.
/// </summary>
public sealed class LineBaseAmountValidator : IChainValidator<JournalEntryValidationContext>
{
    private const decimal RoundingTolerance = 0.005m;

    /// <inheritdoc />
    public Task<ChainValidationResult> ValidateAsync(
        JournalEntryValidationContext request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (JournalEntryLineRequest line in request.Lines)
        {
            ChainValidationResult result = ValidateLine(line, request.BaseCurrencyCode);
            if (!result.IsValid)
            {
                return Task.FromResult(result);
            }
        }

        return Task.FromResult(ChainValidationResult.Success());
    }

    private static ChainValidationResult ValidateLine(JournalEntryLineRequest line, string baseCurrencyCode)
    {
        bool isBaseCurrency = string.Equals(line.CurrencyCode, baseCurrencyCode, StringComparison.OrdinalIgnoreCase);

        if (isBaseCurrency && line.ExchangeRate != 1.000000m)
        {
            return ChainValidationResult.Failure(
                JournalErrorCodes.INVALID_LINE_BASE_AMOUNT,
                $"A base-currency line MUST carry an exchange rate of 1.000000 but carried {line.ExchangeRate}.");
        }

        if (!isBaseCurrency && line.ExchangeRate <= 0m)
        {
            return ChainValidationResult.Failure(
                JournalErrorCodes.INVALID_LINE_BASE_AMOUNT,
                $"A foreign-currency line MUST carry a positive exchange rate but carried {line.ExchangeRate}.");
        }

        if (!Reconciles(line.DebitAmount, line.ExchangeRate, line.BaseDebitAmount) ||
            !Reconciles(line.CreditAmount, line.ExchangeRate, line.BaseCreditAmount))
        {
            return ChainValidationResult.Failure(
                JournalErrorCodes.INVALID_LINE_BASE_AMOUNT,
                "A line base amount does not reconcile with transactional amount × exchange rate.");
        }

        return ChainValidationResult.Success();
    }

    private static bool Reconciles(decimal transactionalAmount, decimal rate, decimal baseAmount)
    {
        decimal expected = decimal.Round(transactionalAmount * rate, 2, MidpointRounding.AwayFromZero);
        return Math.Abs(expected - baseAmount) < RoundingTolerance;
    }
}
