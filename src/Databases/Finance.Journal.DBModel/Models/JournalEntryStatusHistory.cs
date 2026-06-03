namespace Finance.Journal.DBModel.Models;

/// <summary>
/// An append-only record of one workflow state transition of a <see cref="JournalEntry"/>
/// (SDD-FIN-002 §2.4, §2.6; SDD-INFRA-008 §2.4). Written by the service inside the same transaction as
/// the transition it describes.
/// </summary>
public sealed class JournalEntryStatusHistory
{
    /// <summary>Internal surrogate identifier.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the entry whose state changed.</summary>
    public Guid JournalEntryId { get; set; }

    /// <summary>Navigation to the owning entry.</summary>
    public JournalEntry? JournalEntry { get; set; }

    /// <summary>The state the entry transitioned from (<c>null</c> for the initial history row).</summary>
    public string? FromStatus { get; set; }

    /// <summary>The state the entry transitioned to.</summary>
    public required string ToStatus { get; set; }

    /// <summary>The identifier of the user who performed the transition.</summary>
    public Guid ChangedBy { get; set; }

    /// <summary>The UTC-offset moment the transition occurred.</summary>
    public DateTimeOffset ChangedAt { get; set; }

    /// <summary>The ambient correlation identifier tying the row to the originating request.</summary>
    public required string CorrelationId { get; set; }

    /// <summary>Optional operator-supplied reason carried on sensitive transitions (e.g. reversal).</summary>
    public string? Reason { get; set; }
}
