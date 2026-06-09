using Finance.Common.Enums;
using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Periods;

namespace Finance.Journal.API.Tests.Integration.TestDoubles;

/// <summary>
/// In-memory <see cref="IPeriodReadClient"/> test double that stands in for the gateway-backed Refit
/// period client (which the <c>GatewayPostingPeriodGuard</c> calls through the non-running Finance
/// Gateway and which therefore fails closed). It returns a synthetic <see cref="FiscalPeriodDto"/> whose
/// <see cref="FiscalPeriodStatus"/> is controlled by <see cref="Status"/>, so a test can post into an
/// <c>Open</c> period (the default) or assert <c>POSTING_PERIOD_CLOSED</c> by switching to <c>Closed</c>.
/// </summary>
public sealed class FakePeriodReadClient : IPeriodReadClient
{
    /// <summary>The status the fake period is reported with; defaults to <see cref="FiscalPeriodStatus.Open"/>.</summary>
    public FiscalPeriodStatus Status { get; set; } = FiscalPeriodStatus.Open;

    /// <inheritdoc />
    public Task<FiscalPeriodDto> GetByDateAsync(DateTimeOffset date, CancellationToken cancellationToken)
    {
        FiscalPeriodDto period = new()
        {
            Id = 1,
            FiscalYear = date.Year,
            PeriodNumber = date.Month,
            Name = $"{date:MMMM yyyy}",
            StartDate = new DateTimeOffset(date.Year, date.Month, 1, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(date.Year, date.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1).AddTicks(-1),
            Status = Status,
            ClosedAt = Status == FiscalPeriodStatus.Closed ? DateTimeOffset.UtcNow : null,
            ReopenedAt = null,
            RowVersion = Convert.ToBase64String(BitConverter.GetBytes(1L))
        };

        return Task.FromResult(period);
    }
}
