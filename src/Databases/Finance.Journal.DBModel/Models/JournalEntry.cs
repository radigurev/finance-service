using Finance.Common.Enums;
using Finance.GenericFiltering.Attributes;

namespace Finance.Journal.DBModel.Models;

/// <summary>
/// The journal-entry aggregate root: a balanced set of debit/credit lines against chart-of-accounts
/// accounts (SDD-FIN-001 §2.1). Event-exposed and externally referenced, so its identifier is a
/// sequential GUID. Its lifecycle (<c>Draft → Posted → Reversed</c>) is owned by SDD-FIN-002.
/// </summary>
public sealed class JournalEntry
{
    /// <summary>Sequential-GUID identifier (event-exposed, externally referenced).</summary>
    public Guid Id { get; set; }

    /// <summary>The gapless document number assigned at posting; <c>null</c> while <c>Draft</c>.</summary>
    [Filterable]
    [Sortable]
    public string? EntryNumber { get; set; }

    /// <summary>The accounting date of the transaction (used for period assignment).</summary>
    [Filterable]
    [Sortable]
    public DateTimeOffset EntryDate { get; set; }

    /// <summary>Human-readable memo describing the entry.</summary>
    public required string Description { get; set; }

    /// <summary>The base currency the entry balances in (frozen at creation from <c>Country:BaseCurrency</c>).</summary>
    [Filterable]
    [Sortable]
    public required string BaseCurrencyCode { get; set; }

    /// <summary>The lifecycle state: <c>Draft</c>, <c>Posted</c>, or <c>Reversed</c>.</summary>
    [Filterable]
    [Sortable]
    public JournalEntryStatus Status { get; set; } = JournalEntryStatus.Draft;

    /// <summary>On a reversal entry, the identifier of the original entry it reverses; otherwise <c>null</c>.</summary>
    public Guid? ReversesEntryId { get; set; }

    /// <summary>The ambient correlation identifier captured at creation.</summary>
    public required string CorrelationId { get; set; }

    /// <summary>UTC-offset creation timestamp.</summary>
    [Filterable]
    [Sortable]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The identifier of the user who created the entry.</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>The posting timestamp; <c>null</c> while <c>Draft</c>.</summary>
    public DateTimeOffset? PostedAt { get; set; }

    /// <summary>The identifier of the user who posted the entry; <c>null</c> while <c>Draft</c>.</summary>
    public Guid? PostedBy { get; set; }

    /// <summary>SQL Server <c>rowversion</c> optimistic-concurrency token (SDD-INFRA-008/009).</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The lines composing the entry (composition: loaded and saved with the entry).</summary>
    public ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();

    /// <summary>The append-only state-transition history for the entry.</summary>
    public ICollection<JournalEntryStatusHistory> StatusHistory { get; set; } = new List<JournalEntryStatusHistory>();
}
