using Finance.Common.Results;
using Finance.Payments.API.Interfaces;
using Finance.ServiceModel.Events.Invoices;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Finance.Payments.API.Consumers;

/// <summary>
/// MassTransit consumer mirroring <see cref="InvoicePostedEvent"/> onto the LOCAL invoice open-item projection
/// (SDD-PAY-002 §2.3). Wrapped transparently by the shared <c>UseFinanceIdempotency()</c> filter
/// (SDD-INFRA-006).
/// <para>It NEVER creates a row: the event carries only identifiers, which is not enough to build a valid open
/// item. A MISSING row is rethrown so MassTransit retries (1s/5s/15s) and finally dead-letters — that is
/// precisely what makes the out-of-order posted-before-confirmed pair converge, and it MUST NOT be downgraded to
/// a silent success. The retry-then-dead-letter contract holds only because CHG-FIX-006 makes the idempotency
/// filter release its Redis claim on a failed consume.</para>
/// <para>Because the event carries no document type, this consumer cannot tell a deliberately skipped invoice
/// from a confirmation that has not landed yet. A posted non-settleable invoice therefore dead-letters: that is
/// EXPECTED, lossless noise — the projection is the allocation and aging surface and such a document belongs to
/// neither — and MUST NOT be triaged as projection drift.</para>
/// </summary>
public sealed class InvoicePostedEventConsumer : IConsumer<InvoicePostedEvent>
{
    private readonly IInvoiceOpenItemProjection _projection;
    private readonly ILogger<InvoicePostedEventConsumer> _logger;

    /// <summary>Creates a new <see cref="InvoicePostedEventConsumer"/>.</summary>
    /// <param name="projection">The local open-item projection writer.</param>
    /// <param name="logger">The consumer logger.</param>
    public InvoicePostedEventConsumer(
        IInvoiceOpenItemProjection projection,
        ILogger<InvoicePostedEventConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(logger);

        _projection = projection;
        _logger = logger;
    }

    /// <summary>Mirrors the posted status onto the existing open item.</summary>
    /// <param name="context">The consume context carrying the posting back-event.</param>
    /// <returns>A task that completes when the status has been mirrored.</returns>
    public async Task Consume(ConsumeContext<InvoicePostedEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        InvoicePostedEvent message = context.Message;

        _logger.LogInformation(
            "Mirroring posted status of invoice {InvoiceId} (journal entry {JournalEntryNumber}) onto the "
            + "open-item projection",
            message.InvoiceId,
            message.JournalEntryNumber);

        Result result = await _projection
            .ApplyPostedAsync(message.InvoiceId, context.CancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Failed to mirror posted status of invoice {InvoiceId}. Code={ErrorCode}",
                message.InvoiceId,
                result.ErrorCode);
            throw new InvalidOperationException(
                $"Mirroring the posted status of invoice {message.InvoiceId} failed with code {result.ErrorCode}.");
        }
    }
}
