using System.Net;
using Finance.Common.Enums;
using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Periods;
using Refit;

namespace Finance.Journal.API.Tests.Fixtures;

/// <summary>
/// In-memory <see cref="IPeriodReadClient"/> substitute used by the Journal unit tests in place of the
/// gateway-backed Refit client, so the <c>GatewayPostingPeriodGuard</c> period checks (SDD-FIN-004 §2.7)
/// run without HTTP. By default the date resolves to an <c>Open</c> period; tests opt into a closed period,
/// a missing period (<c>404</c>), or an unreachable service via the configuration methods.
/// </summary>
public sealed class FakePeriodReadClient : IPeriodReadClient
{
    private FiscalPeriodStatus _status = FiscalPeriodStatus.Open;
    private bool _noPeriodForDate;
    private bool _unreachable;

    /// <summary>Configures the next lookup to resolve to an <c>Open</c> period (the default).</summary>
    public void ReturnsOpenPeriod()
    {
        _status = FiscalPeriodStatus.Open;
        _noPeriodForDate = false;
        _unreachable = false;
    }

    /// <summary>Configures the next lookup to resolve to a <c>Closed</c> period.</summary>
    public void ReturnsClosedPeriod()
    {
        _status = FiscalPeriodStatus.Closed;
        _noPeriodForDate = false;
        _unreachable = false;
    }

    /// <summary>Configures the next lookup to return a <c>404</c> (no period covers the date).</summary>
    public void ReturnsNoPeriodForDate()
    {
        _noPeriodForDate = true;
        _unreachable = false;
    }

    /// <summary>Configures the next lookup to fail as if the Periods service were unreachable.</summary>
    public void ThrowsServiceUnreachable()
    {
        _unreachable = true;
        _noPeriodForDate = false;
    }

    /// <inheritdoc />
    public async Task<FiscalPeriodDto> GetByDateAsync(DateTimeOffset date, CancellationToken cancellationToken)
    {
        if (_unreachable)
        {
            throw new HttpRequestException("Periods service unreachable (test fixture).");
        }

        if (_noPeriodForDate)
        {
            throw await BuildNotFoundAsync().ConfigureAwait(false);
        }

        return new FiscalPeriodDto
        {
            Id = 1,
            FiscalYear = date.Year,
            PeriodNumber = date.Month,
            Name = "Test Period",
            StartDate = date.AddDays(-1),
            EndDate = date.AddDays(1),
            Status = _status,
            RowVersion = Convert.ToBase64String([1, 2, 3, 4])
        };
    }

    private static async Task<ApiException> BuildNotFoundAsync()
    {
        using HttpResponseMessage response = new(HttpStatusCode.NotFound);
        return await ApiException.Create(
            new HttpRequestMessage(HttpMethod.Get, "/api/v1/periods/by-date"),
            HttpMethod.Get,
            response,
            new RefitSettings()).ConfigureAwait(false);
    }
}
