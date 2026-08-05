using Finance.Common.Enums;

namespace Finance.ServiceModel.Invoices;

/// <summary>
/// Request body for creating a draft invoice (SDD-INV-001 §2.3). The same domain create path serves both
/// manual user-entered drafts and system-created drafts (SDD-INT-WH-001); the base currency is sourced
/// server-side from the country strategy and is not part of the request.
/// </summary>
public sealed record CreateInvoiceRequest
{
    /// <summary>The document type discriminating the four invoice kinds.</summary>
    public required InvoiceDocumentType DocumentType { get; init; }

    /// <summary>The Warehouse-owned counterparty (customer/supplier) reference.</summary>
    public required Guid CounterpartyId { get; init; }

    /// <summary>ISO 4217 alphabetic transactional currency code.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The issue date of the document.</summary>
    public required DateTimeOffset IssueDate { get; init; }

    /// <summary>The payment due date (must be on or after <see cref="IssueDate"/>).</summary>
    public required DateTimeOffset DueDate { get; init; }

    /// <summary>The lines composing the invoice (a manual create requires at least one).</summary>
    public required IReadOnlyList<InvoiceLineRequest> Lines { get; init; }

    /// <summary>
    /// The booking rate at which the invoice's <see cref="CurrencyCode"/> amounts are booked into the
    /// server-resolved base currency (SDD-INV-001 §2.14). The service FREEZES it on the document at creation:
    /// <c>1.000000</c> whenever the transactional currency equals the base currency (the supplied value is then
    /// irrelevant), otherwise this caller-supplied rate — automatic rate resolution is deferred to SDD-FIN-005.
    /// When omitted the service freezes <c>1.000000</c>.
    /// </summary>
    public decimal? ExchangeRate { get; init; }

    /// <summary>On a credit/debit note, the original invoice it corrects; otherwise <c>null</c>.</summary>
    public Guid? CorrectsInvoiceId { get; init; }

    /// <summary>
    /// The Warehouse source-document identifier this draft is materialized from (SDD-INT-WH-001 §2.1.4).
    /// Supplied only by the Warehouse inbound consumers; the manual <c>POST</c> path leaves it <c>null</c>.
    /// </summary>
    public Guid? SourceDocumentId { get; init; }

    /// <summary>
    /// The Warehouse source-document type this draft is materialized from (SDD-INT-WH-001 §2.2). Supplied
    /// only by the Warehouse inbound consumers; the manual <c>POST</c> path leaves it <c>null</c>.
    /// </summary>
    public string? SourceDocumentType { get; init; }

    /// <summary>
    /// An explicit correlation identifier to stamp on the created draft (SDD-INT-WH-001 §2.1; SDD-INFRA-001).
    /// Supplied by the Warehouse inbound consumers so the draft carries the originating event's correlation
    /// id; when <c>null</c> (the manual <c>POST</c> path) the service uses the ambient correlation id.
    /// </summary>
    public string? CorrelationId { get; init; }
}
