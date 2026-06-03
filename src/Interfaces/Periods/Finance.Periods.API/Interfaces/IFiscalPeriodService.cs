using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.ServiceModel.Periods;

namespace Finance.Periods.API.Interfaces;

/// <summary>
/// Application service for the fiscal-period lifecycle (SDD-FIN-004). Every method returns a
/// <see cref="Result"/> / <see cref="Result{T}"/> — never <c>null</c>, never a thrown exception for a
/// business outcome (SDD-INFRA-009). State transitions go through <c>IWorkflowEngine&lt;FiscalPeriod&gt;</c>.
/// </summary>
public interface IFiscalPeriodService
{
    /// <summary>Lists fiscal periods as a filtered, sorted, and paged envelope (SDD-FIN-004 §2.11).</summary>
    /// <param name="request">The filter, sort, and pagination request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A page of <see cref="FiscalPeriodDto"/>, or a filter-validation failure.</returns>
    Task<Result<PagedResult<FiscalPeriodDto>>> SearchAsync(FilterRequest request, CancellationToken cancellationToken);

    /// <summary>Returns a single fiscal period by surrogate id (SDD-FIN-004 §2.11).</summary>
    /// <param name="id">The period identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The matching period, or <c>PERIOD_NOT_FOUND</c>.</returns>
    Task<Result<FiscalPeriodDto>> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>Returns the period whose inclusive date bounds contain the supplied date (SDD-FIN-004 §2.6).</summary>
    /// <param name="date">The date to resolve to a period.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The containing period, or <c>NO_PERIOD_FOR_DATE</c> when none covers the date.</returns>
    Task<Result<FiscalPeriodDto>> GetByDateAsync(DateTimeOffset date, CancellationToken cancellationToken);

    /// <summary>Returns the period identified by its natural key (SDD-FIN-004 §2.6).</summary>
    /// <param name="fiscalYear">The accounting year.</param>
    /// <param name="periodNumber">The 1-based period ordinal.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The matching period, or <c>PERIOD_NOT_FOUND</c>.</returns>
    Task<Result<FiscalPeriodDto>> GetByYearNumberAsync(int fiscalYear, int periodNumber, CancellationToken cancellationToken);

    /// <summary>Generates the full set of periods for a fiscal year (SDD-FIN-004 §2.2).</summary>
    /// <param name="request">The generation request carrying the fiscal year.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The generated periods, or a duplicate / overlap failure.</returns>
    Task<Result<IReadOnlyList<FiscalPeriodDto>>> GenerateAsync(GeneratePeriodsRequest request, CancellationToken cancellationToken);

    /// <summary>Creates a single fiscal period explicitly (SDD-FIN-004 §2.3).</summary>
    /// <param name="request">The create request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The created period, or a duplicate / overlap / validation failure.</returns>
    Task<Result<FiscalPeriodDto>> CreateAsync(CreatePeriodRequest request, CancellationToken cancellationToken);

    /// <summary>Closes an open fiscal period (Open → Closed) (SDD-FIN-004 §2.4).</summary>
    /// <param name="id">The period identifier.</param>
    /// <param name="request">The close request carrying the mandatory reason and row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The closed period, or a state / ordering / concurrency failure.</returns>
    Task<Result<FiscalPeriodDto>> CloseAsync(int id, ClosePeriodRequest request, CancellationToken cancellationToken);

    /// <summary>Reopens a closed fiscal period (Closed → Open) (SDD-FIN-004 §2.5).</summary>
    /// <param name="id">The period identifier.</param>
    /// <param name="request">The reopen request carrying the mandatory reason and row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The reopened period, or a state / ordering / concurrency failure.</returns>
    Task<Result<FiscalPeriodDto>> ReopenAsync(int id, ReopenPeriodRequest request, CancellationToken cancellationToken);
}
