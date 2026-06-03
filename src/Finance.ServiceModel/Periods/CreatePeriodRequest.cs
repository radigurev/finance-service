namespace Finance.ServiceModel.Periods;

/// <summary>
/// Request body for creating a single fiscal period explicitly (SDD-FIN-004 §2.3). The created period is
/// <c>Open</c> and must not overlap an existing period or duplicate its <c>(FiscalYear, PeriodNumber)</c>.
/// </summary>
public sealed record CreatePeriodRequest
{
    /// <summary>The accounting year the period belongs to.</summary>
    public required int FiscalYear { get; init; }

    /// <summary>The 1-based period ordinal within the fiscal year (1–12 for calendar months).</summary>
    public required int PeriodNumber { get; init; }

    /// <summary>Optional human-readable period name; a calendar-month default is derived when omitted.</summary>
    public string? Name { get; init; }

    /// <summary>The first instant of the period (inclusive).</summary>
    public required DateTimeOffset StartDate { get; init; }

    /// <summary>The last instant of the period (inclusive).</summary>
    public required DateTimeOffset EndDate { get; init; }
}
