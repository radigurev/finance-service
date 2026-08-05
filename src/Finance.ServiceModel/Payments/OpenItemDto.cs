using Finance.Common.Enums;

namespace Finance.ServiceModel.Payments;

/// <summary>
/// One invoice that still carries an outstanding amount as of a date — the drill-down behind a single aging cell
/// (SDD-PAY-003 §2.5). This is the ONLY open-item shape in the solution: SDD-PAY-002 owns the
/// <c>InvoiceOpenItem</c> projection but declares no mirror DTO, so the computed report fields
/// (<see cref="Outstanding"/>, <see cref="BaseOutstanding"/>, <see cref="DaysPastDue"/>,
/// <see cref="AgingBucket"/>) live here and nowhere else.
/// <para><b>Only in-force, settleable documents appear.</b> The mirrored <see cref="InvoiceStatus"/> is always
/// <c>Confirmed</c> or <c>Posted</c> — <c>Cancelled</c> and <c>Reversed</c> documents are excluded from every row,
/// bucket and total — and the document type is always one a payment can actually settle. A confirmed CREDIT NOTE
/// is therefore absent permanently and BY DESIGN (no settlement pairing can discharge one); that absence is not
/// projection lag and not a defect.</para>
/// <para><b>Eventually consistent.</b> The underlying projection is fed by the invoice service's domain events,
/// so an invoice confirmed moments ago may be missing until its event is consumed, and a cancelled or reversed
/// one may still appear until its own event lands.</para>
/// <para><b>Historical as-of dates are replayed, not reconstructed.</b> For a past as-of date the settled amount
/// is the sum of the invoice's surviving allocation rows up to that date; because a deallocation REMOVES its row,
/// the figure is the sub-ledger as it stands now replayed by allocation date, not a bi-temporal audit
/// reconstruction.</para>
/// </summary>
public sealed record OpenItemDto
{
    /// <summary>The cross-service invoice identifier and the projection key (no foreign key).</summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>The invoice's document number.</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>The invoice document type name; always a type some payment document type can settle.</summary>
    public required string DocumentType { get; init; }

    /// <summary>The invoice's ledger direction name (<c>AR</c>/<c>AP</c>).</summary>
    public required string Direction { get; init; }

    /// <summary>The Warehouse-owned counterparty reference. v1 returns the GUID only — name enrichment is deferred.</summary>
    public required Guid CounterpartyId { get; init; }

    /// <summary>The invoice's transactional currency code.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The base currency the invoice books in, echoed unchanged from the projection.</summary>
    public required string BaseCurrencyCode { get; init; }

    /// <summary>The invoice gross total in its transactional currency.</summary>
    public required decimal GrossTotal { get; init; }

    /// <summary>
    /// The matched total AS OF the requested date: the maintained projection column for today, or the sum of the
    /// invoice's allocation rows belonging to <c>Confirmed</c>/<c>Posted</c> payments for a past date.
    /// </summary>
    public required decimal SettledAmount { get; init; }

    /// <summary>
    /// The still-open amount in the transactional currency: <see cref="GrossTotal"/> −
    /// <see cref="SettledAmount"/>, always strictly greater than <c>0.00</c> (a fully settled document is history,
    /// not an open item, and is omitted).
    /// </summary>
    public required decimal Outstanding { get; init; }

    /// <summary>
    /// <see cref="Outstanding"/> converted at the invoice's FROZEN booking exchange rate and rounded by the
    /// country strategy. No current rate is looked up and no revaluation is performed, so this is a genuine
    /// booking-rate figure — never an approximation. It equals <see cref="Outstanding"/> exactly when the item is
    /// already in base currency.
    /// </summary>
    public required decimal BaseOutstanding { get; init; }

    /// <summary>The invoice issue date; always on or before the requested as-of date.</summary>
    public required DateTimeOffset IssueDate { get; init; }

    /// <summary>The invoice payment due date — the aging bucket key.</summary>
    public required DateTimeOffset DueDate { get; init; }

    /// <summary>
    /// Whole days from <see cref="DueDate"/> to the as-of date computed on the DATE parts only, so a same-day
    /// comparison yields <c>0</c> regardless of time of day. A not-yet-due item yields a value of <c>0</c> or less.
    /// </summary>
    public required int DaysPastDue { get; init; }

    /// <summary>
    /// The aging bucket label this item falls into (<c>Current</c>, <c>1-30</c>, …). Buckets are exhaustive and
    /// mutually exclusive, so every item carries exactly one label.
    /// </summary>
    public required string AgingBucket { get; init; }

    /// <summary>
    /// The invoice's DERIVED settlement state, computed by the single SDD-PAY-002 settlement calculator from the
    /// as-of settled amount and the gross total — never re-derived here.
    /// </summary>
    public required SettlementStatus SettlementStatus { get; init; }

    /// <summary>The mirrored invoice lifecycle status; always <c>Confirmed</c> or <c>Posted</c>.</summary>
    public required string InvoiceStatus { get; init; }
}
