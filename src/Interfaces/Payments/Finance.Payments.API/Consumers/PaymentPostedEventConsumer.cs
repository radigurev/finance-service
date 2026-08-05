using Finance.Common.Results;
using Finance.Payments.API.Interfaces;
using Finance.ServiceModel.Events.Payments;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Finance.Payments.API.Consumers;

/// <summary>
/// MassTransit consumer for the Journal back-event <see cref="PaymentPostedEvent"/> (SDD-PAY-001 §2.5).
/// Wrapped by the shared <c>UseFinanceIdempotency()</c> filter (Redis <c>SETNX</c> keyed by <c>MessageId</c>,
/// 7-day TTL, released on a failed consume per <c>CHG-FIX-006</c>; SDD-INFRA-006), so a fast retry of the same
/// message never double-links. It matches the event to the source payment by
/// <see cref="PaymentPostedEvent.PaymentId"/>, links the journal entry, and transitions the payment
/// <c>Confirmed → Posted</c> in one transaction.
/// <para>Aggregate-level idempotency lives in <c>LinkPostedJournalEntryAsync</c>: a replay against an
/// already-<c>Posted</c> payment is a success no-op, so a redelivery past the dedupe window or a DLQ replay is
/// safe. A genuine failure (unknown payment, illegal source state) is rethrown so MassTransit retries
/// (1s/5s/15s) and finally dead-letters for operator attention — the payment MUST NOT be silently mutated into
/// <c>Posted</c> from an illegal state.</para>
/// </summary>
public sealed class PaymentPostedEventConsumer : IConsumer<PaymentPostedEvent>
{
    private readonly IPaymentService _payments;
    private readonly ILogger<PaymentPostedEventConsumer> _logger;

    /// <summary>Creates a new <see cref="PaymentPostedEventConsumer"/>.</summary>
    /// <param name="payments">The payment application service.</param>
    /// <param name="logger">The consumer logger.</param>
    public PaymentPostedEventConsumer(
        IPaymentService payments,
        ILogger<PaymentPostedEventConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(logger);

        _payments = payments;
        _logger = logger;
    }

    /// <summary>Links the posted journal entry to the source payment and moves it to <c>Posted</c>.</summary>
    /// <param name="context">The consume context carrying the back-event.</param>
    /// <returns>A task that completes when the link has been applied or the replay skipped.</returns>
    public async Task Consume(ConsumeContext<PaymentPostedEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PaymentPostedEvent message = context.Message;

        _logger.LogInformation(
            "Linking journal entry {JournalEntryId} ({JournalEntryNumber}) to payment {PaymentId}",
            message.JournalEntryId,
            message.JournalEntryNumber,
            message.PaymentId);

        Result result = await _payments
            .LinkPostedJournalEntryAsync(message.PaymentId, message.JournalEntryId, context.CancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Failed to link journal entry to payment {PaymentId}. Code={ErrorCode}",
                message.PaymentId,
                result.ErrorCode);
            throw new InvalidOperationException(
                $"Linking journal entry to payment {message.PaymentId} failed with code {result.ErrorCode}.");
        }
    }
}
