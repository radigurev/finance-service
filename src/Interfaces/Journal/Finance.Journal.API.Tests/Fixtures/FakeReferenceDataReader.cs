using Finance.Journal.API.Interfaces;

namespace Finance.Journal.API.Tests.Fixtures;

/// <summary>
/// In-memory <see cref="IReferenceDataReader"/> substitute used by the Journal unit tests in place of the
/// gateway-backed reader, so account-postability and currency-validity checks (SDD-FIN-001 §2.6, §2.7) run
/// without HTTP. By default every account is postable and every currency active; tests opt specific ids or
/// codes out via <see cref="MarkAccountNotPostable"/> / <see cref="MarkCurrencyInactive"/>.
/// </summary>
public sealed class FakeReferenceDataReader : IReferenceDataReader
{
    private readonly HashSet<int> _notPostableAccounts = [];
    private readonly HashSet<string> _inactiveCurrencies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Marks an account id as missing/inactive so it fails the postability check.</summary>
    /// <param name="accountId">The account id to treat as not postable.</param>
    public void MarkAccountNotPostable(int accountId)
    {
        _notPostableAccounts.Add(accountId);
    }

    /// <summary>Marks a currency code as missing/inactive so it fails the currency check.</summary>
    /// <param name="currencyCode">The currency code to treat as inactive.</param>
    public void MarkCurrencyInactive(string currencyCode)
    {
        _inactiveCurrencies.Add(currencyCode);
    }

    /// <inheritdoc />
    public Task<bool> IsAccountPostableAsync(int accountId, CancellationToken cancellationToken)
    {
        return Task.FromResult(!_notPostableAccounts.Contains(accountId));
    }

    /// <inheritdoc />
    public Task<bool> IsCurrencyActiveAsync(string currencyCode, CancellationToken cancellationToken)
    {
        return Task.FromResult(!_inactiveCurrencies.Contains(currencyCode));
    }
}
