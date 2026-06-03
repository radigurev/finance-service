using Finance.Common.Results;
using Finance.Journal.API.Interfaces;

namespace Finance.Journal.API.Services;

/// <summary>
/// Test-only fallback <see cref="IPostingPeriodGuard"/> that treats every period as open (SDD-FIN-002 §2.7).
/// Production registers <see cref="Finance.Journal.API.Workflow.GatewayPostingPeriodGuard"/>, which performs
/// the real period-status lookup against the Periods service and returns <c>POSTING_PERIOD_CLOSED</c> for
/// closed/locked periods (SDD-FIN-004). This guard exists only so unit tests can post without standing up
/// the Periods service.
/// </summary>
public sealed class AlwaysOpenPostingPeriodGuard : IPostingPeriodGuard
{
    /// <inheritdoc />
    public Task<Result> EnsurePostableAsync(DateTimeOffset entryDate, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success());
    }
}
