namespace Finance.ServiceModel.Invoices;

/// <summary>
/// Request body for updating a draft invoice (SDD-INV-001 §2.6). Only a <c>Draft</c> invoice may be
/// updated; a confirmed-or-later invoice is immutable. Carries the row version for optimistic concurrency.
/// </summary>
public sealed record UpdateInvoiceRequest
{
    /// <summary>The Warehouse-owned counterparty (customer/supplier) reference.</summary>
    public required Guid CounterpartyId { get; init; }

    /// <summary>ISO 4217 alphabetic transactional currency code.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The issue date of the document.</summary>
    public required DateTimeOffset IssueDate { get; init; }

    /// <summary>The payment due date (must be on or after <see cref="IssueDate"/>).</summary>
    public required DateTimeOffset DueDate { get; init; }

    /// <summary>The replacement lines composing the invoice (at least one).</summary>
    public required IReadOnlyList<InvoiceLineRequest> Lines { get; init; }

    /// <summary>
    /// Base64-encoded <c>rowversion</c> token captured from the prior read, used for optimistic concurrency.
    /// A stale token is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
