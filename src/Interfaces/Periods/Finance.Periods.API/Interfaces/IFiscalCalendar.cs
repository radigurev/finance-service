using Finance.Periods.API.Models;

namespace Finance.Periods.API.Interfaces;

/// <summary>
/// Country-strategy seam for deriving the set of fiscal periods for a year (SDD-FIN-004 §2.2, §7). v1 ships
/// <c>CalendarMonthFiscalCalendar</c> (12 calendar-aligned monthly periods); SDD-CTRY-001 can substitute a
/// non-calendar fiscal-year start or a 13th/adjustment period without changing the generation endpoint.
/// The calendar derivation MUST NOT be hard-coded into the service method.
/// </summary>
public interface IFiscalCalendar
{
    /// <summary>
    /// Derives the contiguous, non-overlapping periods that compose the supplied fiscal year. Each
    /// returned descriptor carries the period number, name, and inclusive <c>[StartDate, EndDate]</c> bounds.
    /// </summary>
    /// <param name="fiscalYear">The accounting year to derive periods for.</param>
    /// <returns>The ordered period descriptors for the year.</returns>
    IReadOnlyList<PeriodDescriptor> GeneratePeriods(int fiscalYear);
}
