using System.Collections.Concurrent;
using Finance.Journal.API.Interfaces;

namespace Finance.Journal.API.Tests.Integration.TestDoubles;

/// <summary>
/// In-memory <see cref="IReferenceDataReader"/> test double that stands in for the gateway-backed
/// <c>GatewayReferenceDataReader</c> (which fails closed against the non-running Finance Gateway). By
/// default every account is postable and every currency active so the double-entry validation chain
/// (<c>AccountPostabilityValidator</c>, <c>LineCurrencyValidator</c>) passes; account-selector codes
/// resolve through a configurable code→id map for the Posting Engine. Tests register specific accounts /
/// references to assert enrichment, or mark accounts not-postable to drive <c>ACCOUNT_NOT_POSTABLE</c>.
/// </summary>
public sealed class FakeReferenceDataReader : IReferenceDataReader
{
    private readonly ConcurrentDictionary<int, bool> _accountPostability = new();
    private readonly ConcurrentDictionary<string, bool> _currencyActivity =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int?> _codeToId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, AccountReference> _references = new();

    /// <summary>Default postability returned for accounts not explicitly registered.</summary>
    public bool DefaultPostable { get; set; } = true;

    /// <summary>Default currency activity returned for currencies not explicitly registered.</summary>
    public bool DefaultCurrencyActive { get; set; } = true;

    /// <summary>Marks an account as postable or not-postable, overriding the default.</summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="postable">Whether the account is a valid posting target.</param>
    public void SetAccountPostable(int accountId, bool postable) => _accountPostability[accountId] = postable;

    /// <summary>Maps an account-selector code to a postable account id (or <c>null</c> to make it unresolvable).</summary>
    /// <param name="code">The chart-of-accounts code.</param>
    /// <param name="accountId">The resolved account id, or <c>null</c> when the code resolves to nothing.</param>
    public void MapCodeToAccountId(string code, int? accountId) => _codeToId[code] = accountId;

    /// <summary>Registers a code/name enrichment reference for the supplied account id.</summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="code">The display account code.</param>
    /// <param name="name">The display account name.</param>
    public void SetReference(int accountId, string code, string name) =>
        _references[accountId] = new AccountReference(code, name);

    /// <inheritdoc />
    public Task<bool> IsAccountPostableAsync(int accountId, CancellationToken cancellationToken)
    {
        bool postable = _accountPostability.TryGetValue(accountId, out bool registered)
            ? registered
            : DefaultPostable;
        return Task.FromResult(postable);
    }

    /// <inheritdoc />
    public Task<bool> IsCurrencyActiveAsync(string currencyCode, CancellationToken cancellationToken)
    {
        bool active = _currencyActivity.TryGetValue(currencyCode, out bool registered)
            ? registered
            : DefaultCurrencyActive;
        return Task.FromResult(active);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<int, AccountReference>> GetAccountReferencesAsync(
        IReadOnlyCollection<int> accountIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountIds);

        Dictionary<int, AccountReference> resolved = new(accountIds.Count);
        foreach (int accountId in accountIds.Distinct())
        {
            if (_references.TryGetValue(accountId, out AccountReference? reference))
            {
                resolved[accountId] = reference;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<int, AccountReference>>(resolved);
    }

    /// <inheritdoc />
    public Task<int?> ResolveAccountIdByCodeAsync(string accountCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accountCode))
        {
            return Task.FromResult<int?>(null);
        }

        if (_codeToId.TryGetValue(accountCode, out int? mapped))
        {
            return Task.FromResult(mapped);
        }

        return Task.FromResult<int?>(null);
    }
}
