namespace Finance.Invoices.API.Auditing;

/// <summary>
/// Canonical audit <c>EventType</c> values for invoice lifecycle changes (SDD-INV-001 §2.3-§2.7,
/// SDD-AUDIT-001 §2.1). None of these are on the SDD-AUDIT-001 mandatory-reason list, but cancellation and
/// reversal carry an operator reason by domain rule (SDD-INV-001 §2.6, §2.7).
/// </summary>
public static class InvoiceAuditEventTypes
{
    /// <summary>Audit event type for draft creation.</summary>
    public const string InvoiceCreated = nameof(InvoiceCreated);

    /// <summary>Audit event type for a draft update.</summary>
    public const string InvoiceUpdated = nameof(InvoiceUpdated);

    /// <summary>Audit event type for a draft deletion.</summary>
    public const string InvoiceDeleted = nameof(InvoiceDeleted);

    /// <summary>Audit event type for confirmation (Draft → Confirmed).</summary>
    public const string InvoiceConfirmed = nameof(InvoiceConfirmed);

    /// <summary>Audit event type for posting completion (Confirmed → Posted).</summary>
    public const string InvoicePosted = nameof(InvoicePosted);

    /// <summary>Audit event type for cancellation (→ Cancelled). Carries a reason.</summary>
    public const string InvoiceCancelled = nameof(InvoiceCancelled);

    /// <summary>The audited entity type for invoice rows.</summary>
    public const string EntityType = "Invoice";
}
