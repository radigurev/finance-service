using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Journal;

namespace Finance.Journal.API.Validation;

/// <summary>
/// Cross-aggregate validator asserting that every line posts to a valid, postable account
/// (SDD-FIN-001 §2.6). Account state is read through <see cref="IReferenceDataReader"/> (a gateway read,
/// not a cross-database join). A missing, inactive, or unverifiable account fails the whole entry with
/// <c>ACCOUNT_NOT_POSTABLE</c>. Distinct account ids are checked once.
/// </summary>
public sealed class AccountPostabilityValidator : IChainValidator<JournalEntryValidationContext>
{
    private readonly IReferenceDataReader _referenceData;

    /// <summary>Creates a new <see cref="AccountPostabilityValidator"/>.</summary>
    /// <param name="referenceData">The read seam for account/currency state (SDD-FIN-001 §2.6).</param>
    public AccountPostabilityValidator(IReferenceDataReader referenceData)
    {
        _referenceData = referenceData;
    }

    /// <inheritdoc />
    public async Task<ChainValidationResult> ValidateAsync(
        JournalEntryValidationContext request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        HashSet<int> accountIds = [.. request.Lines.Select(line => line.AccountId)];
        foreach (int accountId in accountIds)
        {
            bool postable = await _referenceData.IsAccountPostableAsync(accountId, ct).ConfigureAwait(false);
            if (!postable)
            {
                return ChainValidationResult.Failure(
                    JournalErrorCodes.ACCOUNT_NOT_POSTABLE,
                    $"Account '{accountId}' is missing, inactive, or not a postable account.");
            }
        }

        return ChainValidationResult.Success();
    }
}
