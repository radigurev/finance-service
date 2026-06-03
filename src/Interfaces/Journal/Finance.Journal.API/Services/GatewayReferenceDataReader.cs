using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Accounts;
using Finance.ServiceModel.Nomenclature;
using Microsoft.Extensions.Logging;
using Refit;

namespace Finance.Journal.API.Services;

/// <summary>
/// Default <see cref="IReferenceDataReader"/> that reads account and currency state from the Accounts
/// and Nomenclature services through the Finance Gateway via Refit (SDD-FIN-001 §2.6, §2.7; resolved §7).
/// A <c>404</c> response (account/currency not found) resolves to not-postable / not-valid; any other
/// upstream failure is logged and treated as not-postable / not-valid so a line never posts against an
/// unverified reference.
/// <para><b>Postability scope (deviation):</b> postability is currently asserted as <c>exists AND active</c>.
/// The "non-header / leaf" refinement (SDD-FIN-001 §2.6) cannot be determined through the existing
/// Accounts read endpoints — <see cref="AccountDto"/> exposes neither an <c>IsHeader</c> flag nor a
/// child count — so the leaf check is deferred pending a <c>CHG-ENH</c> against SDD-ACCT-001.</para>
/// </summary>
public sealed class GatewayReferenceDataReader : IReferenceDataReader
{
    private readonly IAccountReadClient _accounts;
    private readonly ICurrencyReadClient _currencies;
    private readonly ILogger<GatewayReferenceDataReader> _logger;

    /// <summary>Creates a new <see cref="GatewayReferenceDataReader"/>.</summary>
    /// <param name="accounts">The Refit Accounts read client (through the gateway).</param>
    /// <param name="currencies">The Refit Currencies read client (through the gateway).</param>
    /// <param name="logger">Structured logger for upstream-read diagnostics.</param>
    public GatewayReferenceDataReader(
        IAccountReadClient accounts,
        ICurrencyReadClient currencies,
        ILogger<GatewayReferenceDataReader> logger)
    {
        _accounts = accounts;
        _currencies = currencies;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> IsAccountPostableAsync(int accountId, CancellationToken cancellationToken)
    {
        try
        {
            AccountDto account = await _accounts.GetAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
            return account.IsActive;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(
                ex,
                "Account postability read failed for account {AccountId}; treating as not postable.",
                accountId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsCurrencyActiveAsync(string currencyCode, CancellationToken cancellationToken)
    {
        try
        {
            CurrencyDto currency = await _currencies.GetCurrencyAsync(currencyCode, cancellationToken).ConfigureAwait(false);
            return currency.IsActive;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(
                ex,
                "Currency validity read failed for currency {CurrencyCode}; treating as not valid.",
                currencyCode);
            return false;
        }
    }
}
