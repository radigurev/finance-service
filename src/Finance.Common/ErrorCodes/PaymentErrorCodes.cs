namespace Finance.Common.ErrorCodes;

/// <summary>
/// Machine-readable error codes for the Payment domain (SDD-PAY-001 §4, SDD-PAY-002 §4, SDD-PAY-003 §4). Used
/// as the <c>title</c> field of ProblemDetails responses and in FluentValidation <c>.WithErrorCode(...)</c>
/// calls. SDD-PAY-002 and SDD-PAY-003 ADD their codes to this file rather than declaring a second one.
/// <para>The concurrency code is sourced from <see cref="CommonErrorCodes.CONCURRENT_MODIFICATION"/> and the
/// paging cap code from <see cref="FilterErrorCodes.PAGE_SIZE_TOO_LARGE"/>; both are referenced from here
/// and never redefined. The Journal-side posting codes (<c>POSTING_RULE_NOT_FOUND</c>,
/// <c>POSTING_RULE_UNBALANCED</c>, <c>MISSING_POSTING_AMOUNT</c>, <c>POSTING_PERIOD_CLOSED</c>) belong to
/// SDD-FIN-006 and are likewise not redefined here — they never reach a payment API caller.</para>
/// <para>Every SDD-PAY-003 aging code is a 400 VALIDATION code, so none of them is added to
/// <c>PaymentErrorCodeToStatusMap</c>: they resolve through its private default map. The aging read surface
/// deliberately declares no <c>COUNTERPARTY_NOT_FOUND</c> and no <c>OPEN_ITEM_NOT_FOUND</c> — an unknown
/// counterparty and an empty window are valid business states answered with an empty <c>200</c>.</para>
/// </summary>
public static class PaymentErrorCodes
{
    /// <summary>The referenced payment does not exist (SDD-PAY-001 §2.11, §3.3).</summary>
    public const string PAYMENT_NOT_FOUND = nameof(PAYMENT_NOT_FOUND);

    /// <summary>
    /// The settlement account is unknown, or the Accounts read seam is unreachable and the check fails
    /// closed (SDD-PAY-001 §2.8, §3.2).
    /// </summary>
    public const string PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND = nameof(PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND);

    /// <summary>
    /// The document type is missing / not one of the two recognized values, or an update attempted to
    /// change it (SDD-PAY-001 §2.6, §3.1).
    /// </summary>
    public const string INVALID_PAYMENT_DOCUMENT_TYPE = nameof(INVALID_PAYMENT_DOCUMENT_TYPE);

    /// <summary>The payment method is missing or not one of the three recognized values (SDD-PAY-001 §3.1).</summary>
    public const string INVALID_PAYMENT_METHOD = nameof(INVALID_PAYMENT_METHOD);

    /// <summary>The counterparty reference is missing (SDD-PAY-001 §3.1).</summary>
    public const string PAYMENT_COUNTERPARTY_REQUIRED = nameof(PAYMENT_COUNTERPARTY_REQUIRED);

    /// <summary>The currency code is missing or not a valid ISO 4217 three-letter code (SDD-PAY-001 §3.1).</summary>
    public const string INVALID_PAYMENT_CURRENCY = nameof(INVALID_PAYMENT_CURRENCY);

    /// <summary>The amount is zero or negative — a payment is always a positive cash movement (SDD-PAY-001 §2.8, §3.1).</summary>
    public const string INVALID_PAYMENT_AMOUNT = nameof(INVALID_PAYMENT_AMOUNT);

    /// <summary>
    /// The exchange rate is zero or negative, or differs from <c>1.000000</c> on a base-currency payment
    /// (SDD-PAY-001 §2.8, §3.1, §3.2).
    /// </summary>
    public const string INVALID_PAYMENT_EXCHANGE_RATE = nameof(INVALID_PAYMENT_EXCHANGE_RATE);

    /// <summary>The payment date is missing, invalid, or in the future (SDD-PAY-001 §3.1, §2.18).</summary>
    public const string INVALID_PAYMENT_DATE = nameof(INVALID_PAYMENT_DATE);

    /// <summary>The settlement account identifier is missing or non-positive (SDD-PAY-001 §3.1).</summary>
    public const string PAYMENT_SETTLEMENT_ACCOUNT_REQUIRED = nameof(PAYMENT_SETTLEMENT_ACCOUNT_REQUIRED);

