using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Interfaces;

/// <summary>
/// Application service for payment allocation — the sub-ledger MATCHING layer on top of the SDD-PAY-001 payment
/// aggregate (SDD-PAY-002). Every method returns a <see cref="Result"/> / <see cref="Result{T}"/> and threads a
/// <see cref="CancellationToken"/> down to the validation chain, the database, the realized-FX seam, the audit
/// write, and the outbox publish.
/// <para>Allocation is matching, NOT posting: no method creates, mutates, or reverses a journal entry, changes
/// any GL or trial-balance figure, changes the payment's status, or invokes the payment workflow engine.
/// Allocations, open items, settlement state, and outstanding balances are TRANSACTIONAL data and are NEVER
/// cached.</para>
/// </summary>
public interface IPaymentAllocationService
{
    /// <summary>
    /// Lists a payment's allocation rows as a filtered, sorted, and paged envelope, each row enriched from the
    /// LOCAL open-item projection with the invoice's document number, due date, status, gross total, and derived
    /// settlement state (SDD-PAY-002 §2.7). The enrichment is a same-database join — no cross-service read
    /// occurs on this path, and nothing is cached.
    /// <para>An unknown payment yields <c>PAYMENT_NOT_FOUND</c>; a payment with no allocations returns an EMPTY
    /// page with success, because an unallocated payment is a normal business state.</para>
    /// </summary>
    /// <param name="paymentId">The owning payment identifier from the route.</param>
    /// <param name="request">The filter, sort, and pagination request (page size capped at 200).</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A page of <see cref="PaymentAllocationDto"/>, default-ordered by allocation time descending.</returns>
    Task<Result<PagedResult<PaymentAllocationDto>>> ListAsync(
        Guid paymentId,
        FilterRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Matches a confirmed-or-later payment against an explicit list of invoices (SDD-PAY-002 §2.4). The call is
    /// ALL-OR-NOTHING inside one transaction: if any item fails any chain validator, no row is written, no event
    /// is published, no audit row is created, and the payment's allocated amount is unchanged.
    /// <para>On success it inserts one allocation row per item (computing the base allocated amount and the
    /// signed realized-FX difference and invoking the dormant FX seam), increases the payment's allocated amount
    /// and each open item's locally-owned settled amount, writes one audit row per created row BEFORE the outbox
    /// rows, and enqueues one allocation event per created row with its ordering timestamp stamped INSIDE this
    /// transaction.</para>
    /// </summary>
    /// <param name="paymentId">The payment to match.</param>
    /// <param name="request">The explicit item list plus the payment's base64 row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>
    /// The created rows plus the payment's and every affected invoice's post-change state, or the first failing
    /// invariant's code.
    /// </returns>
    Task<Result<AllocatePaymentResultDto>> AllocateAsync(
        Guid paymentId,
        AllocatePaymentRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes exactly one allocation row and releases the matched amount (SDD-PAY-002 §2.6). The lookup is
    /// scoped by <c>(PaymentId, Id)</c>, so an allocation owned by a DIFFERENT payment yields
    /// <c>PAYMENT_ALLOCATION_NOT_FOUND</c> — never a cross-payment delete.
    /// <para>Deallocation posts nothing, reverses nothing, and leaves the payment's status untouched; removing a
    /// match is not a ledger event. Neither decrement can drive the payment's allocated amount or the invoice's
    /// settled amount below zero, because every release reverses an amount the same code path added.</para>
    /// </summary>
    /// <param name="paymentId">The owning payment identifier from the route.</param>
    /// <param name="allocationId">The allocation row to release.</param>
    /// <param name="rowVersion">
    /// The OPTIONAL base64 payment row version. When supplied it is applied as the concurrency token; when
    /// omitted the token loaded inside the transaction still guards a concurrent write.
    /// </param>
    /// <param name="reason">An OPTIONAL free-text reason; when supplied it is persisted on the audit row.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The released amount plus the payment's and the invoice's post-release state, or a failure code.</returns>
    Task<Result<DeallocatePaymentResultDto>> DeallocateAsync(
        Guid paymentId,
        int allocationId,
        string? rowVersion,
        string? reason,
        CancellationToken cancellationToken);
}
