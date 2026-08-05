namespace Finance.ServiceModel.Payments;

/// <summary>
/// One aging bucket's REPORT-LEVEL total (SDD-PAY-003 §2.2, §2.6). It is expressed in BASE currency only and
/// deliberately carries no transactional amount: summing transactional amounts across different currencies is
/// meaningless, so only the base-currency column is cross-summable.
/// </summary>
public sealed record AgingBucketTotalDto
{
    /// <summary>The bucket label, identical to the per-row bucket labels and in the same bucket order.</summary>
    public required string Label { get; init; }

    /// <summary>The inclusive lower days-past-due bound, or <c>null</c> on the open-ended <c>Current</c> bucket.</summary>
    public int? FromDaysPastDue { get; init; }

    /// <summary>The inclusive upper days-past-due bound, or <c>null</c> on the open-ended final bucket.</summary>
    public int? ToDaysPastDue { get; init; }

    /// <summary>The bucket's total outstanding across every row, in the reporting base currency.</summary>
    public required decimal BaseOutstanding { get; init; }

    /// <summary>The number of open items that fell into this bucket across every row.</summary>
    public required int ItemCount { get; init; }
}
