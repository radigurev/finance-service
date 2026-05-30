using Finance.Accounts.API.Interfaces;

namespace Finance.Accounts.API.Tests.Fixtures;

/// <summary>
/// A fixed-identity <see cref="ICurrentUserAccessor"/> used by the Accounts unit tests so audit rows
/// are stamped with a deterministic user (SDD-AUDIT-001 §2.3).
/// </summary>
public sealed class StubCurrentUserAccessor : ICurrentUserAccessor
{
    /// <summary>The deterministic user identifier returned by every call.</summary>
    public static readonly Guid TestUserId = new("11111111-1111-1111-1111-111111111111");

    /// <summary>The deterministic user name returned by every call.</summary>
    public const string TestUsername = "test-user";

    /// <inheritdoc />
    public Guid GetUserId() => TestUserId;

    /// <inheritdoc />
    public string GetUsername() => TestUsername;
}
