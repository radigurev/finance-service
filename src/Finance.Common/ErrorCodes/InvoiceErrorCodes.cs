namespace Finance.Common.ErrorCodes;

/// <summary>
/// Machine-readable error codes for the Invoice domain (SDD-INV-001 §4). Used as the <c>title</c> field of
/// ProblemDetails responses and in FluentValidation <c>.WithErrorCode(...)</c> calls.
/// <para>The concurrency code is sourced from <see cref="CommonErrorCodes.CONCURRENT_MODIFICATION"/>
/// and referenced (never redefined) from here.</para>
/// </summary>
public static class InvoiceErrorCodes
{
    /// <summary>The referenced invoice does not exist (SDD-INV-001 §2.10).</summary>
    public const string INVOICE_NOT_FOUND = nameof(INVOICE_NOT_FOUND);

    /// <summary>A manual create or a confirm was attempted with zero lines (SDD-INV-001 §2.3, §2.13).</summary>
    public const string INVOICE_LINES_REQUIRED = nameof(INVOICE_LINES_REQUIRED);

    /// <summary>The document type is missing or not one of the four recognized values (SDD-INV-001 §3.1).</summary>
    public const string INVALID_INVOICE_DOCUMENT_TYPE = nameof(INVALID_INVOICE_DOCUMENT_TYPE);

    /// <summary>The counterparty reference is missing (SDD-INV-001 §3.1).</summary>
    public const string INVOICE_COUNTERPARTY_REQUIRED = nameof(INVOICE_COUNTERPARTY_REQUIRED);

    /// <summary>The currency code is missing or not a valid ISO 4217 three-letter code (SDD-INV-001 §3.1).</summary>
    public const string INVALID_INVOICE_CURRENCY = nameof(INVALID_INVOICE_CURRENCY);

    /// <summary>The issue date is missing or invalid (SDD-INV-001 §3.1).</summary>
    public const string INVALID_INVOICE_DATE = nameof(INVALID_INVOICE_DATE);

    /// <summary>The due date is missing or earlier than the issue date (SDD-INV-001 §3.1).</summary>
    public const string INVALID_INVOICE_DUE_DATE = nameof(INVALID_INVOICE_DUE_DATE);

    /// <summary>A line has a non-positive quantity or a negative unit price (SDD-INV-001 §3.1).</summary>
    public const string INVALID_INVOICE_LINE = nameof(INVALID_INVOICE_LINE);

    /// <summary>A line tax rate is negative or not a rate the country recognizes (SDD-INV-001 §2.8, §3.1).</summary>
    public const string INVALID_INVOICE_TAX_RATE = nameof(INVALID_INVOICE_TAX_RATE);

    /// <summary>Lines do not sum to the header, or net + tax does not equal gross to the cent (SDD-INV-001 §2.8, §3.2).</summary>
    public const string INVOICE_TOTALS_MISMATCH = nameof(INVOICE_TOTALS_MISMATCH);

    /// <summary>A confirm was attempted on an invoice that is not in the <c>Draft</c> state (SDD-INV-001 §2.4).</summary>
    public const string INVOICE_NOT_DRAFT = nameof(INVOICE_NOT_DRAFT);

    /// <summary>A post was attempted on a non-<c>Confirmed</c> invoice, or its posting is not yet linked (SDD-INV-001 §2.5).</summary>
    public const string INVOICE_NOT_CONFIRMED = nameof(INVOICE_NOT_CONFIRMED);

    /// <summary>An update or delete was attempted on a <c>Confirmed</c>/<c>Posted</c>/<c>Cancelled</c>/<c>Reversed</c> invoice (SDD-INV-001 §2.9).</summary>
    public const string INVOICE_POSTED_IMMUTABLE = nameof(INVOICE_POSTED_IMMUTABLE);

    /// <summary>
    /// The requested lifecycle transition is not allowed (e.g. cancelling a posted invoice). The
    /// Invoice-domain alias for the workflow engine's generic <c>INVALID_STATE_TRANSITION</c> (SDD-INV-001 §2.1).
    /// </summary>
    public const string INVALID_INVOICE_STATE_TRANSITION = nameof(INVALID_INVOICE_STATE_TRANSITION);

    /// <summary>
    /// The issue date falls in a closed or locked fiscal period. The real check is supplied by SDD-FIN-004;
    /// the default always-open guard never returns this code (SDD-INV-001 §2.2, §2.13).
    /// </summary>
    public const string INVOICE_PERIOD_CLOSED = nameof(INVOICE_PERIOD_CLOSED);

    /// <summary>A confirm/replay would assign a second gapless document number to an already-numbered invoice (SDD-INV-001 §3.3).</summary>
    public const string INVOICE_DUPLICATE_DOCUMENT_NUMBER = nameof(INVOICE_DUPLICATE_DOCUMENT_NUMBER);

    /// <summary>A cancel was requested without a non-empty reason (SDD-INV-001 §2.6).</summary>
    public const string INVOICE_CANCEL_REASON_REQUIRED = nameof(INVOICE_CANCEL_REASON_REQUIRED);

    /// <summary>
    /// A cancel was attempted on a <c>Draft</c>/<c>Confirmed</c> invoice that already carries payment
    /// allocations (<c>SettledAmount &gt; 0.00</c>) — the operator must deallocate in the Payments service
    /// first (SDD-INV-001 §2.6/§2.14, SDD-PAY-002 §2.6).
    /// <para><b>Best-effort guard, not a hard invariant.</b> It reads the asynchronously-fed settlement
    /// mirror and the handshake is deliberately one-way with no synchronous cross-service read, so a cancel
    /// racing an in-flight allocation can still succeed; SDD-PAY-002's cancellation consumer is the authority
    /// that detects the resulting orphaned allocation.</para>
    /// </summary>
    public const string INVOICE_HAS_SETTLEMENTS = nameof(INVOICE_HAS_SETTLEMENTS);

    /// <summary>
    /// A settlement event carried an authoritative settled amount that would drive the invoice above its
    /// gross total or below zero (SDD-INV-001 §2.14, §2.15 step 3). Defensive only — SDD-PAY-002 §2.5 forbids
    /// over-allocation at the source — and never surfaced over HTTP: the consumer fails so MassTransit retries
    /// and finally dead-letters rather than persisting a clamped ledger figure.
    /// </summary>
    public const string INVOICE_SETTLEMENT_EXCEEDS_GROSS_TOTAL = nameof(INVOICE_SETTLEMENT_EXCEEDS_GROSS_TOTAL);
}
