using Finance.Common.Results;
using Finance.Invoices.API.Interfaces;
using Finance.ServiceModel.Events.Invoices;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Finance.Invoices.API.Consumers;

/// <summary>
/// MassTransit consumer for the Journal back-event <see cref="InvoicePostedEvent"/> (SDD-INV-001 §2.5).
/// Wrapped by the shared <c>UseFinanceIdempotency()</c> filter (Redis <c>SETNX</c> keyed by
/// <c>MessageId</c>, SDD-INFRA-006) so replays never double-link. It matches the event to the source invoice
/// by <see cref="InvoicePostedEvent.InvoiceId"/>, links the journal entry, and transitions the invoice
/// <c>Confirmed → Posted</c> in one transaction. A replay against an already-<c>Posted</c> invoice is a
/// no-op. A genuine failure (e.g. invoice not found) propagates so MassTransit retries / dead-letters.
/// </summary>
public sealed class InvoicePostedEventConsumer : IConsumer<InvoicePostedEvent>
{
    private readonly IInvoiceService _invoices;
    private readonly ILogger<InvoicePostedEventConsumer> _logger;

    /// <summary>Creates a new <see cref="InvoicePostedEventConsumer"/>.</summary>
    /// <param name="invoices">The invoice application service.</param>
    /// <param name="logger">The consumer logger.</param>
    public InvoicePostedEventConsumer(
        IInvoiceService invoices,
        ILogger<InvoicePostedEventConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(logger);

        _invoices = invoices;
        _logger = logger;
    }

    /// <summary>
    /// Links the posted journal entry to the source invoice and moves it to <c>Posted</c>.
    /// </summary>
    /// <param name="context">The consume context carrying the back-event.</param>
    /// <returns>A task that completes when the link has been applied or the replay skipped.</returns>
    public async Task Consume(ConsumeContext<InvoicePostedEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        InvoicePostedEvent message = context.Message;

        _logger.LogInformation(
            "Linking journal entry {JournalEntryId} ({JournalEntryNumber}) to invoice {InvoiceId}",
            message.JournalEntryId,
            message.JournalEntryNumber,
            message.InvoiceId);

        Result result = await _invoices
            .LinkPostedJournalEntryAsync(message.InvoiceId, message.JournalEntryId, context.CancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Failed to link journal entry to invoice {InvoiceId}. Code={ErrorCode}",
                message.InvoiceId,
                result.ErrorCode);
            throw new InvalidOperationException(
                $"Linking journal entry to invoice {message.InvoiceId} failed with code {result.ErrorCode}.");
        }
    }
}
