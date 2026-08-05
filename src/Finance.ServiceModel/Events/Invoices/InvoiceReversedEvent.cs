namespace Finance.ServiceModel.Events.Invoices;

/// <summary>
/// Domain event published through the transactional outbox when a posted invoice is reversed
/// (SDD-INV-001 §2.7; consumed by SDD-PAY-002 §2.3). Its GL effect is fully offset, so the document stops
/// being a legal settlement target: the Payments-side <c>InvoiceReversedEventConsumer</c> mirrors the
/// reversal onto the local <c>InvoiceOpenItem</c> projection, which makes the item ineligible for further
/// allocation (SDD-PAY-002 §2.5 rule 3). Without the mirror the projection would keep reading <c>Posted</c>
/// forever and real cash could be matched to a voided document while the genuinely open invoice stayed
/// outstanding.
/// <para><b>Payload ownership:</b> the full payload is owned by the SDD-INV-001 §2.11 reversal path, which
/// publishes it through the <c>InvoicesDbContext</c> outbox in the SAME transaction as the state flag, the
/// audit row, and the status-history row. The Payments projection consumer requires ONLY
/// <see cref="InvoiceId"/>; the remaining members serve the audit and reporting consumers. Unlike
/// <c>InvoiceCancelledEvent.DocumentNumber</c> (nullable — a draft cancel has no number),
/// <see cref="DocumentNumber"/> here is required and non-nullable: <c>Reversed</c> is reachable ONLY from
/// <c>Posted</c> and every posted invoice was numbered at confirm.</para>
/// <para>Implements <see cref="IFinanceEvent"/>: <see cref="CorrelationId"/> is sourced from
/// <c>ICorrelationIdAccessor.Get()</c>, <see cref="MessageId"/> is a new GUID at construction, and
/// <see cref="OccurredAt"/> is stamped inside the reversal transaction.</para>
/// </summary>
public sealed record InvoiceReversedEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The sequential-GUID identifier of the reversed ORIGINAL invoice.</summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>The gapless country-formatted document number of the reversed original.</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>
    /// The credit/debit note whose full offset reversed the original — the other end of the note's
    /// <c>CorrectsInvoiceId</c> link.
    /// </summary>
    public required Guid CorrectingInvoiceId { get; init; }

    /// <summary>The operator-supplied reason recorded with the reversal.</summary>
    public required string Reason { get; init; }
}
