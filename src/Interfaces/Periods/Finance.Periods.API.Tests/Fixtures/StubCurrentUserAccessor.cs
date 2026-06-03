using Finance.Periods.API.Interfaces;

namespace Finance.Periods.API.Tests.Fixtures;

/// <summary>
/// A fixed-identity <see cref="ICurrentUserAccessor"/> used by the Periods unit tests so audit rows,
/// close / reopen stamps, and status-history rows are stamped with a deterministic user (SDD-FIN-004 §2.4,
/// SDD-AUDIT-001 §2.3).
/// </summary>
public sealed class StubCurrentUserAccessor : ICurrentUserAccessor
{
    /// <summary>The deterministic user identifier returned by every call.</summary>
    public static readonly Guid TestUserId = new("33333333-3333-3333-3333-333333333333");

    /// <summary>The deterministic user name returned by every call.</summary>
    public const string TestUsername = "test-user";

    /// <inheritdoc />
    public Guid GetUserId() => TestUserId;

    /// <inheritdoc />
    public string GetUsername() => TestUsername;
}
