using Finance.Common.Enums;
using Finance.GenericFiltering.Attributes;

namespace Finance.Periods.DBModel.Models;

/// <summary>
/// The fiscal-period aggregate root: an accounting-calendar window into which transactions are recorded
/// (SDD-FIN-004 §2). Its natural key is <c>(FiscalYear, PeriodNumber)</c>; the surrogate <see cref="Id"/>
/// is internal-only (INT IDENTITY). Its lifecycle (<c>Open ⇄ Closed</c>) is owned by SDD-FIN-004 §2.1.
/// </summary>
public sealed class FiscalPeriod
{
    /// <summary>Internal surrogate identifier (INT IDENTITY, not externally referenced).</summary>
    public int Id { get; set; }

    /// <summary>The accounting year the period belongs to (e.g. 2026).</summary>
    [Filterable]
    [Sortable]
    public int FiscalYear { get; set; }

    /// <summary>The 1-based period ordinal within the fiscal year (1–12 for calendar months).</summary>
    [Filterable]
    [Sortable]
    public int PeriodNumber { get; set; }

    /// <summary>Human-readable period name (e.g. "January 2026").</summary>
    public required string Name { get; set; }

    /// <summary>The first instant of the period (inclusive).</summary>
    [Filterable]
    [Sortable]
    public DateTimeOffset StartDate { get; set; }

    /// <summary>The last instant of the period (inclusive).</summary>
    [Filterable]
    [Sortable]
    public DateTimeOffset EndDate { get; set; }

    /// <summary>The lifecycle state: <c>Open</c> or <c>Closed</c>.</summary>
    [Filterable]
    [Sortable]
    public FiscalPeriodStatus Status { get; set; } = FiscalPeriodStatus.Open;

    /// <summary>The closing timestamp; <c>null</c> while <c>Open</c>.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>The identifier of the user who closed the period; <c>null</c> while <c>Open</c>.</summary>
    public Guid? ClosedBy { get; set; }

    /// <summary>The most recent reopen timestamp; <c>null</c> when the period was never reopened.</summary>
    public DateTimeOffset? ReopenedAt { get; set; }

    /// <summary>The identifier of the user who last reopened the period; <c>null</c> when never reopened.</summary>
    public Guid? ReopenedBy { get; set; }

    /// <summary>The ambient correlation identifier captured at creation.</summary>
    public required string CorrelationId { get; set; }

    /// <summary>UTC-offset creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The identifier of the user who created the period.</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>SQL Server <c>rowversion</c> optimistic-concurrency token (SDD-INFRA-008/009).</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The append-only state-transition history for the period.</summary>
    public ICollection<FiscalPeriodStatusHistory> StatusHistory { get; set; } = new List<FiscalPeriodStatusHistory>();
}
