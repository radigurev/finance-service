namespace Finance.ServiceModel.Payments;

/// <summary>
/// One aging report row, keyed by the PAIR (<see cref="CounterpartyId"/>, <see cref="CurrencyCode"/>)
/// (SDD-PAY-003 §2.2, §2.6). A counterparty holding open items in two currencies therefore produces TWO rows;
/// only <see cref="TotalBaseOutstanding"/> may be summed across rows, and no cross-currency transactional total
/// is ever emitted.
/// <para>A counterparty whose in-scope outstanding is <c>0.00</c> is omitted from the report entirely — an
/// all-zero row is never emitted.</para>
/// </summary>
public sealed record AgingRowDto
{
    /// <summary>The Warehouse-owned counterparty reference. v1 returns the GUID only — name enrichment is deferred.</summary>
    public required Guid CounterpartyId { get; init; }

    /// <summary>The transactional currency this row aggregates; half of the grouping key.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The base currency the row's items book in, echoed unchanged from the projection.</summary>
    public required string BaseCurrencyCode { get; init; }

    /// <summary>The number of in-scope open items behind this row.</summary>
    public required int OpenItemCount { get; init; }

    /// <summary>The bucket breakdown in bucket order; exhaustive and mutually exclusive.</summary>
    public required IReadOnlyList<AgingBucketAmountDto> Buckets { get; init; }

    /// <summary>
    /// The row's outstanding total in its transactional currency. Equals the sum of every bucket's
    /// <c>Outstanding</c> to the cent.
    /// </summary>
    public required decimal TotalOutstanding { get; init; }

    /// <summary>
    /// The row's outstanding total converted at each item's frozen booking rate. Equals the sum of every bucket's
    /// <c>BaseOutstanding</c> to the cent.
    /// </summary>
    public required decimal TotalBaseOutstanding { get; init; }
}