    /// <summary>The bank reference exceeds 64 characters (SDD-PAY-001 §3.1).</summary>
    public const string INVALID_PAYMENT_BANK_REFERENCE = nameof(INVALID_PAYMENT_BANK_REFERENCE);

    /// <summary>
    /// The stored base amount does not equal the rounded <c>Amount × ExchangeRate</c>. Defensive: the
    /// service always recomputes the base amount first, so this is unreachable through the v1 paths and is
    /// retained as defense-in-depth (SDD-PAY-001 §3.2, §4).
    /// </summary>
    public const string PAYMENT_BASE_AMOUNT_MISMATCH = nameof(PAYMENT_BASE_AMOUNT_MISMATCH);

    /// <summary>A cancel was requested without a non-empty reason (SDD-PAY-001 §2.6, §2.15).</summary>
    public const string PAYMENT_CANCEL_REASON_REQUIRED = nameof(PAYMENT_CANCEL_REASON_REQUIRED);

    /// <summary>A reverse was requested without a non-empty reason (SDD-PAY-001 §2.7, §2.15).</summary>
    public const string PAYMENT_REVERSE_REASON_REQUIRED = nameof(PAYMENT_REVERSE_REASON_REQUIRED);

    /// <summary>A confirm was attempted on a payment that is not in the <c>Draft</c> state (SDD-PAY-001 §2.4).</summary>
    public const string PAYMENT_NOT_DRAFT = nameof(PAYMENT_NOT_DRAFT);

    /// <summary>
    /// A post or a back-event link was attempted on a payment that is neither <c>Confirmed</c> nor already
    /// <c>Posted</c> (SDD-PAY-001 §2.5, §3.3).
    /// </summary>
    public const string PAYMENT_NOT_CONFIRMED = nameof(PAYMENT_NOT_CONFIRMED);

    /// <summary>
    /// A post was attempted while the Journal handshake has not yet linked a journal entry. The same call
    /// re-enqueues <c>PaymentConfirmedEvent</c> as the recovery path (SDD-PAY-001 §2.5, §2.18).
    /// </summary>
    public const string PAYMENT_POSTING_PENDING = nameof(PAYMENT_POSTING_PENDING);

    /// <summary>
    /// An update or delete was attempted on a <c>Confirmed</c>/<c>Posted</c>/<c>Cancelled</c>/<c>Reversed</c>
    /// payment (SDD-PAY-001 §2.6, §2.10).
    /// </summary>
    public const string PAYMENT_POSTED_IMMUTABLE = nameof(PAYMENT_POSTED_IMMUTABLE);

    /// <summary>
    /// The requested lifecycle transition is not allowed (e.g. cancelling a <c>Confirmed</c> payment, which
    /// is deliberately absent from <c>AllowedNextStates</c>). The Payment-domain alias for the workflow
    /// engine's generic <c>INVALID_STATE_TRANSITION</c> (SDD-PAY-001 §2.1, §4).
    /// </summary>
    public const string INVALID_PAYMENT_STATE_TRANSITION = nameof(INVALID_PAYMENT_STATE_TRANSITION);

    /// <summary>
    /// The payment date falls in a closed fiscal period, has no period at all, or the Periods service is
    /// unreachable and the guard fails closed (SDD-PAY-001 §2.9, §2.7).
    /// </summary>
    public const string PAYMENT_PERIOD_CLOSED = nameof(PAYMENT_PERIOD_CLOSED);

    /// <summary>A confirm/replay would assign a second gapless document number (SDD-PAY-001 §2.4, §3.3).</summary>
    public const string PAYMENT_DUPLICATE_DOCUMENT_NUMBER = nameof(PAYMENT_DUPLICATE_DOCUMENT_NUMBER);

    /// <summary>
    /// A confirm was attempted where <c>PaymentDate.Year</c> differs from the confirm-clock year, which pins
    /// both the document number's year and the gapless series (SDD-PAY-001 §2.2, §2.4).
    /// </summary>
    public const string PAYMENT_DATE_YEAR_MISMATCH = nameof(PAYMENT_DATE_YEAR_MISMATCH);

