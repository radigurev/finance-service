namespace Finance.Payments.API.Services;

/// <summary>
/// One effective aging bucket: its label and its inclusive days-past-due boundaries (SDD-PAY-003 §2.4). Produced
/// by <see cref="AgingBucketCalculator"/> and echoed onto the response so a client never re-derives a label or a
/// boundary.
/// <para>The first bucket (<c>Current</c>) is open-ended BELOW — it carries a null lower bound and an upper bound
/// of <c>0</c>, admitting every not-yet-due item including one due exactly on the as-of date. The final bucket is
/// open-ended ABOVE and carries a null upper bound.</para>
/// </summary>
public sealed record AgingBucketDefinition
{
    /// <summary>The bucket label (<c>Current</c>, <c>1-30</c>, <c>31-60</c>, <c>61-90</c>, <c>90+</c> by default).</summary>
    public required string Label { get; init; }

    /// <summary>The inclusive lower days-past-due bound, or <c>null</c> on the open-ended <c>Current</c> bucket.</summary>
    public int? FromDaysPastDue { get; init; }

    /// <summary>The inclusive upper days-past-due bound, or <c>null</c> on the open-ended final bucket.</summary>
    public int? ToDaysPastDue { get; init; }
}
