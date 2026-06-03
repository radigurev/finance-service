using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Periods;
using Microsoft.Extensions.Logging;
using Refit;

namespace Finance.Journal.API.Workflow;

/// <summary>
/// Real <see cref="IPostingPeriodGuard"/> that fulfills the SDD-FIN-002 §2.7 seam by reading period status
/// from the Periods service's <c>GET /api/v1/periods/by-date</c> through the Finance Gateway via Refit
/// (SDD-FIN-004 §2.7). Resolution table: period <c>Open</c> → allow; period <c>Closed</c>,
/// <c>NO_PERIOD_FOR_DATE</c> (404), or any upstream error / unreachable Periods service →
/// <c>POSTING_PERIOD_CLOSED</c>. The guard <b>fails closed</b> (blocks posting on uncertainty), mirroring
/// the Batch-10 <c>GatewayReferenceDataReader</c> convention — financial safety over availability.
/// </summary>
public sealed class GatewayPostingPeriodGuard : IPostingPeriodGuard
{
    private readonly IPeriodReadClient _periods;
    private readonly ILogger<GatewayPostingPeriodGuard> _logger;

    /// <summary>Creates a new <see cref="GatewayPostingPeriodGuard"/>.</summary>
    /// <param name="periods">The Refit Periods read client (through the gateway).</param>
    /// <param name="logger">Structured logger for period-lookup diagnostics.</param>
    public GatewayPostingPeriodGuard(IPeriodReadClient periods, ILogger<GatewayPostingPeriodGuard> logger)
    {
        _periods = periods;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> EnsurePostableAsync(DateTimeOffset entryDate, CancellationToken cancellationToken)
    {
        try
        {
            FiscalPeriodDto period = await _periods.GetByDateAsync(entryDate, cancellationToken).ConfigureAwait(false);
            if (period.Status == FiscalPeriodStatus.Open)
            {
                return Result.Success();
            }

            return Result.Failure(
                JournalErrorCodes.POSTING_PERIOD_CLOSED,
                "The fiscal period for the entry date is closed.");
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "No fiscal period covers entry date {EntryDate}; posting blocked (POSTING_PERIOD_CLOSED).",
                entryDate);
            return Result.Failure(
                JournalErrorCodes.POSTING_PERIOD_CLOSED,
                "No fiscal period is defined for the entry date.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Period lookup failed for entry date {EntryDate}; failing closed (POSTING_PERIOD_CLOSED).",
                entryDate);
            return Result.Failure(
                JournalErrorCodes.POSTING_PERIOD_CLOSED,
                "The fiscal-period service could not confirm the period is open.");
        }
    }
}
