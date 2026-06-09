namespace Finance.IntegrationTesting;

/// <summary>
/// Holds the permission set the <see cref="FakeUserPermissionService"/> returns for the current
/// test. Integration tests set <see cref="Permissions"/> in their arrange step to drive the real
/// <c>PermissionAuthorizationHandler</c> toward a 200 (permission present) or 403 (permission
/// absent) outcome without any dependency on the live Auth.API.
/// </summary>
public sealed class TestPermissionState
{
    /// <summary>The effective permission strings granted to the test caller. Defaults to none.</summary>
    public IReadOnlySet<string> Permissions { get; set; } = new HashSet<string>();

    /// <summary>Grants the supplied permissions, replacing any previously configured set.</summary>
    public void Grant(params string[] permissions) => Permissions = new HashSet<string>(permissions);

    /// <summary>Revokes all permissions (drives endpoints toward 403).</summary>
    public void RevokeAll() => Permissions = new HashSet<string>();
}
