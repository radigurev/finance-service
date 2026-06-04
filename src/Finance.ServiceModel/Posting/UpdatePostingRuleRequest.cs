namespace Finance.ServiceModel.Posting;

/// <summary>
/// Request body for updating a posting rule under optimistic concurrency (SDD-FIN-006 §2.1). The
/// <c>RuleKey</c> is immutable after create, so it is not part of the request; <c>IsActive = false</c>
/// deactivates the rule (CoA-style retire).
/// </summary>
public sealed record UpdatePostingRuleRequest
{
    /// <summary>A human-readable description of what the rule books.</summary>
    public required string Description { get; init; }

    /// <summary>Whether the rule is active and applicable; <c>false</c> deactivates it.</summary>
    public required bool IsActive { get; init; }

    /// <summary>The ordered lines that replace the rule's current lines (minimum one; structurally balanceable).</summary>
    public required IReadOnlyList<CreatePostingRuleLineRequest> Lines { get; init; }

    /// <summary>
    /// Base64-encoded <c>rowversion</c> token captured from the prior read, used for optimistic
    /// concurrency. A stale token is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
