namespace Finance.Common.ErrorCodes;

/// <summary>
/// Machine-readable error codes for audit-trail enforcement failures.
/// Used as the <c>title</c> field of ProblemDetails responses.
/// </summary>
public static class AuditErrorCodes
{
    /// <summary>A sensitive operation was attempted without the mandatory reason.</summary>
    public const string AUDIT_REASON_REQUIRED = nameof(AUDIT_REASON_REQUIRED);

    /// <summary>An attempt to modify or delete an append-only audit row was detected.</summary>
    public const string AUDIT_TAMPERING_DETECTED = nameof(AUDIT_TAMPERING_DETECTED);
}
