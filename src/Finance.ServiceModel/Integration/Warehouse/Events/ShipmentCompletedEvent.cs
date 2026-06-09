namespace Finance.ServiceModel.Integration.Warehouse.Events;

/// <summary>
/// Warehouse Fulfillment event published when a shipment has been dispatched to a customer
/// (SDD-INT-WH-001 §2.2). Finance's <c>ShipmentCompletedConsumer</c> turns it into a draft <b>Sale
/// Invoice</b> (counterparty = customer). This is a <b>Warehouse-owned</b> contract mirrored locally;
/// Finance binds only to the fields it depends on and tolerates additional Warehouse fields it does not
/// consume (forward-compatible — SDD-INT-WH-001 §2.3, §5).
/// </summary>
public sealed record ShipmentCompletedEvent : IWarehouseDocumentEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <inheritdoc />
    public required Guid SourceDocumentId { get; init; }

    /// <inheritdoc />
    public required Guid CounterpartyId { get; init; }

    /// <inheritdoc />
    public required string CurrencyCode { get; init; }

    /// <inheritdoc />
    public required IReadOnlyList<WarehouseDocumentLine> Lines { get; init; }
}
