using Finance.Common.Enums;

namespace Finance.ServiceModel.Payments;

/// <summary>
/// Representation of a single payment-to-invoice match exposed by the Payments API (SDD-PAY-002 §2.1, §2.7).
/// The invoice-side fields are joined from the LOCAL <c>InvoiceOpenItem</c> projection — a same-database join —
/// so no cross-service read occurs on the list path. There is no allocation status: a row either exists
/// (matched) or does not (released); the settlement state belongs to the invoice, not to the match row.
/// <para><c>AllocatedBy</c> and <c>CorrelationId</c> are deliberately NOT exposed, mirroring
/// <see cref="PaymentDto"/>.</para>
/// </summary>
public sealed record PaymentAllocationDto
{
    /// <summary>The internal identity of the match row (an <c>INT IDENTITY</c> child key, never event-exposed).</summary>
    public required int Id { get; init; }

    /// <summary>The owning payment.</summary>
    public required Guid PaymentId { get; init; }

    /// <summary>The cross-service identifier of the matched invoice (no foreign key).</summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>The transactional amount applied, in the payment's currency.</summary>
    public required decimal AllocatedAmount { get; init; }

    /// <summary>
    /// The country-rounded base-currency value of the applied amount at the payment's frozen rate. A REPORTING
    /// figure only — allocation posts nothing, so it is not a journal-entry base amount.
    /// </summary>
    public required decimal BaseAllocatedAmount { get; init; }

    /// <summary>
    /// The signed document-level base-currency difference between the two documents' frozen rates
    /// (SDD-PAY-002 §2.9). Informational only until SDD-FIN-005 posts it.
    /// </summary>
    public required decimal RealizedFxDifference { get; init; }

    /// <summary>The server timestamp the match was recorded at.</summary>
    public required DateTimeOffset AllocatedAt { get; init; }

    /// <summary>The matched invoice's document number, joined from the local projection.</summary>
    public string? InvoiceDocumentNumber { get; init; }

    /// <summary>The matched invoice's payment due date, joined from the local projection.</summary>
    public DateTimeOffset? InvoiceDueDate { get; init; }

    /// <summary>
    /// The matched invoice's mirrored lifecycle status (<c>Confirmed</c>, <c>Posted</c>, <c>Cancelled</c>, or
    /// <c>Reversed</c>), joined from the local projection.
    /// </summary>
    public string? InvoiceStatus { get; init; }

    /// <summary>The matched invoice's gross total, joined from the local projection.</summary>
    public decimal? InvoiceGrossTotal { get; init; }

    /// <summary>
    /// The matched invoice's DERIVED settlement state, computed by the single settlement calculator from the
    /// projection's settled amount and gross total (SDD-PAY-002 §2.8) — never re-derived in the DTO mapper.
    /// </summary>
    public SettlementStatus? InvoiceSettlementStatus { get; init; }
}
