namespace Finance.Common.Enums;

/// <summary>
/// Lifecycle state of an <c>Invoice</c> (SDD-INV-001 §2.1). The transitions are
/// <c>Draft → Confirmed → Posted</c>, with <c>Cancelled</c> reachable from <c>Draft</c>/<c>Confirmed</c>
/// and <c>Posted → Reversed</c> via a fully-offsetting credit note. <c>Cancelled</c> and <c>Reversed</c>
/// are terminal. The value is stored as its string name so the workflow engine resolves states by
/// <c>StateName</c>.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>An unconfirmed, editable invoice with no document number assigned yet.</summary>
    Draft = 1,

    /// <summary>A confirmed, immutable invoice carrying a gapless document number, awaiting posting.</summary>
    Confirmed = 2,

    /// <summary>A posted, immutable invoice linked to its journal entry.</summary>
    Posted = 3,

    /// <summary>A voided invoice; a confirmed cancellation keeps (never recycles) its document number. Terminal.</summary>
    Cancelled = 4,

    /// <summary>A posted invoice fully offset by a credit note. Terminal.</summary>
    Reversed = 5
}
