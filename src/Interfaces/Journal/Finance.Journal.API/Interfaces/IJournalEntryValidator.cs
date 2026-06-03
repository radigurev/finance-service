using Finance.Common.Results;
using Finance.Journal.API.Validation;

namespace Finance.Journal.API.Interfaces;

/// <summary>
/// The single double-entry validation surface defined by SDD-FIN-001 §2.8 and invoked by the create and
/// post paths (SDD-FIN-002 §2.2, §2.3). It runs the FluentValidation shape rules (debit-XOR-credit,
/// no-zero, min-two-lines, currency shape) followed by the cross-aggregate chain (balance, account
/// postability, currency validity, base-amount reconciliation), short-circuiting to the first failing
/// code. It is pure with respect to lifecycle: it changes no state and publishes no events.
/// </summary>
public interface IJournalEntryValidator
{
    /// <summary>
    /// Validates the supplied entry against the full SDD-FIN-001 invariant surface.
    /// </summary>
    /// <param name="context">The lines and base currency to validate.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result when valid; otherwise a failure carrying the first violated code.</returns>
    Task<Result> ValidateAsync(JournalEntryValidationContext context, CancellationToken cancellationToken);
}
