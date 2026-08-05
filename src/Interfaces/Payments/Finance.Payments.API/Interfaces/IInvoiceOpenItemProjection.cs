using Finance.Common.Results;
using Finance.ServiceModel.Events.Invoices;

namespace Finance.Payments.API.Interfaces;

/// <summary>
/// The write side of the LOCAL invoice open-item projection (SDD-PAY-002 §2.2, §2.3). The four projection
/// consumers are thin shells over this seam so the convergence rules live in exactly one place.
/// <para><b>Convergence rules that hold for every member.</b> Each apply is an UPSERT keyed by the invoice
/// identifier, so a post-dedupe-window replay, a dead-letter redelivery, or an out-of-order pair still
/// converges. Status only ever moves <c>Confirmed → Posted</c> and into a terminal state, never back.
/// <c>Cancelled</c> and <c>Reversed</c> are TERMINAL: no member may move a row out of either, which is what
/// makes the cancellation tombstone effective. No member EVER writes the locally-owned settled amount — that
/// column is maintained only by the allocate and deallocate transactions.</para>
/// <para>A failure result signals the calling consumer to THROW so MassTransit retries (1s/5s/15s) and finally
/// dead-letters. That contract depends on CHG-FIX-006, which makes the shared idempotency filter release its
/// Redis claim when the downstream pipe throws.</para>
/// </summary>
public interface IInvoiceOpenItemProjection
{
    /// <summary>
    /// Applies an invoice confirmation (SDD-PAY-002 §2.3): inserts the open item as <c>Confirmed</c> with a
    /// zero settled amount, or refreshes the externally-owned columns of an existing non-terminal row.
    /// <para>An invoice whose document type NO settlement pair can settle (v1: a credit note) is a SILENT
    /// SUCCESS — no row, no throw, no dead letter — because such a document could never reach a zero
    /// outstanding and would age as a phantom balance forever. The predicate is
    /// <c>SettlementPairing.IsSettleableInvoiceType</c>, derived from the same pairs as the allocation rule so
    /// admission and allocation cannot drift.</para>
    /// <para>A late confirmation MUST NOT downgrade an already-<c>Posted</c> row, and MUST NOT resurrect a
    /// terminal one.</para>
    /// </summary>
    /// <param name="message">The invoice confirmation event carrying the full projection payload.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result, including for a deliberately skipped document type.</returns>
    Task<Result> ApplyConfirmedAsync(InvoiceConfirmedEvent message, CancellationToken cancellationToken);

    /// <summary>
    /// Applies an invoice posting (SDD-PAY-002 §2.3): sets the mirrored status to <c>Posted</c> on an existing
    /// row.
    /// <para>It MUST NOT create a row — the posting event carries only identifiers, which is not enough to build
    /// a valid open item. A MISSING row is a FAILURE so the consumer throws and MassTransit retries, which is
    /// what makes the out-of-order pair converge; a genuinely lost pair is repaired by the deferred
    /// reconciliation job, never by inventing a partial row. The posting event carries no document type, so a
    /// deliberately skipped invoice is indistinguishable from a confirmation that has not landed yet: a posted
    /// non-settleable invoice therefore exhausts the retry schedule and dead-letters, which is EXPECTED,
    /// lossless noise and MUST NOT be triaged as projection drift.</para>
    /// </summary>
    /// <param name="invoiceId">The invoice whose posting completed.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result, or a not-found failure the consumer rethrows for retry.</returns>
    Task<Result> ApplyPostedAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>
    /// Applies an invoice reversal (SDD-PAY-002 §2.3): sets the mirrored status to <c>Reversed</c> on an
    /// existing row, which makes the item ineligible for further allocation.
    /// <para>Without this mirror a reversed invoice — whose GL effect is fully offset — would read
    /// <c>Posted</c> forever and stay a legal allocation target, so real cash could be matched to a document
    /// carrying no ledger balance while the genuinely open invoice stayed outstanding. Like the posting apply it
    /// MUST NOT create a row (a reversal presupposes a posted invoice the projection has already seen), MUST NOT
    /// delete the row, and MUST NOT remove or release existing allocation rows.</para>
    /// </summary>
    /// <param name="invoiceId">The invoice that was reversed.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result, or a not-found failure the consumer rethrows for retry.</returns>
    Task<Result> ApplyReversedAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>
    /// Applies an invoice cancellation (SDD-PAY-002 §2.3): sets the mirrored status to <c>Cancelled</c>, keeping
    /// the row and every existing allocation row so history stays auditable.
    /// <para>An UNKNOWN invoice TOMBSTONES rather than no-opping: a row is upserted as <c>Cancelled</c> with the
    /// event's known fields and zero/default values elsewhere. This is the ONE deliberate exception to the
    /// "missing row means throw for retry" rule, because a DRAFT cancellation publishes this event too and a
    /// draft never enters the projection — retrying could only dead-letter every draft cancel. A plain no-op is
    /// unsafe: a cancellation landing in the gap between a failed confirmation and its retry would let the
    /// retry insert the row as <c>Confirmed</c>, leaving a cancelled invoice permanently allocatable.</para>
    /// <para>When the row ALREADY existed, an ORPHANED-SETTLEMENT check runs: allocation rows still pointing at
    /// a cancelled invoice are flagged with a structured warning. Payments — not Invoices — is the authority for
    /// what is actually allocated, so this is the detection point for a cancel that raced an in-flight
    /// allocation. The status change is still applied: the orphan is never auto-released.</para>
    /// </summary>
    /// <param name="message">The invoice cancellation event; its document number is null for a draft cancel.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result; this member never fails for a missing row.</returns>
    Task<Result> ApplyCancelledAsync(InvoiceCancelledEvent message, CancellationToken cancellationToken);
}
