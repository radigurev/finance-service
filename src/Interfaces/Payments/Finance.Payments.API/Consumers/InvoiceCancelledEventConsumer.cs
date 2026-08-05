using Finance.Common.Results;
using Finance.Payments.API.Interfaces;
using Finance.ServiceModel.Events.Invoices;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Finance.Payments.API.Consumers;

/// <summary>
/// MassTransit consumer mirroring <see cref="InvoiceCancelledEvent"/> onto the LOCAL invoice open-item
/// projection (SDD-PAY-002 §2.3). Wrapped transparently by the shared <c>UseFinanceIdempotency()</c> filter
/// (SDD-INFRA-006).
/// <para>An UNKNOWN invoice TOMBSTONES a <c>Cancelled</c> row rather than no-opping or throwing. It is the ONE
/// deliberate exception to the "missing row means throw for retry" rule the posting and reversal consumers
/// follow, because a DRAFT cancellation publishes this event too and a draft never enters the projection —
/// retrying could only dead-letter every draft cancel. A plain no-op is equally unsafe: a cancellation landing in
/// the gap between a failed confirmation and its retry would let the retry insert the row as <c>Confirmed</c>,
/// leaving a cancelled invoice permanently allocatable and aged in every bucket.</para>
/// <para>Existing allocation rows are kept and never auto-released, so history stays auditable; when the row
/// already existed the projection also raises the ORPHANED-SETTLEMENT warning that detects a cancel which raced
/// an in-flight allocation.</para>
/// </summary>
public sealed class InvoiceCancelledEventConsumer : IConsumer<InvoiceCancelledEvent>
{
    private readonly IInvoiceOpenItemProjection _projection;
    private readonly ILogger<InvoiceCancelledEventConsumer> _logger;

    /// <summary>Creates a new <see cref="InvoiceCancelledEventConsumer"/>.</summary>
    /// <param name="projection">The local open-item projection writer.</param>
    /// <param name="logger">The consumer logger.</param>
    public InvoiceCancelledEventConsumer(
        IInvoiceOpenItemProjection projection,
        ILogger<InvoiceCancelledEventConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(logger);

        _projection = projection;
        _logger = logger;
    }

    /// <summary>Mirrors the cancellation onto the open item, tombstoning one when none exists.</summary>
    /// <param name="context">The consume context carrying the cancellation event.</param>
    /// <returns>A task that completes when the cancellation has been mirrored or tombstoned.</returns>
    public async Task Consume(ConsumeContext<InvoiceCancelledEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        InvoiceCancelledEvent message = context.Message;

        _logger.LogInformation(
            "Mirroring cancellation of invoice {InvoiceId} onto the open-item projection",
            message.InvoiceId);

        Result result = await _projection
            .ApplyCancelledAsync(message, context.CancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Failed to mirror cancellation of invoice {InvoiceId}. Code={ErrorCode}",
                message.InvoiceId,
                result.ErrorCode);
            throw new InvalidOperationException(
                $"Mirroring the cancellation of invoice {message.InvoiceId} failed with code {result.ErrorCode}.");
        }
    }
}
