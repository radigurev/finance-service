namespace Finance.Payments.API.Services;

/// <summary>
/// The validated, normalized narrowing of ONE aging read (SDD-PAY-003 §2.3, §2.5, §2.6, §2.7). It is the single
/// parameter object every query-building step takes, so no method threads a handful of loose primitives.
/// <para>The derived members express the as-of date as UTC DAY boundaries so the SQL predicates and the in-memory
/// days-past-due arithmetic agree exactly: an item is in scope while its issue instant is before
/// <see cref="DayEnd"/> (i.e. its issue DATE is on or before the as-of date), and it is overdue while its due
/// instant is before <see cref="DayStart"/> (i.e. its due DATE is strictly before the as-of date).</para>
/// <para><see cref="IsHistorical"/> picks the settled-amount path and is driven SOLELY by the date: the current
/// day reads the maintained projection column, an earlier day replays the surviving allocation rows.</para>
/// </summary>
public sealed record AgingScope
{
    /// <summary>The effective as-of date; never in the future.</summary>
    public required DateTimeOffset AsOfDate { get; init; }

    /// <summary>The clock's current UTC day, used only to choose the settled-amount path.</summary>
    public required DateOnly Today { get; init; }

    /// <summary>The canonical direction name (<c>AR</c>/<c>AP</c>), or <c>null</c> when the read is not narrowed.</summary>
    public string? Direction { get; init; }

    /// <summary>The counterparty narrowing, or <c>null</c> when the read is not narrowed.</summary>
    public Guid? CounterpartyId { get; init; }

    /// <summary>The transactional currency narrowing, or <c>null</c> when the read is not narrowed.</summary>
    public string? CurrencyCode { get; init; }

    /// <summary>Whether only items at least one day past due are in scope.</summary>
    public bool OverdueOnly { get; init; }

    /// <summary>The UTC instant the as-of day starts at — the exclusive upper bound of "overdue".</summary>
    public DateTimeOffset DayStart => new(DateOnly.FromDateTime(AsOfDate.UtcDateTime), TimeOnly.MinValue, TimeSpan.Zero);

    /// <summary>The UTC instant the as-of day ends at — the exclusive upper bound of the accounting view.</summary>
    public DateTimeOffset DayEnd => DayStart.AddDays(1);

    /// <summary>
    /// Whether the as-of date is strictly BEFORE the clock's current day, in which case the maintained projection
    /// column is current-state only and MUST NOT be used.
    /// </summary>
    public bool IsHistorical => DateOnly.FromDateTime(AsOfDate.UtcDateTime) < Today;
}