    /// <summary>
    /// A cancel or reverse was attempted while <c>AllocatedAmount &gt; 0</c>. Allocations are never
    /// auto-released — the operator deallocates first (SDD-PAY-001 §2.6, §2.7; SDD-PAY-002).
    /// </summary>
    public const string PAYMENT_HAS_ALLOCATIONS = nameof(PAYMENT_HAS_ALLOCATIONS);

    /// <summary>The settlement account exists but is not active (SDD-PAY-001 §2.8, §3.2).</summary>
    public const string PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE = nameof(PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE);

    /// <summary>
    /// The allocation identifier does not exist for the route payment. The lookup is scoped by
    /// <c>(PaymentId, Id)</c>, so an allocation owned by a DIFFERENT payment yields this code rather than a
    /// cross-payment delete (SDD-PAY-002 §2.6, §3.3).
    /// </summary>
    public const string PAYMENT_ALLOCATION_NOT_FOUND = nameof(PAYMENT_ALLOCATION_NOT_FOUND);

    /// <summary>
    /// The requested invoice is unknown to the LOCAL <c>InvoiceOpenItem</c> projection (SDD-PAY-002 §2.3,
    /// §2.5 rule 2). Two causes share the code and the service deliberately cannot tell them apart: a
    /// transient projection lag, which clears on retry, and a document type the §2.3 admission rule never
    /// projects (v1: a credit note), which is permanent. It is a genuine 404, never a 503.
    /// </summary>
    public const string PAYMENT_ALLOCATION_INVOICE_NOT_FOUND = nameof(PAYMENT_ALLOCATION_INVOICE_NOT_FOUND);

    /// <summary>Allocate was called with a missing or empty item list; v1 requires an explicit invoice list (SDD-PAY-002 §2.4, §3.1).</summary>
    public const string PAYMENT_ALLOCATION_ITEMS_REQUIRED = nameof(PAYMENT_ALLOCATION_ITEMS_REQUIRED);

    /// <summary>An allocation item omits its invoice identifier (SDD-PAY-002 §3.1).</summary>
    public const string PAYMENT_ALLOCATION_INVOICE_REQUIRED = nameof(PAYMENT_ALLOCATION_INVOICE_REQUIRED);

    /// <summary>An allocation item amount is zero or negative, or carries more than two decimal places (SDD-PAY-002 §2.1, §3.1).</summary>
    public const string INVALID_PAYMENT_ALLOCATION_AMOUNT = nameof(INVALID_PAYMENT_ALLOCATION_AMOUNT);

    /// <summary>
    /// Allocate or deallocate was attempted on a payment whose status is not <c>Confirmed</c> or
    /// <c>Posted</c> (SDD-PAY-002 §2.5 rule 1, §2.6, §3.3).
    /// </summary>
    public const string PAYMENT_NOT_ALLOCATABLE = nameof(PAYMENT_NOT_ALLOCATABLE);

    /// <summary>
    /// The invoice open item's mirrored status is not <c>Confirmed</c> or <c>Posted</c> — it is the terminal
    /// <c>Cancelled</c> (including a §2.3 cancellation tombstone) or <c>Reversed</c> (SDD-PAY-002 §2.5
    /// rule 3).
    /// </summary>
    public const string PAYMENT_ALLOCATION_INVOICE_NOT_ELIGIBLE = nameof(PAYMENT_ALLOCATION_INVOICE_NOT_ELIGIBLE);

    /// <summary>
    /// The sum of existing plus requested allocations exceeds <c>Payment.Amount</c>, compared as exact
    /// <c>DECIMAL(18,2)</c> values with no tolerance band (SDD-PAY-002 §2.5 rule 8).
    /// </summary>
    public const string PAYMENT_ALLOCATION_EXCEEDS_PAYMENT = nameof(PAYMENT_ALLOCATION_EXCEEDS_PAYMENT);

    /// <summary>
    /// An invoice's settled amount plus the requested amount exceeds its gross total, compared as exact
    /// <c>DECIMAL(18,2)</c> values (SDD-PAY-002 §2.5 rule 9).
    /// </summary>
    public const string PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING = nameof(PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING);

