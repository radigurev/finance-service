using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.IntegrationTesting;

/// <summary>
/// Boots a Finance microservice host (<typeparamref name="TProgram"/>) against the shared
/// <see cref="FinanceContainers"/> infrastructure. Infrastructure connection details are supplied via
/// environment variables (see <see cref="IntegrationEnvironment"/>) before the host is created; this
/// factory replaces <see cref="IUserPermissionService"/> with <see cref="FakeUserPermissionService"/>
/// so RBAC is driven by <see cref="PermissionState"/> rather than the live Auth.API, while the real
/// JWT validation and authorization pipeline stay under test.
/// </summary>
/// <typeparam name="TProgram">The microservice entry-point class (its <c>public partial class Program</c>).</typeparam>
public sealed class FinanceApiFactory<TProgram> : WebApplicationFactory<TProgram>
    where TProgram : class
{
    private readonly Action<IServiceCollection>? _configureTestServices;

    /// <summary>Shared permission state controlling the fake permission service for this host.</summary>
    public TestPermissionState PermissionState { get; } = new();

    /// <summary>Creates a factory that only swaps the permission service.</summary>
    public FinanceApiFactory() : this(null)
    {
    }

    /// <summary>
    /// Creates a factory with an additional service-override hook applied after the permission swap —
    /// used to replace service-specific outbound clients (e.g. a Refit period-read client) with fakes.
    /// </summary>
    public FinanceApiFactory(Action<IServiceCollection>? configureTestServices) =>
        _configureTestServices = configureTestServices;

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUserPermissionService>();
            services.AddSingleton(PermissionState);
            services.AddScoped<IUserPermissionService, FakeUserPermissionService>();

            _configureTestServices?.Invoke(services);
        });
    }

    /// <summary>Creates an <see cref="HttpClient"/> with a signed bearer token for the given user.</summary>
    public HttpClient CreateAuthenticatedClient(int userId = 1)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }
}
