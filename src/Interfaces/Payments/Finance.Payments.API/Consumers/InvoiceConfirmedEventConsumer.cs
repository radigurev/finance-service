using Finance.Common.Results;
using Finance.Payments.API.Interfaces;
using Finance.ServiceModel.Events.Invoices;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Finance.Payments.API.Consumers;

/// <summary>
/// MassTransit consumer feeding the LOCAL invoice open-item projection from
/// <see cref="InvoiceConfirmedEvent"/> (SDD-PAY-002 §2.3). Wrapped transparently by the shared
/// <c>UseFinanceIdempotency()</c> filter (Redis <c>SETNX</c> keyed by <c>MessageId</c>, 7-day TTL, released on a
/// failed consume per CHG-FIX-006), so no dedupe logic is hand-rolled here.
/// <para>Beyond that transport dedupe the apply is a convergent UPSERT keyed by the invoice identifier: a
/// duplicate delivery is a no-op, a late confirmation never downgrades an already-posted row, and a terminal row
/// is never resurrected. An invoice whose document type no payment can settle is a SILENT SUCCESS with no row —
/// which is what keeps a credit note out of the allocation and aging surface instead of ageing it as a phantom
/// balance forever.</para>
/// </summary>
public sealed class InvoiceConfirmedEventConsumer : IConsumer<InvoiceConfirmedEvent>
{
    private readonly IInvoiceOpenItemProjection _projection;
    private readonly ILogger<InvoiceConfirmedEventConsumer> _logger;

    /// <summary>Creates a new <see cref="InvoiceConfirmedEventConsumer"/>.</summary>
    /// <param name="projection">The local open-item projection writer.</param>
    /// <param name="logger">The consumer logger.</param>
    public InvoiceConfirmedEventConsumer(
        IInvoiceOpenItemProjection projection,
        ILogger<InvoiceConfirmedEventConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(logger);

        _projection = projection;
        _logger = logger;
    }

    /// <summary>Applies the confirmation to the local open-item projection.</summary>
    /// <param name="context">The consume context carrying the confirmation event.</param>
    /// <returns>A task that completes when the projection has been applied or the invoice deliberately skipped.</returns>
    public async Task Consume(ConsumeContext<InvoiceConfirmedEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        InvoiceConfirmedEvent message = context.Message;

        _logger.LogInformation(
            "Applying confirmation of invoice {InvoiceId} ({DocumentNumber}) to the open-item projection",
            message.InvoiceId,
            message.DocumentNumber);

        Result result = await _projection
            .ApplyConfirmedAsync(message, context.CancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Failed to project confirmation of invoice {InvoiceId}. Code={ErrorCode}",
                message.InvoiceId,
                result.ErrorCode);
            throw new InvalidOperationException(
                $"Projecting the confirmation of invoice {message.InvoiceId} failed with code {result.ErrorCode}.");
        }
    }
}
