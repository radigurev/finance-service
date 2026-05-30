using Finance.Nomenclature.API.Interfaces;

namespace Finance.Nomenclature.API.Tests.Fixtures;

/// <summary>
/// A fixed-identity <see cref="ICurrentUserAccessor"/> used by the Nomenclature unit tests so audit rows
/// are stamped with a deterministic user (SDD-AUDIT-001 §2.3).
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
