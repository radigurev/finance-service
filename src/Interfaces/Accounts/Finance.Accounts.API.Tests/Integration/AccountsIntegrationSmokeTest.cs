using System.Net;
using System.Net.Http.Json;
using Finance.Accounts.DBModel;
using Finance.IntegrationTesting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Finance.Accounts.API.Tests.Integration;

/// <summary>
/// Proves the shared Testcontainers harness boots the real Finance.Accounts.API host against a live
/// SQL Server / Redis / RabbitMQ, applies migrations, and enforces RBAC through the real authorization
/// pipeline. Tagged <c>[Category("Integration")]</c> so the offline unit run skips it.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("SDD-ACCT-001")]
public sealed class AccountsIntegrationSmokeTest
{
    private FinanceApiFactory<Program> _factory = null!;
    private DatabaseResetter _resetter = null!;

    /// <summary>Builds the host factory once for the fixture.</summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new FinanceApiFactory<Program>();
        // Force host creation so migrate-on-startup creates the test database before any reset.
        _ = _factory.Server;
        _resetter = new DatabaseResetter(
            IntegrationTestSetup.Containers.SqlConnectionStringForDatabase("finance_accounts_test"));
    }

    /// <summary>Disposes the host factory after the fixture.</summary>
    [OneTimeTearDown]
    public async Task OneTimeTearDown() => await _factory.DisposeAsync();

    /// <summary>The list endpoint returns 200 when the caller holds finance.account:read.</summary>
    [Test]
    public async Task List_Returns200_WhenCallerHasReadPermission()
    {
        // Arrange
        _factory.PermissionState.Grant("finance.account:read");
        HttpClient client = _factory.CreateAuthenticatedClient();

        // Act
        HttpResponseMessage response = await client.GetAsync("/api/v1/accounts");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>The list endpoint returns 403 when the caller lacks the required permission.</summary>
    [Test]
    public async Task List_Returns403_WhenCallerLacksPermission()
    {
        // Arrange
        _factory.PermissionState.RevokeAll();
        HttpClient client = _factory.CreateAuthenticatedClient();

        // Act
        HttpResponseMessage response = await client.GetAsync("/api/v1/accounts");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>A created account persists to SQL Server, and the resetter clears it for the next test.</summary>
    [Test]
    public async Task Create_PersistsToSql_AndResetterClearsState()
    {
        // Arrange
        await _resetter.ResetAsync();
        _factory.PermissionState.Grant("finance.account:write", "finance.account:read");
        HttpClient client = _factory.CreateAuthenticatedClient();
        CreateBody body = new("100", "Cash on hand", 1, null);

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/accounts", body);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(await CountAccountsAsync(), Is.EqualTo(1));

        await _resetter.ResetAsync();
        Assert.That(await CountAccountsAsync(), Is.EqualTo(0));
    }

    private async Task<int> CountAccountsAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        AccountsDbContext db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        return await db.Accounts.CountAsync();
    }

    private sealed record CreateBody(string Code, string Name, int Type, int? ParentId);
}
