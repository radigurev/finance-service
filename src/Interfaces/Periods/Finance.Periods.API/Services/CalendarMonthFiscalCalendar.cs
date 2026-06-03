using System.Globalization;
using Finance.Periods.API.Interfaces;
using Finance.Periods.API.Models;

namespace Finance.Periods.API.Services;

/// <summary>
/// Default <see cref="IFiscalCalendar"/> that derives 12 calendar-aligned monthly periods for a fiscal year
/// (SDD-FIN-004 §2.2). Each period spans the whole calendar month in UTC: <c>StartDate</c> is the first
/// instant of the month and <c>EndDate</c> is the last representable instant of the month, so consecutive
/// periods are contiguous and non-overlapping. SDD-CTRY-001 may replace this with a non-calendar calendar.
/// </summary>
public sealed class CalendarMonthFiscalCalendar : IFiscalCalendar
{
    private const int MonthsPerYear = 12;

    /// <inheritdoc />
    public IReadOnlyList<PeriodDescriptor> GeneratePeriods(int fiscalYear)
    {
        List<PeriodDescriptor> descriptors = new(MonthsPerYear);

        for (int month = 1; month <= MonthsPerYear; month++)
        {
            DateTimeOffset start = new(fiscalYear, month, 1, 0, 0, 0, TimeSpan.Zero);
            int daysInMonth = DateTime.DaysInMonth(fiscalYear, month);
            DateTimeOffset monthEnd = new(fiscalYear, month, daysInMonth, 23, 59, 59, TimeSpan.Zero);
            DateTimeOffset end = monthEnd.AddTicks(TimeSpan.TicksPerSecond - 1);

            descriptors.Add(new PeriodDescriptor(month, BuildName(fiscalYear, month), start, end));
        }

        return descriptors;
    }

    private static string BuildName(int fiscalYear, int month)
    {
        string monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);
        return string.Create(CultureInfo.InvariantCulture, $"{monthName} {fiscalYear}");
    }
}
