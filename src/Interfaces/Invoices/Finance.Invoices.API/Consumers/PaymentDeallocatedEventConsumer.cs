using Finance.Common.Results;
using Finance.Invoices.API.Interfaces;
using Finance.ServiceModel.Events.Payments;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Finance.Invoices.API.Consumers;

/// <summary>
/// MassTransit consumer applying SDD-PAY-002's <see cref="PaymentDeallocatedEvent"/> to the invoice-side
/// settlement mirror (SDD-INV-001 §2.15). Wrapped transparently by the shared <c>UseFinanceIdempotency()</c>
/// filter (SDD-INFRA-006); it hand-rolls no dedupe logic.
/// <para>The release is applied by ABSOLUTE assignment of
/// <see cref="PaymentDeallocatedEvent.InvoiceSettledAmount"/>, so a replay cannot release the same cash twice,
/// and the ordering token prevents a stale release from restoring an older settled amount.</para>
/// <para>The update is applied WHATEVER the invoice's lifecycle state, <c>Cancelled</c> and <c>Reversed</c>
/// included: the orphan-repair release that follows a cancel which won the race against an in-flight allocation
/// carries <c>0.00</c> and MUST land, so the mirror stops claiming cash the Payments service has released while
/// the lifecycle status is left untouched.</para>
/// </summary>
public sealed class PaymentDeallocatedEventConsumer : IConsumer<PaymentDeallocatedEvent>
{
    private readonly IInvoiceSettlementService _settlement;
    private readonly ILogger<PaymentDeallocatedEventConsumer> _logger;

    /// <summary>Creates a new <see cref="PaymentDeallocatedEventConsumer"/>.</summary>
    /// <param name="settlement">The invoice-side settlement mirror writer.</param>
    /// <param name="logger">The consumer logger.</param>
    public PaymentDeallocatedEventConsumer(
        IInvoiceSettlementService settlement,
        ILogger<PaymentDeallocatedEventConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(logger);

        _settlement = settlement;
        _logger = logger;
    }

    /// <summary>Releases the matched amount from the invoice's settlement mirror.</summary>
    /// <param name="context">The consume context carrying the deallocation event.</param>
    /// <returns>A task that completes when the update has been applied or dropped as stale.</returns>
    public async Task Consume(ConsumeContext<PaymentDeallocatedEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PaymentDeallocatedEvent message = context.Message;

        _logger.LogInformation(
            "Releasing {ReleasedAmount} from payment {PaymentId} against invoice {InvoiceId}: authoritative "
            + "settled amount {InvoiceSettledAmount}",
            message.ReleasedAmount,
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
            SourceEvent = nameof(PaymentDeallocatedEvent)
        };

        Result result = await _settlement
            .ApplyAsync(update, context.CancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Failed to apply {SourceEvent} to invoice {InvoiceId}. Code={ErrorCode}",
                nameof(PaymentDeallocatedEvent),
                message.InvoiceId,
                result.ErrorCode);
            throw new InvalidOperationException(
                $"Applying {nameof(PaymentDeallocatedEvent)} to invoice {message.InvoiceId} failed with code "
                + $"{result.ErrorCode}.");
        }
    }
}
