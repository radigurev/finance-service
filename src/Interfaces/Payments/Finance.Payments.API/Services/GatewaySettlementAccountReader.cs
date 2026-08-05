using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Payments.API.Interfaces;
using Finance.ServiceModel.Accounts;
using Microsoft.Extensions.Logging;
using Refit;

namespace Finance.Payments.API.Services;

/// <summary>
/// Default <see cref="ISettlementAccountReader"/> that asserts the cash/bank GL account through the Accounts
/// service's <c>GET /api/v1/accounts/{id}</c> via the Finance Gateway (SDD-PAY-001 §2.8; SDD-ACCT-001). Never a
/// cross-database join and never a foreign key.
/// <para>Resolution: <c>404</c> ⇒ <c>PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND</c>; found but inactive ⇒
/// <c>PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE</c>; any other upstream failure ⇒ <b>fail closed</b> with
/// <c>PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND</c>, matching the shipped <c>GatewayReferenceDataReader</c>
/// convention. <see cref="OperationCanceledException"/> is rethrown.</para>
/// </summary>
public sealed class GatewaySettlementAccountReader : ISettlementAccountReader
{
    private readonly IAccountReadClient _accounts;
    private readonly ILogger<GatewaySettlementAccountReader> _logger;

    /// <summary>Creates a new <see cref="GatewaySettlementAccountReader"/>.</summary>
    /// <param name="accounts">The Refit Accounts read client (through the gateway).</param>
    /// <param name="logger">Structured logger for upstream-read diagnostics.</param>
    public GatewaySettlementAccountReader(
        IAccountReadClient accounts,
        ILogger<GatewaySettlementAccountReader> logger)
    {
        _accounts = accounts;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> EnsureUsableAsync(int settlementAccountId, CancellationToken cancellationToken)
    {
        try
        {
            AccountDto account = await _accounts
                .GetAccountAsync(settlementAccountId, cancellationToken)
                .ConfigureAwait(false);

            if (!account.IsActive)
            {
                return Result.Failure(
                    PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE,
                    "The settlement account exists but is not active.");
            }

            if (!IsPostable(account))
            {
                return Result.Failure(
                    PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE,
                    "The settlement account is not postable.");
            }

            return Result.Success();
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "Settlement account {SettlementAccountId} was not found; blocked.",
                settlementAccountId);
            return Result.Failure(
                PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND,
                "The settlement account does not exist.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Settlement-account read failed for {SettlementAccountId}; failing closed.",
                settlementAccountId);
            return Result.Failure(
                PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND,
                "The Accounts service could not confirm the settlement account.");
        }
    }

    /// <inheritdoc />
    public bool IsPostable(AccountDto account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return true;
    }
}
