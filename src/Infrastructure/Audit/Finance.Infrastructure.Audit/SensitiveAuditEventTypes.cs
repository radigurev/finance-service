namespace Finance.Infrastructure.Audit;

/// <summary>
/// Canonical set of high-sensitivity audit <c>EventType</c> values that MUST carry an operator-supplied
/// reason (SDD-AUDIT-001 §3): period close, period reopen, journal reversal, account deactivation, and
/// permission revocation. Recording any of these without a reason fails with <c>AUDIT_REASON_REQUIRED</c>.
/// </summary>
public static class SensitiveAuditEventTypes
{
    /// <summary>Fiscal period transition into a closed state.</summary>
    public const string PeriodClosed = nameof(PeriodClosed);

    /// <summary>Fiscal period close event type emitted by the Periods service (SDD-FIN-004 §2.4).</summary>
    public const string FiscalPeriodClosed = nameof(FiscalPeriodClosed);

    /// <summary>Fiscal period reopen event type emitted by the Periods service (SDD-FIN-004 §2.5).</summary>
    public const string FiscalPeriodReopened = nameof(FiscalPeriodReopened);

    /// <summary>Reversal of a previously posted journal entry.</summary>
    public const string JournalEntryReversed = nameof(JournalEntryReversed);

    /// <summary>Deactivation of a chart-of-accounts account.</summary>
    public const string AccountDeactivated = nameof(AccountDeactivated);

    /// <summary>Revocation of a finance RBAC permission.</summary>
    public const string PermissionRevoked = nameof(PermissionRevoked);

    /// <summary>Cancellation (voiding) of a draft cash payment (SDD-PAY-001 §2.6, §2.15).</summary>
    public const string PaymentCancelled = nameof(PaymentCancelled);

    /// <summary>Reversal of a posted cash payment (SDD-PAY-001 §2.7, §2.15).</summary>
    public const string PaymentReversed = nameof(PaymentReversed);

    private static readonly HashSet<string> Values = new(StringComparer.Ordinal)
    {
        PeriodClosed,
        FiscalPeriodClosed,
        FiscalPeriodReopened,
        JournalEntryReversed,
        AccountDeactivated,
        PermissionRevoked,
        PaymentCancelled,
        PaymentReversed,
    };

    /// <summary>
    /// Determines whether the supplied event type is high-sensitivity and therefore requires a reason.
    /// </summary>
    /// <param name="eventType">The audit <c>EventType</c> to test.</param>
    /// <returns><c>true</c> when the event type requires a reason; otherwise <c>false</c>.</returns>
    public static bool RequiresReason(string eventType) => Values.Contains(eventType);
}
