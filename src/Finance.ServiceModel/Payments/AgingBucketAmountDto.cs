namespace Finance.ServiceModel.Payments;

/// <summary>
/// One aging bucket's contribution to a single (counterparty, currency) row (SDD-PAY-003 §2.4, §2.6). It carries
/// its own label AND its numeric boundaries so a client never has to re-derive either to render the report.
/// <para>Buckets are exhaustive and mutually exclusive, so the sum of every bucket's
/// <see cref="Outstanding"/> equals the row's total outstanding to the cent, and the same holds in base
/// currency.</para>
/// </summary>
public sealed record AgingBucketAmountDto
{
    /// <summary>The bucket label (<c>Current</c>, <c>1-30</c>, <c>31-60</c>, <c>61-90</c>, <c>90+</c> by default).</summary>
    public required string Label { get; init; }

    /// <summary>
    /// The inclusive lower days-past-due bound, or <c>null</c> on the open-ended <c>Current</c> bucket, which
    /// admits every not-yet-due item (days past due of <c>0</c> or less).
    /// </summary>
    public int? FromDaysPastDue { get; init; }

    /// <summary>
    /// The inclusive upper days-past-due bound, or <c>null</c> on the open-ended FINAL bucket, which admits
    /// everything strictly beyond the last boundary.
    /// </summary>
    public int? ToDaysPastDue { get; init; }

    /// <summary>The bucket's outstanding total in the row's transactional currency.</summary>
    public required decimal Outstanding { get; init; }

    /// <summary>The bucket's outstanding total converted at each item's frozen booking rate.</summary>
    public required decimal BaseOutstanding { get; init; }

    /// <summary>The number of open items that fell into this bucket.</summary>
    public required int ItemCount { get; init; }
}