    /// <summary>
    /// The payment direction differs from the invoice direction, compared by enum member name
    /// (<c>AR</c>/<c>AP</c>). A cash-direction pre-filter only — the accounting match is the control-account
    /// rule (SDD-PAY-002 §2.5 rule 4).
    /// </summary>
    public const string PAYMENT_ALLOCATION_DIRECTION_MISMATCH = nameof(PAYMENT_ALLOCATION_DIRECTION_MISMATCH);

    /// <summary>The payment counterparty differs from the invoice counterparty (SDD-PAY-002 §2.5 rule 5).</summary>
    public const string PAYMENT_ALLOCATION_COUNTERPARTY_MISMATCH = nameof(PAYMENT_ALLOCATION_COUNTERPARTY_MISMATCH);

    /// <summary>
    /// The payment currency differs from the invoice currency; cross-currency allocation is deferred to
    /// SDD-FIN-005 (SDD-PAY-002 §2.5 rule 6).
    /// </summary>
    public const string PAYMENT_ALLOCATION_CURRENCY_MISMATCH = nameof(PAYMENT_ALLOCATION_CURRENCY_MISMATCH);

    /// <summary>
    /// The payment and invoice document types are not a documented <c>SettlementPairing</c> pair, so the two
    /// documents moved DIFFERENT control accounts. <b>Defensive code</b>: unreachable through the v1 paths,
    /// because SDD-PAY-002 §2.3 admits only settleable document types into the projection, so every mismatch
    /// short-circuits earlier on the direction rule or on the unknown-invoice rule. Retained as
    /// defense-in-depth and for a future <c>SettlementPairing</c> widening (SDD-PAY-002 §2.5 rule 10, §2.14).
    /// </summary>
    public const string PAYMENT_ALLOCATION_CONTROL_ACCOUNT_MISMATCH =
        nameof(PAYMENT_ALLOCATION_CONTROL_ACCOUNT_MISMATCH);

    /// <summary>
    /// An allocation row already exists — or is requested twice within the same request — for the
    /// <c>(PaymentId, InvoiceId)</c> pair. Also backed by the UNIQUE index
    /// <c>IX_PaymentAllocations_PaymentInvoice</c>, but the chain (never a <c>DbUpdateException</c>) is the
    /// user-facing path (SDD-PAY-002 §2.5 rule 7, §2.14).
    /// </summary>
    public const string PAYMENT_ALLOCATION_DUPLICATE = nameof(PAYMENT_ALLOCATION_DUPLICATE);

    /// <summary>
    /// The as-of date is missing on an aging report endpoint, or it is in the FUTURE on any aging endpoint
    /// (SDD-PAY-003 §2.3, §3.1, §4). A future date would age not-yet-due documents against a calendar that has
    /// not happened, so the request is rejected before any query runs. Always <c>400</c>.
    /// </summary>
    public const string INVALID_AGING_AS_OF_DATE = nameof(INVALID_AGING_AS_OF_DATE);

    /// <summary>
    /// The aging direction is missing on a report endpoint, or is not <c>AR</c>/<c>AP</c> (SDD-PAY-003 §3.1,
    /// §4). Always <c>400</c>.
    /// </summary>
    public const string INVALID_AGING_DIRECTION = nameof(INVALID_AGING_DIRECTION);

    /// <summary>
    /// The supplied aging bucket boundaries are not strictly ascending positive integers, or there are more
    /// than six of them (SDD-PAY-003 §2.4, §3.1, §4). Always <c>400</c>.
    /// </summary>
    public const string INVALID_AGING_BUCKETS = nameof(INVALID_AGING_BUCKETS);

    /// <summary>
    /// A counterparty narrowing was supplied as an EMPTY GUID (SDD-PAY-003 §3.1, §4). An unknown-but-well-formed
    /// counterparty is NOT an error — it yields an empty <c>200</c>, because the counterparty is Warehouse-owned
    /// master data this service deliberately does not pre-check. Always <c>400</c>.
    /// </summary>
    public const string INVALID_COUNTERPARTY_ID = nameof(INVALID_COUNTERPARTY_ID);

    /// <summary>
    /// A currency narrowing was supplied but is not a three-letter ISO 4217 code (SDD-PAY-003 §3.1, §4).
    /// Always <c>400</c>.
    /// </summary>
    public const string INVALID_AGING_CURRENCY = nameof(INVALID_AGING_CURRENCY);
}
