namespace Finance.Journal.API.Interfaces;

/// <summary>
/// Read-only seam used by the cross-aggregate journal validators to assert account postability and
/// currency validity without a cross-database join (SDD-FIN-001 §2.6, §2.7; resolved §7). The default
/// implementation reads the Accounts and Nomenclature services through the Finance Gateway; the seam is
/// kept narrow so unit tests can substitute an in-memory reader.
/// </summary>
public interface IReferenceDataReader
{
    /// <summary>
    /// Determines whether the supplied account is a valid posting target: it MUST exist and be active
    /// (SDD-FIN-001 §2.6). A missing account, an inactive account, or an unreachable read all resolve to
    /// not-postable so a line can never post against an unverified account.
    /// </summary>
    /// <param name="accountId">The posting-target account identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns><see langword="true"/> when the account is postable; otherwise <see langword="false"/>.</returns>
    Task<bool> IsAccountPostableAsync(int accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Determines whether the supplied currency code is a valid, active currency (SDD-FIN-001 §2.7).
    /// A missing or inactive currency, or an unreachable read, resolves to not-valid.
    /// </summary>
    /// <param name="currencyCode">The ISO 4217 alphabetic currency code.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns><see langword="true"/> when the currency is valid and active; otherwise <see langword="false"/>.</returns>
    Task<bool> IsCurrencyActiveAsync(string currencyCode, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the display <c>code</c> / <c>name</c> for a set of accounts so the GL / trial-balance read
    /// path can enrich its rows without an N-per-account round-trip (SDD-FIN-003 §2.5). The lookup is
    /// resilient: an account whose read fails or is missing is simply omitted from the returned map, so the
    /// caller still surfaces that account's numeric balances with a null code/name. Enrichment MUST NOT
    /// fail the whole query.
    /// </summary>
    /// <param name="accountIds">The distinct account identifiers to resolve.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A map from account identifier to its resolved <see cref="AccountReference"/>; missing accounts are absent.</returns>
    Task<IReadOnlyDictionary<int, AccountReference>> GetAccountReferencesAsync(
        IReadOnlyCollection<int> accountIds,
        CancellationToken cancellationToken);
}
