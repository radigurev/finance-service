namespace Finance.Periods.API.Models;

/// <summary>
/// A derived period blueprint produced by <see cref="Interfaces.IFiscalCalendar"/> before a
/// <see cref="DBModel.Models.FiscalPeriod"/> is materialized (SDD-FIN-004 §2.2). Carries the natural-key
/// ordinal, name, and inclusive date bounds for one period of a fiscal year.
/// </summary>
/// <param name="PeriodNumber">The 1-based period ordinal within the fiscal year.</param>
/// <param name="Name">The human-readable period name (e.g. "January 2026").</param>
/// <param name="StartDate">The first instant of the period (inclusive).</param>
/// <param name="EndDate">The last instant of the period (inclusive).</param>
public sealed record PeriodDescriptor(int PeriodNumber, string Name, DateTimeOffset StartDate, DateTimeOffset EndDate);
