using Finance.Common.Enums;
using Finance.Common.Results;
using Finance.Country.Abstractions;
using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Events.Invoices;
using Finance.ServiceModel.Journal;
using Finance.ServiceModel.Posting;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Finance.Journal.API.Consumers;

/// <summary>
/// MassTransit consumer for <see cref="InvoiceConfirmedEvent"/> (SDD-INV-001 §2.5, SDD-FIN-006). Wrapped by
/// the shared <c>UseFinanceIdempotency()</c> filter (Redis <c>SETNX</c> keyed by <c>MessageId</c>,
/// SDD-INFRA-006) so replays never double-post. It maps the invoice document type to a posting-rule key,
/// applies the rule via <see cref="IPostingEngine.ApplyAsync"/> with the net/tax/gross amounts, and on
/// success publishes the dedicated back-event <see cref="InvoicePostedEvent"/> through the Journal
/// transactional outbox so the Invoice service can link the journal entry and move to <c>Posted</c>. A
/// posting failure propagates so MassTransit retries / dead-letters.
/// </summary>
public sealed class InvoiceConfirmedEventConsumer : IConsumer<InvoiceConfirmedEvent>
{
    private const string SaleInvoiceRuleKey = "SALE_INVOICE";
    private const string PurchaseInvoiceRuleKey = "PURCHASE_INVOICE";
    private const string CreditNoteRuleKey = "CREDIT_NOTE";
    private const string DebitNoteRuleKey = "DEBIT_NOTE";

    private readonly IPostingEngine _postingEngine;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<InvoiceConfirmedEventConsumer> _logger;

    /// <summary>Creates a new <see cref="InvoiceConfirmedEventConsumer"/>.</summary>
    /// <param name="postingEngine">The posting engine that materializes and posts the journal entry.</param>
    /// <param name="publishEndpoint">The transactional-outbox publish endpoint for the back-event.</param>
    /// <param name="logger">The consumer logger.</param>
    public InvoiceConfirmedEventConsumer(
        IPostingEngine postingEngine,
        IPublishEndpoint publishEndpoint,
        ILogger<InvoiceConfirmedEventConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(postingEngine);
        ArgumentNullException.ThrowIfNull(publishEndpoint);
        ArgumentNullException.ThrowIfNull(logger);

        _postingEngine = postingEngine;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    /// <summary>
    /// Posts the journal entry for the confirmed invoice and publishes the back-event on success.
    /// </summary>
    /// <param name="context">The consume context carrying the confirmed-invoice event.</param>
    /// <returns>A task that completes when the entry has been posted and the back-event enqueued.</returns>
    public async Task Consume(ConsumeContext<InvoiceConfirmedEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        InvoiceConfirmedEvent message = context.Message;

        _logger.LogInformation(
            "Posting journal entry for confirmed invoice {InvoiceId} ({DocumentNumber}) via rule {PostingRuleKey}",
            message.InvoiceId,
            message.DocumentNumber,
            message.PostingRuleKey);

        ApplyPostingRuleRequest request = BuildRequest(message);
        Result<JournalEntryDto> posted =
            await _postingEngine.ApplyAsync(request, context.CancellationToken).ConfigureAwait(false);

        if (!posted.IsSuccess)
        {
            _logger.LogError(
                "Posting failed for invoice {InvoiceId}. Code={ErrorCode}",
                message.InvoiceId,
                posted.ErrorCode);
            throw new InvalidOperationException(
                $"Posting the journal entry for invoice {message.InvoiceId} failed with code {posted.ErrorCode}.");
        }

        await _publishEndpoint
            .Publish(BuildPostedEvent(message, posted.Value!), context.CancellationToken)
            .ConfigureAwait(false);
    }

    private static ApplyPostingRuleRequest BuildRequest(InvoiceConfirmedEvent message) => new()
    {
        RuleKey = ResolveRuleKey(message),
        Amounts = new Dictionary<PostingAmountSource, decimal>
        {
            [PostingAmountSource.Net] = message.NetTotal,
            [PostingAmountSource.Tax] = message.TaxTotal,
            [PostingAmountSource.Gross] = message.GrossTotal
        },
        CurrencyCode = message.CurrencyCode,
        EntryDate = message.IssueDate,
        Description = $"Invoice {message.DocumentNumber}",
        PostImmediately = true
    };

    private static string ResolveRuleKey(InvoiceConfirmedEvent message)
    {
        if (!string.IsNullOrWhiteSpace(message.PostingRuleKey))
        {
            return message.PostingRuleKey;
        }

        return message.DocumentType switch
        {
            InvoiceDocumentType.SaleInvoice => SaleInvoiceRuleKey,
            InvoiceDocumentType.PurchaseInvoice => PurchaseInvoiceRuleKey,
            InvoiceDocumentType.CreditNote => CreditNoteRuleKey,
            InvoiceDocumentType.DebitNote => DebitNoteRuleKey,
            _ => throw new ArgumentOutOfRangeException(nameof(message), message.DocumentType, null)
        };
    }

    private static InvoicePostedEvent BuildPostedEvent(InvoiceConfirmedEvent source, JournalEntryDto entry) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = source.CorrelationId,
        OccurredAt = DateTimeOffset.UtcNow,
        InvoiceId = source.InvoiceId,
        JournalEntryId = entry.Id,
        JournalEntryNumber = entry.EntryNumber ?? string.Empty
    };
}
