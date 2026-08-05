using Finance.Common.Enums;
using Finance.Common.Results;
using Finance.Country.Abstractions;
using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Events.Payments;
using Finance.ServiceModel.Journal;
using Finance.ServiceModel.Posting;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Finance.Journal.API.Consumers;

/// <summary>
/// MassTransit consumer for <see cref="PaymentConfirmedEvent"/> (SDD-PAY-001 §2.5, SDD-FIN-006). Wrapped by
/// the shared <c>UseFinanceIdempotency()</c> filter (Redis <c>SETNX</c> keyed by <c>MessageId</c>, released on
/// a failed consume per <c>CHG-FIX-006</c>; SDD-INFRA-006). It applies the payment's posting rule via
/// <see cref="IPostingEngine.ApplyAsync"/> and publishes the dedicated back-event
/// <see cref="PaymentPostedEvent"/> through the Journal transactional outbox so the Payments service can link
/// the entry and move <c>Confirmed → Posted</c>. A posting failure propagates so MassTransit retries and
/// finally dead-letters.
/// <para>The TRANSACTIONAL <see cref="PaymentConfirmedEvent.Amount"/> is posted in
/// <see cref="PaymentConfirmedEvent.CurrencyCode"/> — never the base amount — so the payment leg of a control
/// account is booked in the same currency as the invoice leg (§2.5).</para>
/// <para>Aggregate-level duplicate-post guard (§2.5): every entry it creates is stamped with the
/// <c>("Payment", PaymentId)</c> source-document pair, and a redelivery past the dedupe window or a DLQ replay
/// whose entry is already <c>Posted</c> posts NOTHING and merely re-publishes the back-event for the existing
/// entry — which is what unsticks a payment whose back-event was lost.</para>
/// </summary>
public sealed class PaymentConfirmedEventConsumer : IConsumer<PaymentConfirmedEvent>
{
    private const string CustomerReceiptRuleKey = "PAYMENT_CUSTOMER_RECEIPT";
    private const string SupplierPaymentRuleKey = "PAYMENT_SUPPLIER_PAYMENT";

    private readonly IJournalEntryService _journalEntries;
    private readonly IPostingEngine _postingEngine;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<PaymentConfirmedEventConsumer> _logger;

    /// <summary>Creates a new <see cref="PaymentConfirmedEventConsumer"/>.</summary>
    /// <param name="journalEntries">The journal-entry service supplying the source-document dedupe lookup.</param>
    /// <param name="postingEngine">The posting engine that materializes and posts the journal entry.</param>
    /// <param name="publishEndpoint">The transactional-outbox publish endpoint for the back-event.</param>
    /// <param name="logger">The consumer logger.</param>
    public PaymentConfirmedEventConsumer(
        IJournalEntryService journalEntries,
        IPostingEngine postingEngine,
        IPublishEndpoint publishEndpoint,
        ILogger<PaymentConfirmedEventConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(journalEntries);
        ArgumentNullException.ThrowIfNull(postingEngine);
        ArgumentNullException.ThrowIfNull(publishEndpoint);
        ArgumentNullException.ThrowIfNull(logger);

        _journalEntries = journalEntries;
        _postingEngine = postingEngine;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    /// <summary>
    /// Posts the journal entry for the confirmed payment and publishes the back-event on success, or
    /// re-publishes the back-event alone when an entry is already posted for the payment.
    /// </summary>
    /// <param name="context">The consume context carrying the confirmed-payment event.</param>
    /// <returns>A task that completes when the entry has been posted and the back-event enqueued.</returns>
    public async Task Consume(ConsumeContext<PaymentConfirmedEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PaymentConfirmedEvent message = context.Message;

        JournalEntryDto? alreadyPosted = await _journalEntries
            .FindPostedBySourceDocumentAsync(
                JournalSourceDocumentTypes.Payment, message.PaymentId, context.CancellationToken)
            .ConfigureAwait(false);
        if (alreadyPosted is not null)
        {
            await RepublishForExistingEntryAsync(message, alreadyPosted, context.CancellationToken)
                .ConfigureAwait(false);
            return;
        }

        _logger.LogInformation(
            "Posting journal entry for confirmed payment {PaymentId} ({DocumentNumber}) via rule {PostingRuleKey}",
            message.PaymentId,
            message.DocumentNumber,
            message.PostingRuleKey);

        ApplyPostingRuleRequest request = BuildRequest(message);
        Result<JournalEntryDto> posted =
            await _postingEngine.ApplyAsync(request, context.CancellationToken).ConfigureAwait(false);

        if (!posted.IsSuccess)
        {
            _logger.LogError(
                "Posting failed for payment {PaymentId}. Code={ErrorCode}",
                message.PaymentId,
                posted.ErrorCode);
            throw new InvalidOperationException(
                $"Posting the journal entry for payment {message.PaymentId} failed with code {posted.ErrorCode}.");
        }

        await _publishEndpoint
            .Publish(BuildPostedEvent(message, posted.Value!), context.CancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RepublishForExistingEntryAsync(
        PaymentConfirmedEvent message,
        JournalEntryDto existing,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Journal entry {JournalEntryId} is already posted for payment {PaymentId}; re-publishing the back-event only",
            existing.Id,
            message.PaymentId);

        await _publishEndpoint
            .Publish(BuildPostedEvent(message, existing), cancellationToken)
            .ConfigureAwait(false);
    }

    private static ApplyPostingRuleRequest BuildRequest(PaymentConfirmedEvent message) => new()
    {
        RuleKey = ResolveRuleKey(message),
        Amounts = new Dictionary<PostingAmountSource, decimal>
        {
            [PostingAmountSource.Gross] = message.Amount
        },
        CurrencyCode = message.CurrencyCode,
        EntryDate = message.PaymentDate,
        Description = $"Payment {message.DocumentNumber}",
        PostImmediately = true,
        SourceDocumentType = JournalSourceDocumentTypes.Payment,
        SourceDocumentId = message.PaymentId
    };

    private static string ResolveRuleKey(PaymentConfirmedEvent message)
    {
        if (!string.IsNullOrWhiteSpace(message.PostingRuleKey))
        {
            return message.PostingRuleKey;
        }

        return message.DocumentType switch
        {
            PaymentDocumentType.CustomerReceipt => CustomerReceiptRuleKey,
            PaymentDocumentType.SupplierPayment => SupplierPaymentRuleKey,
            _ => throw new ArgumentOutOfRangeException(nameof(message), message.DocumentType, null)
        };
    }

    private static PaymentPostedEvent BuildPostedEvent(PaymentConfirmedEvent source, JournalEntryDto entry) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = source.CorrelationId,
        OccurredAt = DateTimeOffset.UtcNow,
        PaymentId = source.PaymentId,
        JournalEntryId = entry.Id,
        JournalEntryNumber = entry.EntryNumber ?? string.Empty
    };
}
