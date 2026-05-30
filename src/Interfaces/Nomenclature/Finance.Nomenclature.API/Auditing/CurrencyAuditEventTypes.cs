namespace Finance.Nomenclature.API.Auditing;

/// <summary>
/// Canonical audit <c>EventType</c> values for currency mutations (SDD-NOM-001 §2.1,
/// SDD-AUDIT-001 §2.1). <see cref="CurrencyDeactivated"/> is high-sensitivity and MUST carry a reason.
/// </summary>
public static class CurrencyAuditEventTypes
{
    /// <summary>Audit event type for currency creation.</summary>
    public const string CurrencyCreated = nameof(CurrencyCreated);

    /// <summary>Audit event type for a non-deactivating currency update.</summary>
    public const string CurrencyUpdated = nameof(CurrencyUpdated);

    /// <summary>Audit event type for currency deactivation. High-sensitivity: requires a reason.</summary>
    public const string CurrencyDeactivated = nameof(CurrencyDeactivated);

    /// <summary>The audited entity type for currency rows.</summary>
    public const string EntityType = "Currency";

    /// <summary>The default reason recorded when a currency is deactivated without an explicit reason.</summary>
    public const string DefaultDeactivationReason = "Currency deactivated via update (IsActive set to false).";
}
