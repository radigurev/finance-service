using Finance.Common.Enums;
using Finance.Common.Results;
using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Events.Payments;
using Finance.ServiceModel.Journal;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Finance.Journal.API.Consumers;

/// <summary>
/// MassTransit consumer for <see cref="PaymentReversedEvent"/> (SDD-PAY-001 §2.7, SDD-FIN-002 §2.6). Wrapped by
/// the shared <c>UseFinanceIdempotency()</c> filter (Redis <c>SETNX</c> keyed by <c>MessageId</c>, released on a
/// failed consume per <c>CHG-FIX-006</c>; SDD-INFRA-006). It corrects the general ledger with a sign-flipped new
/// entry through the shipped <see cref="IJournalEntryService.ReverseAsync"/> path — never an UPDATE — and
/// reimplements no reversal arithmetic.
/// <para>The linked entry MUST be READ first: <c>ReverseJournalEntryRequest.RowVersion</c> is a required base64
/// concurrency token the event does not carry, so the consumer loads the entry and forwards its exposed token
/// (§2.7).</para>
/// <para>Aggregate-level idempotency (§2.7, §2.18): a linked entry already in <c>Reversed</c> is a success
/// no-op and is never passed to <see cref="IJournalEntryService.ReverseAsync"/>, so a redelivery past the
/// 7-day dedupe window or a DLQ replay cannot self-renew a dead letter. Any other failure propagates so
/// MassTransit retries and finally dead-letters.</para>
/// </summary>
public sealed class PaymentReversedEventConsumer : IConsumer<PaymentReversedEvent>
{
    private readonly IJournalEntryService _journalEntries;
    private readonly ILogger<PaymentReversedEventConsumer> _logger;

    /// <summary>Creates a new <see cref="PaymentReversedEventConsumer"/>.</summary>
    /// <param name="journalEntries">The journal-entry service owning entry reversal (SDD-FIN-002 §2.6).</param>
    /// <param name="logger">The consumer logger.</param>
    public PaymentReversedEventConsumer(
        IJournalEntryService journalEntries,
        ILogger<PaymentReversedEventConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(journalEntries);
        ArgumentNullException.ThrowIfNull(logger);

        _journalEntries = journalEntries;
        _logger = logger;
    }

    /// <summary>Reverses the journal entry linked to the reversed payment, or skips an already-reversed entry.</summary>
    /// <param name="context">The consume context carrying the reversed-payment event.</param>
    /// <returns>A task that completes when the offsetting entry has been posted or the replay skipped.</returns>
    public async Task Consume(ConsumeContext<PaymentReversedEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PaymentReversedEvent message = context.Message;

        JournalEntryDto linked = await LoadLinkedEntryAsync(message, context.CancellationToken)
            .ConfigureAwait(false);

        if (linked.Status == JournalEntryStatus.Reversed)
        {
            _logger.LogInformation(
                "Journal entry {JournalEntryId} for payment {PaymentId} is already reversed; skipping the replay",
                message.JournalEntryId,
                message.PaymentId);
            return;
        }

        await ReverseLinkedEntryAsync(message, linked, context.CancellationToken).ConfigureAwait(false);
    }

    private async Task<JournalEntryDto> LoadLinkedEntryAsync(
        PaymentReversedEvent message,
        CancellationToken cancellationToken)
    {
        Result<JournalEntryDto> linked = await _journalEntries
            .GetAsync(message.JournalEntryId, cancellationToken)
            .ConfigureAwait(false);

        if (!linked.IsSuccess)
        {
            _logger.LogError(
                "Journal entry {JournalEntryId} linked to reversed payment {PaymentId} could not be loaded. Code={ErrorCode}",
                message.JournalEntryId,
                message.PaymentId,
                linked.ErrorCode);
            throw new InvalidOperationException(
                $"Loading journal entry {message.JournalEntryId} for reversed payment {message.PaymentId} " +
                $"failed with code {linked.ErrorCode}.");
        }

        return linked.Value!;
    }

    private async Task ReverseLinkedEntryAsync(
        PaymentReversedEvent message,
        JournalEntryDto linked,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Reversing journal entry {JournalEntryId} for reversed payment {PaymentId} ({DocumentNumber})",
            message.JournalEntryId,
            message.PaymentId,
            message.DocumentNumber);

        ReverseJournalEntryRequest request = new()
        {
            Reason = message.Reason,
            RowVersion = linked.RowVersion
        };

        Result<JournalEntryDto> reversed = await _journalEntries
            .ReverseAsync(message.JournalEntryId, request, cancellationToken)
            .ConfigureAwait(false);

        if (!reversed.IsSuccess)
        {
            _logger.LogError(
                "Reversing journal entry {JournalEntryId} for payment {PaymentId} failed. Code={ErrorCode}",
                message.JournalEntryId,
                message.PaymentId,
                reversed.ErrorCode);
            throw new InvalidOperationException(
                $"Reversing journal entry {message.JournalEntryId} for payment {message.PaymentId} " +
                $"failed with code {reversed.ErrorCode}.");
        }
    }
}
