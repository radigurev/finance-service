using Finance.Common.Enums;
using Finance.Invoices.API.Interfaces;
using Finance.ServiceModel.Integration.Warehouse.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Finance.Invoices.API.Consumers;

/// <summary>
/// Shared base for the four Warehouse inbound consumers (SDD-INT-WH-001 §2.1-§2.4). It pushes the inbound
/// <see cref="IWarehouseDocumentEvent.CorrelationId"/> onto the NLog scope (SDD-OBS-001), logs entry/exit
/// with structured templates, delegates map-and-create to <see cref="IWarehouseInvoiceDraftFactory"/> (the
/// SAME SDD-INV-001 create path), and applies the failure policy: a permanent business failure is logged at
/// error and acknowledged (returns normally — NOT thrown — so the queue is not poisoned), while a transient
/// infrastructure failure propagates as an exception from the factory so MassTransit retries / dead-letters.
/// Idempotency on <c>MessageId</c> is handled transparently upstream by <c>UseFinanceIdempotency()</c>
/// (SDD-INFRA-006); the source-document dedupe is the backstop inside the factory. Each concrete consumer is
/// a thin one-class-per-file subclass that supplies the document type and source-document tag.
/// </summary>
/// <typeparam name="TEvent">The Warehouse event contract this consumer turns into a draft invoice.</typeparam>
public abstract class WarehouseInvoiceConsumerBase<TEvent> : IConsumer<TEvent>
    where TEvent : class, IWarehouseDocumentEvent
{
    private readonly IWarehouseInvoiceDraftFactory _factory;
    private readonly ILogger _logger;

    /// <summary>Initializes the consumer with its draft factory and logger.</summary>
    /// <param name="factory">The shared map-and-create factory.</param>
    /// <param name="logger">The logger used for the structured entry/exit messages.</param>
    protected WarehouseInvoiceConsumerBase(IWarehouseInvoiceDraftFactory factory, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(logger);

        _factory = factory;
        _logger = logger;
    }

    /// <summary>The invoice document type this consumer creates (SDD-INT-WH-001 §2.2).</summary>
    protected abstract InvoiceDocumentType DocumentType { get; }

    /// <summary>The source-document type tag persisted on the draft (SDD-INT-WH-001 §2.2).</summary>
    protected abstract string SourceDocumentType { get; }

    /// <summary>
    /// Maps the consumed event to a draft invoice via the shared create path, scoping the log to the
    /// inbound correlation id and applying the §2.4 permanent-vs-transient failure policy.
    /// </summary>
    /// <param name="context">The MassTransit consume context carrying the event.</param>
    /// <returns>A task that completes when the draft is created, deduped, or the failure acknowledged.</returns>
    public async Task Consume(ConsumeContext<TEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TEvent message = context.Message;

        using (_logger.BeginScope(BuildLogScope(message.CorrelationId)))
        {
            _logger.LogInformation(
                "Consuming {EventType} for source document {SourceDocumentId} ({SourceDocumentType})",
                typeof(TEvent).Name,
                message.SourceDocumentId,
                SourceDocumentType);

            Guid? correctsInvoiceId = await ResolveCorrectsInvoiceIdAsync(message, context.CancellationToken)
                .ConfigureAwait(false);

            WarehouseDraftOutcome outcome = await _factory
                .CreateDraftAsync(
                    message,
                    DocumentType,
                    SourceDocumentType,
                    correctsInvoiceId,
                    context.CancellationToken)
                .ConfigureAwait(false);

            HandleOutcome(message, outcome);
        }
    }

    /// <summary>
    /// Resolves the original invoice this draft corrects, when the event references one (SDD-INT-WH-001 §2.2,
    /// §2.6). The base returns <c>null</c>; the customer-return consumer overrides it to link a matching
    /// Finance sale invoice when one exists (standalone otherwise — the consumer MUST NOT fail when no match).
    /// </summary>
    /// <param name="message">The inbound event.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The corrected invoice id, or <c>null</c> for a standalone draft.</returns>
    protected virtual Task<Guid?> ResolveCorrectsInvoiceIdAsync(TEvent message, CancellationToken cancellationToken) =>
        Task.FromResult<Guid?>(null);

    private void HandleOutcome(TEvent message, WarehouseDraftOutcome outcome)
    {
        switch (outcome.Kind)
        {
            case WarehouseDraftOutcomeKind.Created:
                _logger.LogInformation(
                    "Created draft invoice {InvoiceId} from source document {SourceDocumentId} ({SourceDocumentType})",
                    outcome.Invoice!.Id,
                    message.SourceDocumentId,
                    SourceDocumentType);
                break;
            case WarehouseDraftOutcomeKind.AlreadyExists:
                _logger.LogInformation(
                    "Draft invoice {InvoiceId} already exists for source document {SourceDocumentId} ({SourceDocumentType}); skipping",
                    outcome.Invoice!.Id,
                    message.SourceDocumentId,
                    SourceDocumentType);
                break;
            default:
                _logger.LogError(
                    "Permanent failure creating draft from {EventType} for source document {SourceDocumentId}. Code={ErrorCode}",
                    typeof(TEvent).Name,
                    message.SourceDocumentId,
                    outcome.ErrorCode);
                break;
        }
    }

    private static Dictionary<string, object> BuildLogScope(string correlationId)
    {
        return new Dictionary<string, object> { ["CorrelationId"] = correlationId };
    }
}
