using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Interfaces;

/// <summary>
/// Application service for the payment lifecycle (SDD-PAY-001). Every method returns a <see cref="Result"/> /
/// <see cref="Result{T}"/> (SDD-INFRA-009) and threads a <see cref="CancellationToken"/> down to the database,
/// the sequence generator, the period guard, the account reader, and the outbox publish. State transitions go
/// through <see cref="Finance.Common.Workflow.IWorkflowEngine{TAggregate}"/>; confirmed and later payments are
/// immutable and are corrected only by reversal.
/// </summary>
public interface IPaymentService
{
    /// <summary>Lists payments as a filtered, sorted, and paged envelope (SDD-PAY-001 §2.11).</summary>
    /// <param name="request">The filter, sort, and pagination request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A page of <see cref="PaymentDto"/>, default-ordered by payment date descending.</returns>
    Task<Result<PagedResult<PaymentDto>>> SearchAsync(FilterRequest request, CancellationToken cancellationToken);

    /// <summary>Returns a single payment, or <c>PAYMENT_NOT_FOUND</c> (SDD-PAY-001 §2.11).</summary>
    /// <param name="id">The payment identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The matching <see cref="PaymentDto"/>, or a not-found failure.</returns>
    Task<Result<PaymentDto>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a draft payment (SDD-PAY-001 §2.3). The direction is derived from the document type and frozen,
    /// the base currency comes from the country strategy, the base amount is recomputed server-side, and the
    /// document number stays <c>null</c> until confirm. Writes an audit <c>Create</c> row and publishes no
    /// domain event.
    /// </summary>
    /// <param name="request">The create-draft request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The created <see cref="PaymentDto"/>, or a validation/reference-data failure.</returns>
    Task<Result<PaymentDto>> CreateDraftAsync(CreatePaymentRequest request, CancellationToken cancellationToken);

    /// <summary>Updates a draft payment (SDD-PAY-001 §2.6). Only a <c>Draft</c> is editable.</summary>
    /// <param name="id">The payment identifier.</param>
    /// <param name="request">The update request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The updated <see cref="PaymentDto"/>, or a not-found / immutable / concurrency failure.</returns>
    Task<Result<PaymentDto>> UpdateDraftAsync(
        Guid id,
        UpdatePaymentRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hard-deletes a draft payment (SDD-PAY-001 §2.6). A draft carries no gapless number, so nothing is
    /// consumed or released. A non-<c>Draft</c> payment yields <c>PAYMENT_POSTED_IMMUTABLE</c>.
    /// </summary>
    /// <param name="id">The payment identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result, or a not-found / immutable failure.</returns>
    Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Confirms a draft payment (<c>Draft → Confirmed</c>) (SDD-PAY-001 §2.4). Runs the validation, settlement
    /// account, period-open, and document-number-year guards; freezes the base amount; allocates the gapless
    /// country-formatted document number; stamps the confirm audit trail; and enqueues
    /// <c>PaymentConfirmedEvent</c> to the transactional outbox — all in one transaction.
    /// </summary>
    /// <param name="id">The payment identifier.</param>
    /// <param name="request">The confirm request carrying the row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The confirmed <see cref="PaymentDto"/>, or a state / guard / concurrency failure.</returns>
    Task<Result<PaymentDto>> ConfirmAsync(
        Guid id,
        ConfirmPaymentRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Operator-driven post completion (SDD-PAY-001 §2.5). It NEVER posts a journal entry itself. Resolution
    /// order: already <c>Posted</c> ⇒ idempotent success; not <c>Confirmed</c> ⇒ <c>PAYMENT_NOT_CONFIRMED</c>;
    /// <c>Confirmed</c> but unlinked ⇒ re-enqueue <c>PaymentConfirmedEvent</c> (fresh <c>MessageId</c>, the
    /// payment's stored correlation id, no row mutation) then <c>PAYMENT_POSTING_PENDING</c>; <c>Confirmed</c>
    /// AND linked ⇒ the unreachable defense-in-depth branch that period-pre-checks and completes the
    /// transition.
    /// </summary>
    /// <param name="id">The payment identifier.</param>
    /// <param name="request">The post request carrying the row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The posted <see cref="PaymentDto"/>, or a state / period / concurrency failure.</returns>
    Task<Result<PaymentDto>> PostAsync(Guid id, PostPaymentRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels (voids) a <c>Draft</c> payment (SDD-PAY-001 §2.6). Legal from <c>Draft</c> ONLY: once a payment
    /// is <c>Confirmed</c> its posting is irrevocably in flight, so it is completed and then corrected by
    /// reversal. Requires a non-empty reason and rejects an allocated payment.
    /// </summary>
    /// <param name="id">The payment identifier.</param>
    /// <param name="request">The cancel request carrying the mandatory reason and the row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The cancelled <see cref="PaymentDto"/>, or a state / validation / concurrency failure.</returns>
    Task<Result<PaymentDto>> CancelAsync(
        Guid id,
        CancelPaymentRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reverses a <c>Posted</c> payment (SDD-PAY-001 §2.7) — the immutability-preserving correction. Requires
    /// a non-empty reason, rejects an allocated payment, and pre-checks the fiscal period of the linked entry's
    /// date (which equals the payment date by construction). Only the state flag,
    /// <c>ReversedAt</c>, the row version, and the appended history row change; the GL correction is a
    /// sign-flipped entry produced by the Journal service from <c>PaymentReversedEvent</c>.
    /// </summary>
    /// <param name="id">The payment identifier.</param>
    /// <param name="request">The reverse request carrying the mandatory reason and the row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The reversed <see cref="PaymentDto"/>, or a state / period / allocation / concurrency failure.</returns>
    Task<Result<PaymentDto>> ReverseAsync(
        Guid id,
        ReversePaymentRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Links the posted journal entry and transitions the payment <c>Confirmed → Posted</c> in response to the
    /// Journal back-event (SDD-PAY-001 §2.5). A replay against an already-<c>Posted</c> payment is a
    /// <see cref="Result.Success"/> no-op; any other non-<c>Confirmed</c> state yields
    /// <c>PAYMENT_NOT_CONFIRMED</c>. This path deliberately does NOT re-run the period guard. Invoked by the
    /// back-event consumer, never by the controller.
    /// </summary>
    /// <param name="paymentId">The source payment identifier carried on the back-event.</param>
    /// <param name="journalEntryId">The journal entry the posting produced.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result, or a not-found / state failure.</returns>
    Task<Result> LinkPostedJournalEntryAsync(
        Guid paymentId,
        Guid journalEntryId,
        CancellationToken cancellationToken);
}
