namespace Finance.Journal.API.Auditing;

/// <summary>
/// Canonical audit <c>EventType</c> values for journal-entry lifecycle changes (SDD-FIN-002 §2.3-§2.6,
/// SDD-AUDIT-001 §2.1). <c>JournalEntryReversed</c> is high-sensitivity and MUST carry a reason — the
/// value matches <c>SensitiveAuditEventTypes.JournalEntryReversed</c> so the audit service enforces it.
/// </summary>
public static class JournalAuditEventTypes
{
    /// <summary>Audit event type for draft creation.</summary>
    public const string JournalEntryCreated = nameof(JournalEntryCreated);

    /// <summary>Audit event type for a draft update.</summary>
    public const string JournalEntryUpdated = nameof(JournalEntryUpdated);

    /// <summary>Audit event type for a draft deletion.</summary>
    public const string JournalEntryDeleted = nameof(JournalEntryDeleted);

    /// <summary>Audit event type for posting (Draft → Posted).</summary>
    public const string JournalEntryPosted = nameof(JournalEntryPosted);

    /// <summary>Audit event type for reversal (Posted → Reversed). High-sensitivity: requires a reason.</summary>
    public const string JournalEntryReversed = "JournalEntryReversed";

    /// <summary>The audited entity type for journal-entry rows.</summary>
    public const string EntityType = "JournalEntry";
}
