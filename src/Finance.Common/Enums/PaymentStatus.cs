namespace Finance.Common.Enums;

/// <summary>
/// Lifecycle state of a <c>Payment</c> (SDD-PAY-001 §2.1). The transitions are
/// <c>Draft → Confirmed → Posted</c>, with <c>Cancelled</c> reachable from <c>Draft</c> ONLY and
/// <c>Posted → Reversed</c> via a sign-flipped journal entry. <c>Cancelled</c> and <c>Reversed</c> are
/// terminal. The value is stored as its string name so the workflow engine resolves states by
/// <c>StateName</c>.
/// </summary>
public enum PaymentStatus
{
    /// <summary>An unconfirmed, editable payment with no document number assigned yet.</summary>
    Draft = 1,

    /// <summary>A confirmed, immutable payment carrying a gapless document number, awaiting posting.</summary>
    Confirmed = 2,

    /// <summary>A posted, immutable payment linked to its journal entry.</summary>
    Posted = 3,

    /// <summary>A voided draft payment; it never held a document number. Terminal.</summary>
    Cancelled = 4,

    /// <summary>A posted payment corrected by a sign-flipped journal entry. Terminal.</summary>
    Reversed = 5
}
