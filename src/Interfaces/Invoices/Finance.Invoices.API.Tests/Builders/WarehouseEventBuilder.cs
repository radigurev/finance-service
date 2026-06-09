using Finance.ServiceModel.Integration.Warehouse.Events;

namespace Finance.Invoices.API.Tests.Builders;

/// <summary>
/// Builds the four Warehouse inbound event contracts for the consumer/factory tests (SDD-INT-WH-001 §6).
/// Defaults to a valid event (one usable line, a present counterparty, BGN currency); a test overrides only
/// what it exercises. The same field set drives all four event shapes via the shared
/// <see cref="IWarehouseDocumentEvent"/> contract.
/// </summary>
public sealed class WarehouseEventBuilder
{
    private Guid _messageId = Guid.NewGuid();
    private string _correlationId = "wh-correlation-id";
    private DateTimeOffset _occurredAt = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
    private Guid _sourceDocumentId = Guid.NewGuid();
    private Guid _counterpartyId = new("44444444-4444-4444-4444-444444444444");
    private string _currencyCode = "BGN";
    private Guid? _originatingShipmentId;
    private IReadOnlyList<WarehouseDocumentLine> _lines =
    [
        WarehouseDocumentLineBuilder.Create().Build()
    ];

    /// <summary>Starts a new builder with valid defaults.</summary>
    /// <returns>A fresh builder.</returns>
    public static WarehouseEventBuilder Create() => new();

    /// <summary>Sets the idempotency message id.</summary>
    /// <param name="messageId">The message id.</param>
    /// <returns>This builder.</returns>
    public WarehouseEventBuilder WithMessageId(Guid messageId)
    {
        _messageId = messageId;
        return this;
    }

    /// <summary>Sets the inbound correlation id.</summary>
    /// <param name="correlationId">The correlation id.</param>
    /// <returns>This builder.</returns>
    public WarehouseEventBuilder WithCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    /// <summary>Sets the originating-event timestamp (the fallback issue date).</summary>
    /// <param name="occurredAt">The occurred-at instant.</param>
    /// <returns>This builder.</returns>
    public WarehouseEventBuilder WithOccurredAt(DateTimeOffset occurredAt)
    {
        _occurredAt = occurredAt;
        return this;
    }

    /// <summary>Sets the Warehouse source-document id (the dedupe key).</summary>
    /// <param name="sourceDocumentId">The source-document id.</param>
    /// <returns>This builder.</returns>
    public WarehouseEventBuilder WithSourceDocumentId(Guid sourceDocumentId)
    {
        _sourceDocumentId = sourceDocumentId;
        return this;
    }

    /// <summary>Sets the counterparty (supplier/customer) reference.</summary>
    /// <param name="counterpartyId">The counterparty id.</param>
    /// <returns>This builder.</returns>
    public WarehouseEventBuilder WithCounterpartyId(Guid counterpartyId)
    {
        _counterpartyId = counterpartyId;
        return this;
    }

    /// <summary>Sets the document currency code.</summary>
    /// <param name="currencyCode">The currency code.</param>
    /// <returns>This builder.</returns>
    public WarehouseEventBuilder WithCurrencyCode(string currencyCode)
    {
        _currencyCode = currencyCode;
        return this;
    }

    /// <summary>Sets the originating-shipment linkage (customer-return events only).</summary>
    /// <param name="originatingShipmentId">The originating shipment id.</param>
    /// <returns>This builder.</returns>
    public WarehouseEventBuilder WithOriginatingShipmentId(Guid? originatingShipmentId)
    {
        _originatingShipmentId = originatingShipmentId;
        return this;
    }

    /// <summary>Replaces the line collection with the supplied lines.</summary>
    /// <param name="lines">The line items.</param>
    /// <returns>This builder.</returns>
    public WarehouseEventBuilder WithLines(params WarehouseDocumentLine[] lines)
    {
        _lines = lines;
        return this;
    }

    /// <summary>Removes all lines (a zero-line event) for the permanent-failure path.</summary>
    /// <returns>This builder.</returns>
    public WarehouseEventBuilder WithNoLines()
    {
        _lines = [];
        return this;
    }

    /// <summary>Builds a <see cref="GoodsReceiptCompletedEvent"/> (→ draft purchase invoice).</summary>
    /// <returns>The built event.</returns>
    public GoodsReceiptCompletedEvent BuildGoodsReceipt() => new()
    {
        MessageId = _messageId,
        CorrelationId = _correlationId,
        OccurredAt = _occurredAt,
        SourceDocumentId = _sourceDocumentId,
        CounterpartyId = _counterpartyId,
        CurrencyCode = _currencyCode,
        Lines = _lines
    };

    /// <summary>Builds a <see cref="ShipmentCompletedEvent"/> (→ draft sale invoice).</summary>
    /// <returns>The built event.</returns>
    public ShipmentCompletedEvent BuildShipment() => new()
    {
        MessageId = _messageId,
        CorrelationId = _correlationId,
        OccurredAt = _occurredAt,
        SourceDocumentId = _sourceDocumentId,
        CounterpartyId = _counterpartyId,
        CurrencyCode = _currencyCode,
        Lines = _lines
    };

    /// <summary>Builds a <see cref="CustomerReturnCompletedEvent"/> (→ draft credit note).</summary>
    /// <returns>The built event.</returns>
    public CustomerReturnCompletedEvent BuildCustomerReturn() => new()
    {
        MessageId = _messageId,
        CorrelationId = _correlationId,
        OccurredAt = _occurredAt,
        SourceDocumentId = _sourceDocumentId,
        CounterpartyId = _counterpartyId,
        CurrencyCode = _currencyCode,
        OriginatingShipmentId = _originatingShipmentId,
        Lines = _lines
    };

    /// <summary>Builds a <see cref="SupplierReturnShippedEvent"/> (→ draft debit note).</summary>
    /// <returns>The built event.</returns>
    public SupplierReturnShippedEvent BuildSupplierReturn() => new()
    {
        MessageId = _messageId,
        CorrelationId = _correlationId,
        OccurredAt = _occurredAt,
        SourceDocumentId = _sourceDocumentId,
        CounterpartyId = _counterpartyId,
        CurrencyCode = _currencyCode,
        Lines = _lines
    };
}
