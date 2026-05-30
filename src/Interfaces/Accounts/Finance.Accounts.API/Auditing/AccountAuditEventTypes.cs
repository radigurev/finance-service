namespace Finance.Accounts.API.Auditing;

/// <summary>
/// Canonical audit <c>EventType</c> values for chart-of-accounts mutations (SDD-ACCT-001 §2.9,
/// SDD-AUDIT-001 §2.1). <see cref="AccountDeactivated"/> is high-sensitivity and MUST carry a reason.
/// </summary>
public static class AccountAuditEventTypes
{
    /// <summary>Audit event type for account creation.</summary>
    public const string AccountCreated = nameof(AccountCreated);

    /// <summary>Audit event type for a non-deactivating account update.</summary>
    public const string AccountUpdated = nameof(AccountUpdated);

    /// <summary>Audit event type for account deactivation. High-sensitivity: requires a reason.</summary>
    public const string AccountDeactivated = nameof(AccountDeactivated);

    /// <summary>The audited entity type for chart-of-accounts rows.</summary>
    public const string EntityType = "Account";

    /// <summary>The default reason recorded when an account is deactivated without an explicit reason.</summary>
    public const string DefaultDeactivationReason = "Account deactivated via update (IsActive set to false).";
}
