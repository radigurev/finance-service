namespace Finance.ServiceModel.Payments;

/// <summary>
/// The query parameters of <c>GET /api/v1/aging</c> (SDD-PAY-003 §2.6). <see cref="AsOfDate"/> and
/// <see cref="Direction"/> are REQUIRED — the report is meaningless without a reference date and a side of the
/// ledger — while the counterparty, currency and bucket narrowings are optional.
/// </summary>
public sealed record AgingReportQueryRequest
{
    /// <summary>
    /// REQUIRED. The inclusive upper bound of the accounting view and the reference date for bucketing. A missing
    /// or FUTURE value is rejected before any query runs.
    /// </summary>
    public DateTimeOffset? AsOfDate { get; init; }

    /// <summary>REQUIRED. The ledger direction to report: <c>AR</c> or <c>AP</c>.</summary>
    public string? Direction { get; init; }

    /// <summary>The optional counterparty narrowing; a non-empty GUID when supplied.</summary>
    public Guid? CounterpartyId { get; init; }

    /// <summary>The optional transactional currency narrowing; a three-letter ISO 4217 code when supplied.</summary>
    public string? CurrencyCode { get; init; }

    /// <summary>
    /// The optional bucket day boundaries, supplied as repeated <c>buckets</c> query values (e.g.
    /// <c>?buckets=15&amp;buckets=30&amp;buckets=60</c>). They MUST be strictly ascending positive integers and
    /// there MUST be at most six of them. When omitted the documented default <c>30, 60, 90</c> is used, yielding
    /// <c>Current</c>, <c>1-30</c>, <c>31-60</c>, <c>61-90</c>, <c>90+</c>.
    /// </summary>
    public int[]? Buckets { get; init; }
}
