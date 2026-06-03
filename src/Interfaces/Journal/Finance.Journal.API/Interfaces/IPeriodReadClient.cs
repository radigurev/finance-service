using Finance.ServiceModel.Periods;
using Refit;

namespace Finance.Journal.API.Interfaces;

/// <summary>
/// Refit contract for the Periods date→status lookup consumed through the Finance Gateway
/// (SDD-FIN-004 §2.6, §2.7). The Journal service owns only <c>finance_journal</c> and MUST NOT
/// cross-database-join into <c>finance_periods</c>; period status is asserted at post time via a
/// synchronous read of <c>GET /api/v1/periods/by-date</c>. Registered with the standard handler chain
/// (<c>CorrelationIdDelegatingHandler</c> → bearer forwarding → resilience).
/// </summary>
public interface IPeriodReadClient
{
    /// <summary>Reads the fiscal period whose inclusive date bounds contain the supplied date.</summary>
    /// <param name="date">The accounting date whose period is being resolved.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The containing period when found; a <c>404</c> API error when no period covers the date.</returns>
    [Get("/api/v1/periods/by-date")]
    Task<FiscalPeriodDto> GetByDateAsync(DateTimeOffset date, CancellationToken cancellationToken);
}
