using Finance.Common.Enums;

namespace Finance.ServiceModel.Periods;

/// <summary>
/// Representation of a fiscal period exposed by the API (SDD-FIN-004 §2). The natural key is
/// <c>(FiscalYear, PeriodNumber)</c>; <see cref="Id"/> is an internal surrogate.
/// </summary>
public sealed record FiscalPeriodDto
{
    /// <summary>Surrogate identifier of the period.</summary>
    public required int Id { get; init; }

    /// <summary>The accounting year the period belongs to (e.g. 2026).</summary>
    public required int FiscalYear { get; init; }

    /// <summary>The 1-based period ordinal within the fiscal year (1–12 for calendar months).</summary>
    public required int PeriodNumber { get; init; }

    /// <summary>Human-readable period name (e.g. "January 2026").</summary>
    public required string Name { get; init; }

    /// <summary>The first instant of the period (inclusive).</summary>
    public required DateTimeOffset StartDate { get; init; }

    /// <summary>The last instant of the period (inclusive).</summary>
    public required DateTimeOffset EndDate { get; init; }

    /// <summary>The lifecycle state: <c>Open</c> or <c>Closed</c>.</summary>
    public required FiscalPeriodStatus Status { get; init; }

    /// <summary>The closing timestamp; <c>null</c> while the period is <c>Open</c>.</summary>
    public DateTimeOffset? ClosedAt { get; init; }

    /// <summary>The most recent reopen timestamp; <c>null</c> when the period was never reopened.</summary>
    public DateTimeOffset? ReopenedAt { get; init; }

    /// <summary>
    /// Base64-encoded SQL Server <c>rowversion</c> optimistic-concurrency token. Clients round-trip this
    /// value back on close / reopen so a stale write is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
