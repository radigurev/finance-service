using Finance.Common.Enums;

namespace Finance.ServiceModel.Events.Payments;

/// <summary>
/// Domain event published through the EF transactional outbox for EVERY allocation row created — never one
/// aggregate event per multi-item request — so the Invoices-side settlement consumer stays per-invoice, small,
/// and idempotent (SDD-PAY-002 §2.10). The handshake is ONE-WAY: the Invoices service updates the invoice's own
/// settled amount and settlement status from this event, and allocation never waits for, polls, or depends on
/// that service.
/// <para><b><see cref="OccurredAt"/> is an ORDERING TOKEN.</b> It MUST be stamped INSIDE the allocation
/// transaction from the server clock and MUST NOT be re-stamped when the outbox dispatches the row, when
/// MassTransit publishes it, or when a consumer receives it: the SDD-INV-001 settlement mirror compares it
/// against the invoice's last-applied timestamp and silently DROPS a strictly older token, which is what makes
/// that mirror last-writer-by-<see cref="OccurredAt"/> rather than last-writer-by-arrival. A token stamped at
/// dispatch time would reorder concurrent allocations of the same invoice and leave the mirror permanently
/// wrong.</para>
/// <para>The payment's own number is carried as <see cref="DocumentNumber"/> — the aggregate's column name and
/// the convention for every record under this folder; the <c>&lt;Entity&gt;Number</c> form is reserved for
/// cross-document references. <see cref="InvoiceSettlementStatus"/> is a PROPERTY name carrying a value of the
/// one shared <see cref="SettlementStatus"/> enum, not a second enum type.</para>
/// </summary>
public sealed record PaymentAllocatedEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The sequential-GUID identifier of the payment whose matching changed.</summary>
    public required Guid PaymentId { get; init; }

    /// <summary>The gapless country-formatted document number of the payment.</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>The cross-service identifier of the invoice the amount was matched against.</summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>The payment's ledger direction (<c>AP</c>/<c>AR</c>).</summary>
    public required PaymentDirection Direction { get; init; }

    /// <summary>The Warehouse-owned counterparty shared by both documents.</summary>
    public required Guid CounterpartyId { get; init; }

    /// <summary>The transactional currency, identical on both documents (v1 requires currency equality).</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The transactional amount applied to the invoice.</summary>
    public required decimal AllocatedAmount { get; init; }

    /// <summary>The country-rounded base-currency value of the applied amount at the payment's frozen rate.</summary>
    public required decimal BaseAllocatedAmount { get; init; }

    /// <summary>
    /// The signed document-level base-currency difference arising from the two documents' frozen rates
    /// (SDD-PAY-002 §2.9). Informational only until SDD-FIN-005 posts it — it changes no GL figure.
    /// </summary>
    public required decimal RealizedFxDifference { get; init; }

    /// <summary>The invoice's locally-owned settled amount AFTER this allocation.</summary>
    public required decimal InvoiceSettledAmount { get; init; }

    /// <summary>
    /// The invoice's derived settlement state after this allocation, carried so the Invoices-side consumer
    /// does not reproduce the derivation across the service boundary (SDD-PAY-002 §2.8).
    /// </summary>
    public required SettlementStatus InvoiceSettlementStatus { get; init; }

    /// <summary>The server timestamp stamped on the allocation row inside the allocation transaction.</summary>
    public required DateTimeOffset AllocatedAt { get; init; }
}
