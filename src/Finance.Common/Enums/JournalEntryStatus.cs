namespace Finance.Common.Enums;

/// <summary>
/// Lifecycle state of a journal entry (SDD-FIN-002 §2.1). The transitions are
/// <c>Draft → Posted</c> and <c>Posted → Reversed</c>; <c>Reversed</c> is terminal. The value is stored
/// as its string name so the workflow engine resolves states by <c>StateName</c>.
/// </summary>
public enum JournalEntryStatus
{
    /// <summary>An unposted, editable entry with no document number assigned yet.</summary>
    Draft = 1,

    /// <summary>A posted, immutable entry carrying a gapless document number.</summary>
    Posted = 2,

    /// <summary>A previously posted entry that has been reversed by a sign-flipped linked entry. Terminal.</summary>
    Reversed = 3
}
