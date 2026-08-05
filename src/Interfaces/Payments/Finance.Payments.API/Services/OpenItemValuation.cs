namespace Finance.Payments.API.Services;

/// <summary>
/// One in-scope open item with its report arithmetic applied (SDD-PAY-003 §2.2, §2.4): the outstanding amount, its
/// base-currency counterpart at the FROZEN booking rate, the days past due on date parts only, and the index of the
/// single bucket it belongs to.
/// <para>This is the unit the three endpoints share. <c>/open-items</c> renders one valuation per row, while
/// <c>/aging</c> and <c>/counterparty-balances</c> group the same valuations — which is what makes the two reports
/// structurally incapable of disagreeing on a total.</para>
/// </summary>
public sealed record OpenItemValuation
{
    /// <summary>The underlying projection row and its as-of settled amount.</summary>
    public required OpenItemAggregateRow Row { get; init; }

    /// <summary>The transactional outstanding amount; always strictly greater than <c>0.00</c>.</summary>
    public required decimal Outstanding { get; init; }

    /// <summary>The country-rounded base-currency counterpart of <see cref="Outstanding"/>.</summary>
    public required decimal BaseOutstanding { get; init; }

    /// <summary>The whole days from the due date to the as-of date, computed on date parts only.</summary>
    public required int DaysPastDue { get; init; }

    /// <summary>The zero-based index of the single bucket this item falls into.</summary>
    public required int BucketIndex { get; init; }

    /// <summary>Whether the item is at least one day past due — i.e. in any bucket other than <c>Current</c>.</summary>
    public bool IsOverdue => DaysPastDue >= 1;
}
