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
}
