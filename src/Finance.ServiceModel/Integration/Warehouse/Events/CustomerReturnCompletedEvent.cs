namespace Finance.ServiceModel.Integration.Warehouse.Events;

/// <summary>
/// Warehouse Fulfillment event published when a customer return has been processed (SDD-INT-WH-001 §2.2).
/// Finance's <c>CustomerReturnCompletedConsumer</c> turns it into a draft <b>Credit Note</b>
/// (counterparty = customer). Where <see cref="OriginatingShipmentId"/> references the originating sale and a
/// matching Finance sale invoice exists, the consumer populates the <c>CorrectsInvoiceId</c> linkage;
/// otherwise the Credit Note is created standalone and the operator links it on review (SDD-INT-WH-001 §2.6).
/// This is a <b>Warehouse-owned</b> contract mirrored locally; Finance binds only to the fields it depends
/// on and tolerates additional Warehouse fields it does not consume (forward-compatible).
/// </summary>
public sealed record CustomerReturnCompletedEvent : IWarehouseDocumentEvent
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

    /// <summary>
    /// The Warehouse identifier of the originating shipment / sale this return corrects, when known. The
    /// consumer uses it to find a matching Finance sale invoice (by its <c>SourceDocumentId</c>) and link the
    /// Credit Note via <c>CorrectsInvoiceId</c>; when absent or unmatched the Credit Note is standalone
    /// (SDD-INT-WH-001 §2.2, §2.6).
    /// </summary>
    public Guid? OriginatingShipmentId { get; init; }
}
