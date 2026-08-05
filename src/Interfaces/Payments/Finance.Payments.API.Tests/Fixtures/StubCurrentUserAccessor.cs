using Finance.Payments.API.Interfaces;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// A fixed-identity <see cref="ICurrentUserAccessor"/> used by the Payments unit tests so audit rows,
/// confirm/post/reverse stamps, status-history rows, and allocation rows are stamped with a deterministic user
/// (SDD-PAY-001 §2.3, §2.4; SDD-PAY-002 §2.4; SDD-AUDIT-001 §2.3).
/// </summary>
public sealed class StubCurrentUserAccessor : ICurrentUserAccessor
{
    /// <summary>The deterministic user identifier returned by every call.</summary>
    public static readonly Guid TestUserId = new("44444444-4444-4444-4444-444444444444");

    /// <summary>The deterministic user name returned by every call.</summary>
    public const string TestUsername = "test-user";

    /// <inheritdoc />
    public Guid GetUserId() => TestUserId;

    /// <inheritdoc />
    public string GetUsername() => TestUsername;
}
