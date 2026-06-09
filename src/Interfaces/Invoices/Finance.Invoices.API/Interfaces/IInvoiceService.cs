using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Invoices.DBModel.Models;
using Finance.ServiceModel.Invoices;

namespace Finance.Invoices.API.Interfaces;

/// <summary>
/// Application service for the invoice lifecycle (SDD-INV-001). Every method returns a <see cref="Result"/>
/// / <see cref="Result{T}"/> (SDD-INFRA-009) and threads a <see cref="CancellationToken"/>. State
/// transitions go through <see cref="Finance.Common.Workflow.IWorkflowEngine{TAggregate}"/>; confirmed and
/// later states are immutable.
/// </summary>
public interface IInvoiceService
{
    /// <summary>Lists invoices as a filtered, sorted, and paged envelope (SDD-INV-001 §2.10).</summary>
    /// <param name="request">The filter, sort, and pagination request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A page of <see cref="InvoiceDto"/>.</returns>
    Task<Result<PagedResult<InvoiceDto>>> SearchAsync(FilterRequest request, CancellationToken cancellationToken);

    /// <summary>Returns a single invoice with its lines, or <c>INVOICE_NOT_FOUND</c> (SDD-INV-001 §2.10).</summary>
    /// <param name="id">The invoice identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The matching <see cref="InvoiceDto"/>, or a not-found failure.</returns>
    Task<Result<InvoiceDto>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a draft invoice (SDD-INV-001 §2.3). The same path serves both manual and system-created
    /// drafts; <paramref name="allowEmptyLines"/> permits a system-created draft to be built incrementally,
    /// whereas a manual create requires at least one line.
    /// </summary>
    /// <param name="request">The create-draft request.</param>
    /// <param name="allowEmptyLines">When <c>true</c>, a zero-line draft is allowed (system path).</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The created <see cref="InvoiceDto"/>, or a validation/conflict failure.</returns>
    Task<Result<InvoiceDto>> CreateDraftAsync(
        CreateInvoiceRequest request,
        bool allowEmptyLines,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the existing invoice materialized from the given Warehouse source document, or <c>null</c>
    /// when none exists (SDD-INT-WH-001 §2.1.2). Used by the Warehouse inbound consumers to dedupe a
    /// re-published event so a second draft is never created for the same source document.
    /// </summary>
    /// <param name="sourceDocumentType">The Warehouse source-document type (e.g. <c>GoodsReceipt</c>).</param>
    /// <param name="sourceDocumentId">The Warehouse source-document identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The matching <see cref="InvoiceDto"/>, or <c>null</c> when no draft exists for the source document.</returns>
    Task<InvoiceDto?> FindBySourceDocumentAsync(
        string sourceDocumentType,
        Guid sourceDocumentId,
        CancellationToken cancellationToken);

    /// <summary>Updates a draft invoice (SDD-INV-001 §2.6).</summary>
    /// <param name="id">The invoice identifier.</param>
    /// <param name="request">The update request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The updated <see cref="InvoiceDto"/>, or a not-found / immutable / concurrency failure.</returns>
    Task<Result<InvoiceDto>> UpdateDraftAsync(
        Guid id,
        UpdateInvoiceRequest request,
        CancellationToken cancellationToken);

    /// <summary>Deletes a draft invoice (SDD-INV-001 §2.9).</summary>
    /// <param name="id">The invoice identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result, or a not-found / immutable failure.</returns>
    Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Confirms a draft invoice (Draft → Confirmed) (SDD-INV-001 §2.4).</summary>
    /// <param name="id">The invoice identifier.</param>
    /// <param name="request">The confirm request carrying the row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The confirmed <see cref="InvoiceDto"/>, or a state / validation / concurrency failure.</returns>
    Task<Result<InvoiceDto>> ConfirmAsync(
        Guid id,
        ConfirmInvoiceRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Operator-driven post completion (SDD-INV-001 §2.5): confirms the <c>Posted</c> transition if the
    /// Journal back-event has already linked a journal entry; otherwise reports posting-pending.
    /// </summary>
    /// <param name="id">The invoice identifier.</param>
    /// <param name="request">The post request carrying the row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The posted <see cref="InvoiceDto"/>, or a state / concurrency failure.</returns>
    Task<Result<InvoiceDto>> PostAsync(Guid id, PostInvoiceRequest request, CancellationToken cancellationToken);

    /// <summary>Cancels (voids) a draft or confirmed invoice (SDD-INV-001 §2.6).</summary>
    /// <param name="id">The invoice identifier.</param>
    /// <param name="request">The cancel request carrying the mandatory reason and row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The cancelled <see cref="InvoiceDto"/>, or a state / validation / concurrency failure.</returns>
    Task<Result<InvoiceDto>> CancelAsync(
        Guid id,
        CancelInvoiceRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Links the posted journal entry and transitions the invoice <c>Confirmed → Posted</c> in response to
    /// the Journal back-event (SDD-INV-001 §2.5). A replay against an already-<c>Posted</c> invoice is a
    /// no-op. Invoked by the back-event consumer, not by the controller.
    /// </summary>
    /// <param name="invoiceId">The source invoice identifier carried on the back-event.</param>
    /// <param name="journalEntryId">The journal entry the posting produced.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result, or a not-found / state failure.</returns>
    Task<Result> LinkPostedJournalEntryAsync(
        Guid invoiceId,
        Guid journalEntryId,
        CancellationToken cancellationToken);
}
