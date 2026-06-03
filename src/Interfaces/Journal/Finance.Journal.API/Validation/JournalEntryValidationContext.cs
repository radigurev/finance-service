using Finance.ServiceModel.Journal;

namespace Finance.Journal.API.Validation;

/// <summary>
/// The cross-aggregate validation request for a journal entry's invariants (SDD-FIN-001 §2.8). Carries
/// the entry's lines and frozen base currency so the chain validators (balance, account postability,
/// currency validity, base-amount reconciliation) can assert the invariants without touching lifecycle
/// state. Used by both the draft-create and post-time re-validation paths (SDD-FIN-002 §2.2).
/// </summary>
public sealed record JournalEntryValidationContext
{
    /// <summary>The base currency the entry balances in.</summary>
    public required string BaseCurrencyCode { get; init; }

    /// <summary>The lines whose invariants are being asserted.</summary>
    public required IReadOnlyList<JournalEntryLineRequest> Lines { get; init; }
}
