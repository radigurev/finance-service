namespace Finance.ServiceModel.Payments;

/// <summary>
/// The query narrowings of <c>GET /api/v1/open-items</c> (SDD-PAY-003 §2.5), bound from the query string
/// alongside the SDD-INFRA-005 <c>FilterRequest</c> that carries filtering, sorting and paging.
/// <para>Every narrowing is OPTIONAL: <see cref="AsOfDate"/> defaults to the current date, and an omitted
/// direction / counterparty / currency simply widens the list. The as-of date MUST NOT be in the future.</para>
/// </summary>
public sealed record OpenItemQueryRequest
{
    /// <summary>
    /// The inclusive upper bound of the accounting view and the reference date for days-past-due. Defaults to the
    /// current date when omitted; a FUTURE value is rejected.
    /// </summary>
    public DateTimeOffset? AsOfDate { get; init; }

    /// <summary>The optional ledger direction narrowing; <c>AR</c> or <c>AP</c> when supplied.</summary>
    public string? Direction { get; init; }

    /// <summary>The optional counterparty narrowing; a non-empty GUID when supplied.</summary>
    public Guid? CounterpartyId { get; init; }

    /// <summary>The optional transactional currency narrowing; a three-letter ISO 4217 code when supplied.</summary>
    public string? CurrencyCode { get; init; }

    /// <summary>
    /// When <see langword="true"/>, returns only items at least one day past due. Defaults to
    /// <see langword="false"/>, which returns both current and overdue items.
    /// </summary>
    public bool OverdueOnly { get; init; }
}
