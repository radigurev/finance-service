namespace Finance.ServiceModel.Posting;

/// <summary>
/// Request body for creating a posting rule with its ordered lines (SDD-FIN-006 §2.1). The owning country
/// code is sourced from configuration server-side and is not part of the request.
/// </summary>
public sealed record CreatePostingRuleRequest
{
    /// <summary>The stable, unique, uppercase machine key (e.g. <c>"SALE_INVOICE"</c>).</summary>
    public required string RuleKey { get; init; }

    /// <summary>A human-readable description of what the rule books.</summary>
    public required string Description { get; init; }

    /// <summary>The ordered lines composing the rule (minimum one; structurally balanceable).</summary>
    public required IReadOnlyList<CreatePostingRuleLineRequest> Lines { get; init; }
}
