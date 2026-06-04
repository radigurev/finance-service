namespace Finance.Journal.API.Auditing;

/// <summary>
/// Canonical audit <c>EventType</c> values for posting-rule mutations (SDD-FIN-006 §2.6, SDD-AUDIT-001
/// §2.1). Deactivation is recorded as a <c>StateChange</c> but does NOT require a mandatory reason (it is
/// not on SDD-AUDIT-001's mandatory-reason list).
/// </summary>
public static class PostingRuleAuditEventTypes
{
    /// <summary>Audit event type for posting-rule creation.</summary>
    public const string PostingRuleCreated = nameof(PostingRuleCreated);

    /// <summary>Audit event type for a non-deactivating posting-rule update.</summary>
    public const string PostingRuleUpdated = nameof(PostingRuleUpdated);

    /// <summary>Audit event type for posting-rule deactivation.</summary>
    public const string PostingRuleDeactivated = nameof(PostingRuleDeactivated);

    /// <summary>The audited entity type for posting-rule rows.</summary>
    public const string EntityType = "PostingRule";

    /// <summary>The default reason recorded when a rule is deactivated via update.</summary>
    public const string DefaultDeactivationReason = "Posting rule deactivated via update (IsActive set to false).";
}
