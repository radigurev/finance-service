namespace Finance.Periods.API.Auditing;

/// <summary>
/// Canonical audit <c>EventType</c> values for fiscal-period lifecycle changes (SDD-FIN-004 §2.10,
/// SDD-AUDIT-001 §2.1). Close and reopen are high-sensitivity operations: the Periods service validates a
/// non-empty reason at the service layer before any audit row is written (SDD-FIN-004 §2.4, §2.5).
/// </summary>
public static class PeriodAuditEventTypes
{
    /// <summary>Audit event type for period generation / single-period creation.</summary>
    public const string FiscalPeriodCreated = nameof(FiscalPeriodCreated);

    /// <summary>Audit event type for closing a period (Open → Closed). Carries a mandatory reason.</summary>
    public const string FiscalPeriodClosed = nameof(FiscalPeriodClosed);

    /// <summary>Audit event type for reopening a period (Closed → Open). Carries a mandatory reason.</summary>
    public const string FiscalPeriodReopened = nameof(FiscalPeriodReopened);

    /// <summary>The audited entity type for fiscal-period rows.</summary>
    public const string EntityType = "FiscalPeriod";
}
