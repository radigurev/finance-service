using Finance.Journal.API.Interfaces;

namespace Finance.Journal.API.Tests.Fixtures;

/// <summary>
/// A fixed-identity <see cref="ICurrentUserAccessor"/> used by the Journal unit tests so audit rows,
/// posting stamps, and status-history rows are stamped with a deterministic user (SDD-FIN-002 §2.4,
/// SDD-AUDIT-001 §2.3).
/// </summary>
public sealed class StubCurrentUserAccessor : ICurrentUserAccessor
{
    /// <summary>The deterministic user identifier returned by every call.</summary>
    public static readonly Guid TestUserId = new("22222222-2222-2222-2222-222222222222");

    /// <summary>The deterministic user name returned by every call.</summary>
    public const string TestUsername = "test-user";

    /// <inheritdoc />
    public Guid GetUserId() => TestUserId;

    /// <inheritdoc />
    public string GetUsername() => TestUsername;
}
