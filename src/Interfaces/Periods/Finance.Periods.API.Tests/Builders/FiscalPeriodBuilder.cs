using Finance.Common.Enums;
using Finance.Periods.DBModel.Models;

namespace Finance.Periods.API.Tests.Builders;

/// <summary>
/// Builds <see cref="FiscalPeriod"/> entities for seeding the SQLite-backed Periods tests (SDD-FIN-004 §6).
/// Defaults to a valid <c>Open</c> calendar-month period; tests override only the facets they exercise.
/// </summary>
public sealed class FiscalPeriodBuilder
{
    private int _fiscalYear = 2026;
    private int _periodNumber = 1;
    private string _name = "January 2026";
    private DateTimeOffset _startDate = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private DateTimeOffset _endDate = new(2026, 1, 31, 23, 59, 59, TimeSpan.Zero);
    private FiscalPeriodStatus _status = FiscalPeriodStatus.Open;

    /// <summary>Creates a new builder seeded with valid defaults.</summary>
    /// <returns>A new <see cref="FiscalPeriodBuilder"/>.</returns>
    public static FiscalPeriodBuilder Create() => new();

    /// <summary>Sets the fiscal year.</summary>
    /// <param name="fiscalYear">The accounting year.</param>
    /// <returns>This builder.</returns>
    public FiscalPeriodBuilder WithFiscalYear(int fiscalYear)
    {
        _fiscalYear = fiscalYear;
        return this;
    }

    /// <summary>Sets the period ordinal.</summary>
    /// <param name="periodNumber">The 1-based period ordinal.</param>
    /// <returns>This builder.</returns>
    public FiscalPeriodBuilder WithPeriodNumber(int periodNumber)
    {
        _periodNumber = periodNumber;
        return this;
    }

    /// <summary>Sets the human-readable name.</summary>
    /// <param name="name">The period name.</param>
    /// <returns>This builder.</returns>
    public FiscalPeriodBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>Sets the inclusive date bounds.</summary>
    /// <param name="startDate">The first instant of the period.</param>
    /// <param name="endDate">The last instant of the period.</param>
    /// <returns>This builder.</returns>
    public FiscalPeriodBuilder WithDates(DateTimeOffset startDate, DateTimeOffset endDate)
    {
        _startDate = startDate;
        _endDate = endDate;
        return this;
    }

    /// <summary>Sets the lifecycle status.</summary>
    /// <param name="status">The status (<c>Open</c> or <c>Closed</c>).</param>
    /// <returns>This builder.</returns>
    public FiscalPeriodBuilder WithStatus(FiscalPeriodStatus status)
    {
        _status = status;
        return this;
    }

    /// <summary>Materializes the configured <see cref="FiscalPeriod"/> entity.</summary>
    /// <returns>The built period.</returns>
    public FiscalPeriod Build() => new()
    {
        FiscalYear = _fiscalYear,
        PeriodNumber = _periodNumber,
        Name = _name,
        StartDate = _startDate,
        EndDate = _endDate,
        Status = _status,
        CorrelationId = StubCorrelationIdAccessorCorrelation,
        CreatedAt = DateTimeOffset.UtcNow,
        CreatedBy = Guid.NewGuid()
    };

    private const string StubCorrelationIdAccessorCorrelation = "test-correlation-id";
}
