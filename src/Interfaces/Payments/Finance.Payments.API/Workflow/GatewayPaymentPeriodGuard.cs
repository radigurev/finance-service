using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Payments.API.Interfaces;
using Finance.ServiceModel.Periods;
using Microsoft.Extensions.Logging;
using Refit;

namespace Finance.Payments.API.Workflow;

/// <summary>
/// The production <see cref="IPaymentPeriodGuard"/> (SDD-PAY-001 §2.9). It reads period status from the Periods
/// service's <c>GET /api/v1/periods/by-date</c> through the Finance Gateway via Refit — the SAME shipped lookup
/// SDD-FIN-004 §2.7 built to fulfil the SDD-FIN-002 seam. Gateway-backed from day one: this service
/// deliberately does NOT repeat the Invoices service's always-open production registration.
/// <para>Resolution table: period <c>Open</c> → allow; period <c>Closed</c>, <c>NO_PERIOD_FOR_DATE</c> (404),
/// or any upstream error / unreachable Periods service → <c>PAYMENT_PERIOD_CLOSED</c>. The guard <b>fails
/// closed</b> (blocks the operation on uncertainty) — financial safety over availability.
/// <see cref="OperationCanceledException"/> is rethrown.</para>
/// </summary>
public sealed class GatewayPaymentPeriodGuard : IPaymentPeriodGuard
{
    private readonly IPeriodReadClient _periods;
    private readonly ILogger<GatewayPaymentPeriodGuard> _logger;

    /// <summary>Creates a new <see cref="GatewayPaymentPeriodGuard"/>.</summary>
    /// <param name="periods">The Refit Periods read client (through the gateway).</param>
    /// <param name="logger">Structured logger for period-lookup diagnostics.</param>
    public GatewayPaymentPeriodGuard(IPeriodReadClient periods, ILogger<GatewayPaymentPeriodGuard> logger)
    {
        _periods = periods;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> EnsureOpenAsync(DateTimeOffset paymentDate, CancellationToken cancellationToken)
    {
        try
        {
            FiscalPeriodDto period =
                await _periods.GetByDateAsync(paymentDate, cancellationToken).ConfigureAwait(false);
            if (period.Status == FiscalPeriodStatus.Open)
            {
                return Result.Success();
            }

            return Result.Failure(
                PaymentErrorCodes.PAYMENT_PERIOD_CLOSED,
                "The fiscal period for the payment date is closed.");
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "No fiscal period covers payment date {PaymentDate}; blocked (PAYMENT_PERIOD_CLOSED).",
                paymentDate);
            return Result.Failure(
                PaymentErrorCodes.PAYMENT_PERIOD_CLOSED,
                "No fiscal period is defined for the payment date.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Period lookup failed for payment date {PaymentDate}; failing closed (PAYMENT_PERIOD_CLOSED).",
                paymentDate);
            return Result.Failure(
                PaymentErrorCodes.PAYMENT_PERIOD_CLOSED,
                "The fiscal-period service could not confirm the period is open.");
        }
    }
}
