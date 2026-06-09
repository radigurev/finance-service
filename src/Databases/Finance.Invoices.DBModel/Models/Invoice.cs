using Finance.Common.Enums;
using Finance.GenericFiltering.Attributes;

namespace Finance.Invoices.DBModel.Models;

/// <summary>
/// The invoice aggregate root (SDD-INV-001 §1, §2.3): a single entity representing all four financial
/// documents (purchase invoice, sale invoice, credit note, debit note) discriminated by
/// <see cref="DocumentType"/> and <see cref="Direction"/>. Event-exposed and externally referenced, so its
/// identifier is a sequential GUID. Its lifecycle (<c>Draft → Confirmed → Posted</c>, plus
/// <c>Cancelled</c>/<c>Reversed</c>) is owned by SDD-INV-001. Confirmed/posted invoices are immutable.
/// </summary>
public sealed class Invoice
{
    /// <summary>Sequential-GUID identifier (event-exposed, externally referenced).</summary>
    public Guid Id { get; set; }

    /// <summary>The gapless country-formatted document number assigned at confirm; <c>null</c> while <c>Draft</c>.</summary>
    [Filterable]
    [Sortable]
    public string? DocumentNumber { get; set; }

    /// <summary>The document type discriminating the four invoice kinds.</summary>
    [Filterable]
    [Sortable]
    public InvoiceDocumentType DocumentType { get; set; }

    /// <summary>The ledger direction (<c>AP</c>/<c>AR</c>), derived from <see cref="DocumentType"/> and frozen.</summary>
    [Filterable]
    [Sortable]
    public InvoiceDirection Direction { get; set; }

    /// <summary>The lifecycle state.</summary>
    [Filterable]
    [Sortable]
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    /// <summary>The Warehouse-owned counterparty (customer/supplier) reference (no cross-database join).</summary>
    [Filterable]
    public Guid CounterpartyId { get; set; }

    /// <summary>ISO 4217 alphabetic transactional currency code.</summary>
    [Filterable]
    [Sortable]
    public required string CurrencyCode { get; set; }

    /// <summary>The base currency the invoice books in (frozen at creation from the country strategy).</summary>
    public required string BaseCurrencyCode { get; set; }

    /// <summary>The issue date (drives period assignment and numbering).</summary>
    [Filterable]
    [Sortable]
    public DateTimeOffset IssueDate { get; set; }

    /// <summary>The payment due date (on or after <see cref="IssueDate"/>).</summary>
    [Filterable]
    [Sortable]
    public DateTimeOffset DueDate { get; set; }

    /// <summary>The document net total (sum of line nets).</summary>
    public decimal NetTotal { get; set; }

    /// <summary>The document tax total (sum of line taxes).</summary>
    public decimal TaxTotal { get; set; }

    /// <summary>The document gross total (<c>NetTotal + TaxTotal</c>).</summary>
    public decimal GrossTotal { get; set; }

    /// <summary>On a credit/debit note, the original invoice it corrects; otherwise <c>null</c>.</summary>
    public Guid? CorrectsInvoiceId { get; set; }

    /// <summary>
    /// The Warehouse source-document identifier (goods-receipt / shipment / return id) this draft was
    /// materialized from, or <c>null</c> for a manually created invoice (SDD-INT-WH-001 §2.1.4). Together
    /// with <see cref="SourceDocumentType"/> it makes the document traceable to its Warehouse origin and is
    /// the dedupe key that prevents a re-published event from creating a second draft (SDD-INT-WH-001 §2.1.2).
    /// </summary>
    public Guid? SourceDocumentId { get; set; }

    /// <summary>
    /// The Warehouse source-document type (<c>GoodsReceipt</c>/<c>Shipment</c>/<c>CustomerReturn</c>/
    /// <c>SupplierReturn</c>) this draft was materialized from, or <c>null</c> for a manually created
    /// invoice (SDD-INT-WH-001 §2.1.4, §2.2).
    /// </summary>
    public string? SourceDocumentType { get; set; }

    /// <summary>The linked journal entry once posted; <c>null</c> until the posting handshake completes.</summary>
    public Guid? JournalEntryId { get; set; }

    /// <summary>The ambient correlation identifier captured at creation.</summary>
    public required string CorrelationId { get; set; }

    /// <summary>UTC-offset creation timestamp.</summary>
    [Filterable]
    [Sortable]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The identifier of the user (or system identity) who created the invoice.</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>The confirm timestamp; <c>null</c> while <c>Draft</c>.</summary>
    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>The identifier of the user who confirmed the invoice; <c>null</c> while <c>Draft</c>.</summary>
    public Guid? ConfirmedBy { get; set; }

    /// <summary>The posting timestamp; <c>null</c> until <c>Posted</c>.</summary>
    public DateTimeOffset? PostedAt { get; set; }

    /// <summary>SQL Server <c>rowversion</c> optimistic-concurrency token (SDD-INFRA-008/009).</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The lines composing the invoice (composition: loaded and saved with the invoice).</summary>
    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();

    /// <summary>The append-only state-transition history for the invoice.</summary>
    public ICollection<InvoiceStatusHistory> StatusHistory { get; set; } = new List<InvoiceStatusHistory>();
}
