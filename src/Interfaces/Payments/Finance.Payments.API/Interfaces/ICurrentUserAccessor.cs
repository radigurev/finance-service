namespace Finance.Payments.API.Interfaces;

/// <summary>
/// Provides the authenticated user's identity for audit recording and lifecycle stamps (SDD-AUDIT-001 §2.3,
/// SDD-PAY-001 §2.3, §2.4). Resolves the stable user identifier and display name from the ambient
/// <c>ClaimsPrincipal</c> established by the shared JWT authentication (SDD-INT-AUTH-001).
/// <para>This is a per-service copy: the contract is not shared infrastructure today. Promoting it (and its
/// <c>HttpContext</c> implementation) into <c>Finance.Infrastructure.Web</c> is a recorded SDD-INFRA-009
/// change (SDD-PAY-001 §7) — a project reference to another service's API project is forbidden.</para>
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>Gets the authenticated user's identifier, or <see cref="System.Guid.Empty"/> when absent.</summary>
    /// <returns>The user identifier from the bearer token claims.</returns>
    Guid GetUserId();

    /// <summary>Gets the authenticated user's display name, falling back to a system label when absent.</summary>
    /// <returns>The user name from the bearer token claims.</returns>
    string GetUsername();
}
