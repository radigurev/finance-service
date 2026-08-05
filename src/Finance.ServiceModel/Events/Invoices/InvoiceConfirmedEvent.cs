using Finance.Common.Enums;

namespace Finance.ServiceModel.Events.Invoices;

/// <summary>
/// Domain event published through the transactional outbox when an invoice is confirmed
/// (SDD-INV-001 §2.4, §2.11; SDD-INFRA-006 §2.2). The Journal service consumes it and posts a balanced
/// journal entry via the Posting Engine using <see cref="PostingRuleKey"/> and the net/tax/gross amounts.
/// <para>Implements <see cref="IFinanceEvent"/>: <see cref="CorrelationId"/> is sourced from
/// <c>ICorrelationIdAccessor.Get()</c>, <see cref="MessageId"/> is a new GUID at construction, and
/// <see cref="OccurredAt"/> is <c>DateTimeOffset.UtcNow</c>.</para>
/// </summary>
public sealed record InvoiceConfirmedEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The sequential-GUID identifier of the confirmed invoice.</summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>The gapless country-formatted document number assigned at confirm.</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>The document type discriminating the four invoice kinds.</summary>
    public required InvoiceDocumentType DocumentType { get; init; }

    /// <summary>The ledger direction (<c>AP</c>/<c>AR</c>).</summary>
    public required InvoiceDirection Direction { get; init; }

    /// <summary>The Warehouse-owned counterparty reference.</summary>
    public required Guid CounterpartyId { get; init; }

    /// <summary>The transactional currency code.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The base currency the invoice books in.</summary>
    public required string BaseCurrencyCode { get; init; }

    /// <summary>The issue date (used by the Journal posting for period assignment).</summary>
    public required DateTimeOffset IssueDate { get; init; }

    /// <summary>The posting-rule key (derived from <see cref="DocumentType"/>) the Journal posting applies.</summary>
    public required string PostingRuleKey { get; init; }

    /// <summary>The document net total.</summary>
    public required decimal NetTotal { get; init; }

    /// <summary>The document tax total.</summary>
    public required decimal TaxTotal { get; init; }

    /// <summary>The document gross total.</summary>
    public required decimal GrossTotal { get; init; }

    /// <summary>
    /// The invoice payment due date (SDD-INV-001 §2.11/§2.15), mirrored onto the SDD-PAY-002
    /// <c>InvoiceOpenItem</c> projection so aging (SDD-PAY-003) can bucket the open item. POPULATED by the
    /// SDD-INV-001 settlement amendment from <c>Invoice.DueDate</c> on every publish, so the SDD-PAY-002 §2.2
    /// contingency fallback (<c>DueDate := IssueDate</c>) is no longer taken.
    /// <para>The property stays NULLABLE deliberately: it is the wire contract's compatibility seam for a
    /// message enqueued by an older publisher and still sitting in an outbox row or a dead-letter queue.
    /// A publisher MUST NOT leave it unset.</para>
    /// </summary>
    public DateTimeOffset? DueDate { get; init; }

    /// <summary>
    /// The exchange rate the invoice FROZE at creation (SDD-INV-001 §2.14 <c>Invoice.ExchangeRate</c>),
    /// mirrored onto the SDD-PAY-002 <c>InvoiceOpenItem.BookingExchangeRate</c> so the realized-FX difference
    /// (SDD-PAY-002 §2.9) and SDD-PAY-003's base outstanding are computed from the rate the document actually
    /// booked at. POPULATED by the SDD-INV-001 settlement amendment from that frozen column — never a
    /// hard-coded <c>1.000000</c> — so the SDD-PAY-002 §2.2 contingency fallback, which misstated both figures
    /// for every non-base-currency invoice, is no longer taken.
    /// <para>The property stays NULLABLE deliberately: it is the wire contract's compatibility seam for a
    /// message enqueued by an older publisher and still sitting in an outbox row or a dead-letter queue.
    /// A publisher MUST NOT leave it unset.</para>
    /// </summary>
    public decimal? BookingExchangeRate { get; init; }
}
