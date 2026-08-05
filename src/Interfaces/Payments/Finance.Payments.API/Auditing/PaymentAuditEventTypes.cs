namespace Finance.Payments.API.Auditing;

/// <summary>
/// Canonical audit <c>EventType</c> values for payment lifecycle changes (SDD-PAY-001 §2.15,
/// SDD-AUDIT-001 §2.1). <see cref="PaymentCancelled"/> and <see cref="PaymentReversed"/> are SENSITIVE and
/// MUST carry a non-empty reason; both are also registered in the shared
/// <c>SensitiveAuditEventTypes</c> set so the audit layer independently rejects a reasonless entry with
/// <c>AUDIT_REASON_REQUIRED</c>.
/// </summary>
public static class PaymentAuditEventTypes
{
    /// <summary>Audit event type for draft creation.</summary>
    public const string PaymentCreated = nameof(PaymentCreated);

    /// <summary>Audit event type for a draft update.</summary>
    public const string PaymentUpdated = nameof(PaymentUpdated);

    /// <summary>Audit event type for a draft deletion.</summary>
    public const string PaymentDeleted = nameof(PaymentDeleted);

    /// <summary>Audit event type for confirmation (Draft → Confirmed).</summary>
    public const string PaymentConfirmed = nameof(PaymentConfirmed);

    /// <summary>Audit event type for posting completion (Confirmed → Posted).</summary>
    public const string PaymentPosted = nameof(PaymentPosted);

    /// <summary>Audit event type for cancellation (Draft → Cancelled). SENSITIVE — carries a reason.</summary>
    public const string PaymentCancelled = nameof(PaymentCancelled);

    /// <summary>Audit event type for reversal (Posted → Reversed). SENSITIVE — carries a reason.</summary>
    public const string PaymentReversed = nameof(PaymentReversed);

    /// <summary>
    /// Audit event type for a sub-ledger match being created (SDD-PAY-002 §2.11). Recorded as an
    /// <c>Update</c> on the PAYMENT — the audited subject is the payment whose matching changed, never the
    /// allocation row alone — with the payment's pre-change matching projection as the before snapshot. Not
    /// sensitive: no reason is required.
    /// </summary>
    public const string PaymentAllocated = nameof(PaymentAllocated);

    /// <summary>
    /// Audit event type for a sub-ledger match being released (SDD-PAY-002 §2.11). Recorded as an
    /// <c>Update</c> on the PAYMENT, with the pre-change matching projection plus the removed row as the before
    /// snapshot. Not sensitive, but a caller MAY supply an optional free-text reason, which is persisted here.
    /// </summary>
    public const string PaymentDeallocated = nameof(PaymentDeallocated);

    /// <summary>The audited entity type for payment rows.</summary>
    public const string EntityType = "Payment";
}
