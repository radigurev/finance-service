using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Journal;

namespace Finance.Journal.API.Validation;

/// <summary>
/// Cross-aggregate validator asserting that every line carries a valid, active currency
/// (SDD-FIN-001 §2.7). Currency state is read through <see cref="IReferenceDataReader"/> (a gateway read,
/// not a cross-database join). A missing or inactive currency fails the whole entry with
/// <c>INVALID_LINE_CURRENCY</c>. Distinct currency codes are checked once.
/// </summary>
public sealed class LineCurrencyValidator : IChainValidator<JournalEntryValidationContext>
{
    private readonly IReferenceDataReader _referenceData;

    /// <summary>Creates a new <see cref="LineCurrencyValidator"/>.</summary>
    /// <param name="referenceData">The read seam for account/currency state (SDD-FIN-001 §2.7).</param>
    public LineCurrencyValidator(IReferenceDataReader referenceData)
    {
        _referenceData = referenceData;
    }

    /// <inheritdoc />
    public async Task<ChainValidationResult> ValidateAsync(
        JournalEntryValidationContext request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        HashSet<string> currencyCodes = new(
            request.Lines.Select(line => line.CurrencyCode),
            StringComparer.OrdinalIgnoreCase);

        foreach (string currencyCode in currencyCodes)
        {
            bool active = await _referenceData.IsCurrencyActiveAsync(currencyCode, ct).ConfigureAwait(false);
            if (!active)
            {
                return ChainValidationResult.Failure(
                    JournalErrorCodes.INVALID_LINE_CURRENCY,
                    $"Currency '{currencyCode}' is missing or inactive.");
            }
        }

        return ChainValidationResult.Success();
    }
}
