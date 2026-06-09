using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.IntegrationTesting;

/// <summary>
/// Test double for <see cref="IUserPermissionService"/> that returns the permission set held by
/// <see cref="TestPermissionState"/> instead of resolving via Redis + Auth.API. This keeps the
/// real authorization pipeline (<c>PermissionAuthorizationHandler</c> + policy + the JWT principal)
/// under test while removing the live Auth.API dependency.
/// </summary>
public sealed class FakeUserPermissionService : IUserPermissionService
{
    private readonly TestPermissionState _state;

    /// <summary>Initializes the fake with the shared permission state.</summary>
    public FakeUserPermissionService(TestPermissionState state) => _state = state;

    /// <summary>Returns the permissions currently configured on <see cref="TestPermissionState"/>.</summary>
    public Task<IReadOnlySet<string>> GetPermissionsAsync(int userId, CancellationToken cancellationToken) =>
        Task.FromResult(_state.Permissions);
}
