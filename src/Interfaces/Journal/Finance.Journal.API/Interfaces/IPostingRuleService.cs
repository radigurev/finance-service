using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.ServiceModel.Posting;

namespace Finance.Journal.API.Interfaces;

/// <summary>
/// Application service for managing the editable posting-rule reference data (SDD-FIN-006 §2.1). Every
/// method returns a <see cref="Result"/> / <see cref="Result{T}"/>; business outcomes are never signalled
/// via <c>null</c> or exceptions (SDD-INFRA-009). Reads are cached, writes are audited and invalidate the
/// cache (SDD-INFRA-004, SDD-AUDIT-001).
/// </summary>
public interface IPostingRuleService
{
    /// <summary>
    /// Returns a filtered, sorted, and paged page of posting rules, defaulting to ascending
    /// <c>RuleKey</c> ordering (SDD-FIN-006 §2.1).
    /// </summary>
    /// <param name="request">The client-supplied filter, sort, and pagination request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the page, or a failure carrying the filter error code.</returns>
    Task<Result<PagedResult<PostingRuleDto>>> SearchAsync(FilterRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the rule with the given id and its ordered lines, or a <c>POSTING_RULE_NOT_FOUND</c>
    /// failure (SDD-FIN-006 §2.1). Served from the reference-read cache.
    /// </summary>
    /// <param name="id">The surrogate rule identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the rule, or a not-found failure.</returns>
    Task<Result<PostingRuleDto>> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a posting rule and its lines after duplicate-key and structural-balance validation
    /// (SDD-FIN-006 §2.1, §3.2). Writes an audit <c>Create</c> row and invalidates the cache.
    /// </summary>
    /// <param name="request">The create request body.</param>
    /// <param name="countryCode">The owning country code derived from configuration.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the created rule, or a validation/conflict failure.</returns>
    Task<Result<PostingRuleDto>> CreateAsync(
        CreatePostingRuleRequest request,
        string countryCode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates a rule (description, active flag, lines) under optimistic concurrency, re-validating the
    /// structural balance (SDD-FIN-006 §2.1). Deactivation writes a <c>StateChange</c> audit row; other
    /// updates write an <c>Update</c> row. Invalidates the cache.
    /// </summary>
    /// <param name="id">The surrogate rule identifier.</param>
    /// <param name="request">The update request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the updated rule, or a not-found / validation / concurrency failure.</returns>
    Task<Result<PostingRuleDto>> UpdateAsync(
        int id,
        UpdatePostingRuleRequest request,
        CancellationToken cancellationToken);
}
