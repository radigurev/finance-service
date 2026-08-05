using Finance.Common.Results;
using Finance.Invoices.API.Interfaces;
using Finance.ServiceModel.Events.Payments;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Finance.Invoices.API.Consumers;

/// <summary>
/// MassTransit consumer applying SDD-PAY-002's <see cref="PaymentAllocatedEvent"/> to the invoice-side
/// settlement mirror (SDD-INV-001 §2.15). Wrapped transparently by the shared <c>UseFinanceIdempotency()</c>
/// filter (Redis <c>SETNX</c> keyed by <c>MessageId</c>, 7-day TTL — SDD-INFRA-006), so it hand-rolls no dedupe
/// logic of its own.
/// <para>Beyond that dedupe the mirror is safe twice over: the settled amount is ASSIGNED from the event's
/// authoritative <see cref="PaymentAllocatedEvent.InvoiceSettledAmount"/> (never incremented, so a post-TTL
/// replay cannot double-count cash), and the event's <c>OccurredAt</c> is compared against the invoice's
/// ordering token so a strictly older event is dropped instead of regressing the newer total.</para>
/// <para>The handshake is ONE-WAY: no back-event is published and the Payments service is never called. A
/// genuine failure — an unknown invoice, or an amount that breaches the gross-total ceiling — is rethrown so
/// MassTransit retries (1s/5s/15s) and finally dead-letters, rather than clamping a ledger figure.</para>
/// </summary>
public sealed class PaymentAllocatedEventConsumer : IConsumer<PaymentAllocatedEvent>
{
    private readonly IInvoiceSettlementService _settlement;
    private readonly ILogger<PaymentAllocatedEventConsumer> _logger;

    /// <summary>Creates a new <see cref="PaymentAllocatedEventConsumer"/>.</summary>
    /// <param name="settlement">The invoice-side settlement mirror writer.</param>
    /// <param name="logger">The consumer logger.</param>
    public PaymentAllocatedEventConsumer(
        IInvoiceSettlementService settlement,
        ILogger<PaymentAllocatedEventConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(logger);

        _settlement = settlement;
        _logger = logger;
    }

    /// <summary>Applies the allocated amount to the invoice's settlement mirror.</summary>
    /// <param name="context">The consume context carrying the allocation event.</param>
    /// <returns>A task that completes when the update has been applied or dropped as stale.</returns>
    public async Task Consume(ConsumeContext<PaymentAllocatedEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PaymentAllocatedEvent message = context.Message;

        _logger.LogInformation(
            "Applying allocation of {AllocatedAmount} from payment {PaymentId} to invoice {InvoiceId}: "
            + "authoritative settled amount {InvoiceSettledAmount}",
            message.AllocatedAmount,
            message.PaymentId,
            message.InvoiceId,
            message.InvoiceSettledAmount);

        InvoiceSettlementUpdate update = new()
        {
            InvoiceId = message.InvoiceId,
            SettledAmount = message.InvoiceSettledAmount,
            ReportedStatus = message.InvoiceSettlementStatus,
            OccurredAt = message.OccurredAt,
            CorrelationId = message.CorrelationId,
            SourceEvent = nameof(PaymentAllocatedEvent)
        };

        Result result = await _settlement
            .ApplyAsync(update, context.CancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Failed to apply {SourceEvent} to invoice {InvoiceId}. Code={ErrorCode}",
                nameof(PaymentAllocatedEvent),
                message.InvoiceId,
                result.ErrorCode);
            throw new InvalidOperationException(
                $"Applying {nameof(PaymentAllocatedEvent)} to invoice {message.InvoiceId} failed with code "
                + $"{result.ErrorCode}.");
        }
    }
}
