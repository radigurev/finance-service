using Finance.Common.Enums;

namespace Finance.ServiceModel.Invoices;

/// <summary>
/// Representation of an invoice exposed by the Invoices API (SDD-INV-001 §2.10). A single aggregate
/// represents all four document types, discriminated by <see cref="DocumentType"/> and <see cref="Direction"/>.
/// </summary>
public sealed record InvoiceDto
{
    /// <summary>Sequential-GUID identifier of the invoice (event-exposed and externally referenced).</summary>
    public required Guid Id { get; init; }

    /// <summary>The gapless country-formatted document number assigned at confirm; <c>null</c> while <c>Draft</c>.</summary>
    public string? DocumentNumber { get; init; }

    /// <summary>The document type discriminating the four invoice kinds.</summary>
    public required InvoiceDocumentType DocumentType { get; init; }

    /// <summary>The ledger direction (<c>AP</c>/<c>AR</c>) derived from <see cref="DocumentType"/>.</summary>
    public required InvoiceDirection Direction { get; init; }

    /// <summary>The lifecycle state: <c>Draft</c>, <c>Confirmed</c>, <c>Posted</c>, <c>Cancelled</c>, or <c>Reversed</c>.</summary>
    public required InvoiceStatus Status { get; init; }

    /// <summary>The Warehouse-owned counterparty (customer/supplier) reference (no cross-service join).</summary>
    public required Guid CounterpartyId { get; init; }

    /// <summary>ISO 4217 alphabetic transactional currency code.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The base currency the invoice books in (frozen at creation from the country strategy).</summary>
    public required string BaseCurrencyCode { get; init; }

    /// <summary>The issue date (drives period assignment and numbering).</summary>
    public required DateTimeOffset IssueDate { get; init; }

    /// <summary>The payment due date (on or after <see cref="IssueDate"/>).</summary>
    public required DateTimeOffset DueDate { get; init; }

    /// <summary>The document net total (sum of line nets).</summary>
    public required decimal NetTotal { get; init; }

    /// <summary>The document tax total (sum of line taxes).</summary>
    public required decimal TaxTotal { get; init; }

    /// <summary>The document gross total (<c>NetTotal + TaxTotal</c>).</summary>
    public required decimal GrossTotal { get; init; }

    /// <summary>On a credit/debit note, the original invoice it corrects; otherwise <c>null</c>.</summary>
    public Guid? CorrectsInvoiceId { get; init; }

    /// <summary>The linked journal entry once posted; <c>null</c> until the posting handshake completes.</summary>
    public Guid? JournalEntryId { get; init; }

    /// <summary>UTC-offset creation timestamp.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The confirm timestamp; <c>null</c> while <c>Draft</c>.</summary>
    public DateTimeOffset? ConfirmedAt { get; init; }

    /// <summary>The posting timestamp; <c>null</c> until <c>Posted</c>.</summary>
    public DateTimeOffset? PostedAt { get; init; }

    /// <summary>The lines composing the invoice, ordered by <see cref="InvoiceLineDto.LineNumber"/>.</summary>
    public required IReadOnlyList<InvoiceLineDto> Lines { get; init; }

    /// <summary>
    /// Base64-encoded SQL Server <c>rowversion</c> optimistic-concurrency token. Clients round-trip this
    /// value back on update / confirm / cancel so a stale write is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
