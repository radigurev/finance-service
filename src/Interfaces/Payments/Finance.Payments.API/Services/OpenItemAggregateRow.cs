using Finance.Payments.DBModel.Models;

namespace Finance.Payments.API.Services;

/// <summary>
/// The SQL-side shape of one in-scope open item: the projection row itself plus its settled amount resolved AS OF
/// the requested date (SDD-PAY-003 §2.3). Both members are produced by ONE server-evaluated projection, so the
/// as-of settled amount is never computed by a follow-up round trip and never by a per-item query.
/// <para>For the current day <see cref="SettledAsOfDate"/> is the maintained projection column; for an earlier day
/// it is a correlated <c>SUM</c> over the invoice's surviving allocation rows restricted to allocations recorded on
/// or before the as-of date whose owning payment is <c>Confirmed</c> or <c>Posted</c>.</para>
/// </summary>
public sealed class OpenItemAggregateRow
{
    /// <summary>The local open-item projection row supplying every externally-owned column.</summary>
    public required InvoiceOpenItem Item { get; init; }

    /// <summary>The settled amount in force at the as-of date; the subtrahend of the outstanding formula.</summary>
    public required decimal SettledAsOfDate { get; init; }
}
