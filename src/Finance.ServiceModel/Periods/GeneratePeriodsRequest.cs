namespace Finance.ServiceModel.Periods;

/// <summary>
/// Request body for generating the full set of fiscal periods for a year (SDD-FIN-004 §2.2). v1 generates
/// 12 calendar-aligned monthly periods, all <c>Open</c>.
/// </summary>
public sealed record GeneratePeriodsRequest
{
    /// <summary>The accounting year to generate periods for (e.g. 2026).</summary>
    public required int FiscalYear { get; init; }
}
