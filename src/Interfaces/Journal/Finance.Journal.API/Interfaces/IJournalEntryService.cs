using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.ServiceModel.Journal;

namespace Finance.Journal.API.Interfaces;

/// <summary>
/// Application service for the journal-entry lifecycle (SDD-FIN-001, SDD-FIN-002). Every method returns a
/// <see cref="Result"/> / <see cref="Result{T}"/>; business outcomes are never signalled via <c>null</c>
/// or thrown exceptions (SDD-INFRA-009). Posted entries are immutable — corrections are made by reversal.
/// </summary>
public interface IJournalEntryService
{
    /// <summary>
    /// Returns a filtered, sorted, and paged page of entries, defaulting to descending <c>EntryDate</c>
    /// ordering (SDD-FIN-002 §2.9). Journal entries are transactional data and are never cached.
    /// </summary>
    /// <param name="request">The client-supplied filter, sort, and pagination request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the page, or a failure carrying the filter error code.</returns>
    Task<Result<PagedResult<JournalEntryDto>>> SearchAsync(FilterRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the entry with the given id and its lines, or a <c>JOURNAL_ENTRY_NOT_FOUND</c> failure
    /// (SDD-FIN-002 §2.9). Never cached.
    /// </summary>
    /// <param name="id">The entry identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the entry, or a not-found failure.</returns>
    Task<Result<JournalEntryDto>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the <c>Posted</c> entry already booked for the given source document, or <c>null</c> when none
    /// exists (SDD-PAY-001 §2.5). It is the aggregate-level duplicate-post guard the document consumers call
    /// before posting, backed by the UNIQUE FILTERED index <c>IX_JournalEntries_SourceDocument</c>.
    /// </summary>
    /// <param name="sourceDocumentType">The source-document type tag (e.g. <c>Payment</c>, <c>Invoice</c>).</param>
    /// <param name="sourceDocumentId">The source-document identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The matching posted <see cref="JournalEntryDto"/>, or <c>null</c> when nothing is posted yet.</returns>
    Task<JournalEntryDto?> FindPostedBySourceDocumentAsync(
        string sourceDocumentType,
        Guid sourceDocumentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a balanced draft entry from caller-supplied lines after the full SDD-FIN-001 validation
    /// surface passes, writing an audit <c>Create</c> row (SDD-FIN-002 §2.3).
    /// </summary>
    /// <param name="request">The create-draft request.</param>
    /// <param name="baseCurrencyCode">The base currency frozen on the entry, sourced from configuration.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the created draft, or a validation failure.</returns>
    Task<Result<JournalEntryDto>> CreateDraftAsync(
        CreateJournalEntryRequest request,
        string baseCurrencyCode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates a <c>Draft</c> entry (description, lines) under optimistic concurrency, re-validating the
    /// SDD-FIN-001 surface; a non-draft entry is rejected with <c>CANNOT_EDIT_POSTED_ENTRY</c>
    /// (SDD-FIN-002 §2.5).
    /// </summary>
    /// <param name="id">The entry identifier.</param>
    /// <param name="request">The update request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the updated draft, or a not-found / state / concurrency failure.</returns>
    Task<Result<JournalEntryDto>> UpdateDraftAsync(
        Guid id,
        UpdateJournalEntryRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hard-deletes a <c>Draft</c> entry; a non-draft entry is rejected with
    /// <c>CANNOT_EDIT_POSTED_ENTRY</c> (SDD-FIN-002 §2.5).
    /// </summary>
    /// <param name="id">The entry identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result, or a not-found / state failure.</returns>
    Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Posts a <c>Draft</c> entry: re-validates, runs the period guard, assigns a gapless <c>JE</c> number,
    /// stamps posted-at/by, writes an audit <c>StateChange</c> row, and publishes
    /// <c>JournalEntryPostedEvent</c> via the transactional outbox — all atomically (SDD-FIN-002 §2.4).
    /// </summary>
    /// <param name="id">The entry identifier.</param>
    /// <param name="request">The post request carrying the row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the posted entry, or a state / validation / concurrency failure.</returns>
    Task<Result<JournalEntryDto>> PostAsync(
        Guid id,
        PostJournalEntryRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reverses a <c>Posted</c> entry by creating a sign-flipped linked posted entry and flipping the
    /// original to <c>Reversed</c>, with a mandatory reason, audit rows for both, and
    /// <c>JournalEntryReversedEvent</c> via the outbox — all atomically (SDD-FIN-002 §2.6).
    /// </summary>
    /// <param name="id">The identifier of the posted entry to reverse.</param>
    /// <param name="request">The reverse request carrying the reason and row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the new reversal entry, or a state / validation / concurrency failure.</returns>
    Task<Result<JournalEntryDto>> ReverseAsync(
        Guid id,
        ReverseJournalEntryRequest request,
        CancellationToken cancellationToken);
}
