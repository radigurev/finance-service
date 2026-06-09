using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finance.Accounts.DBModel;
using Finance.Accounts.DBModel.Models;
using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Audit.Entities;
using Finance.IntegrationTesting;
using Finance.ServiceModel.Accounts;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Finance.Accounts.API.Tests.Integration;

/// <summary>
/// SDD-ACCT-001 §6.2 endpoint and wiring integration tests for the Chart of Accounts service. Each test
/// boots the real <c>Finance.Accounts.API</c> host through <see cref="FinanceApiFactory{TProgram}"/>
/// against the shared Testcontainers SQL Server / Redis / RabbitMQ infrastructure, drives the real
/// <c>[RequirePermission]</c> authorization pipeline through <see cref="TestPermissionState"/>, and asserts
/// the HTTP contract plus the persisted database state. Tagged <c>[Category("Integration")]</c> so the
/// offline unit run (<c>TestCategory!=Integration</c>) skips it.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("SDD-ACCT-001")]
public sealed class AccountsEndpointIntegrationTests
{
    private const string ReadPermission = "finance.account:read";
    private const string WritePermission = "finance.account:write";
    private const string CountryCode = "BG";

    private FinanceApiFactory<Program> _factory = null!;
    private DatabaseResetter _resetter = null!;

    /// <summary>Builds the host factory once and forces migrate-on-startup before any reset.</summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new FinanceApiFactory<Program>();
        _ = _factory.Server;
        _resetter = new DatabaseResetter(
            IntegrationTestSetup.Containers.SqlConnectionStringForDatabase("finance_accounts_test"));
    }

    /// <summary>
    /// Wipes all rows before each test for isolation. The MassTransit bus-outbox delivery service
    /// runs in the background and queries the outbox tables; that can deadlock against Respawn's
    /// bulk DELETE, so the reset is retried a few times on a SQL deadlock (error 1205).
    /// </summary>
    [SetUp]
    public async Task SetUp() => await ResetWithDeadlockRetryAsync();

    /// <summary>Disposes the host factory after the fixture.</summary>
    [OneTimeTearDown]
    public async Task OneTimeTearDown() => await _factory.DisposeAsync();

    /// <summary>List returns an empty paged result when no accounts exist.</summary>
    [Test]
    public async Task List_ReturnsEmptyPagedResult_WhenNoAccounts()
    {
        // Arrange
        _factory.PermissionState.Grant(ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();

        // Act
        HttpResponseMessage response = await client.GetAsync("/api/v1/accounts");
        PagedResult<AccountDto>? page = await response.Content.ReadFromJsonAsync<PagedResult<AccountDto>>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(page, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(page!.Items, Is.Empty);
            Assert.That(page.TotalCount, Is.EqualTo(0));
            Assert.That(page.Page, Is.EqualTo(1));
        });
    }

    /// <summary>List returns the paged result ordered by CountryCode then Code by default.</summary>
    [Test]
    public async Task List_ReturnsPagedResultOrderedByCountryAndCode()
    {
        // Arrange
        await SeedAccountsAsync(
            ("304", "BG"),
            ("401", "BG"),
            ("100", "BG"),
            ("501", "DE"));
        _factory.PermissionState.Grant(ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();

        // Act
        HttpResponseMessage response = await client.GetAsync("/api/v1/accounts");
        PagedResult<AccountDto>? page = await response.Content.ReadFromJsonAsync<PagedResult<AccountDto>>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(page, Is.Not.Null);
        IReadOnlyList<string> orderedKeys =
            page!.Items.Select(a => a.CountryCode + ":" + a.Code).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(page.TotalCount, Is.EqualTo(4));
            Assert.That(orderedKeys, Is.EqualTo(new[] { "BG:100", "BG:304", "BG:401", "DE:501" }));
        });
    }

    /// <summary>List applies the free-text search filter and the sort supplied via the query string.</summary>
    [Test]
    public async Task List_AppliesFilterAndSortFromQueryString()
    {
        // Arrange: only the "Supplier" rows should match the search term.
        await SeedNamedAccountsAsync(
            ("100", "Cash"),
            ("401", "Supplier A"),
            ("402", "Supplier B"));
        _factory.PermissionState.Grant(ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();

        // Act: free-text search "Supplier" with Code sorted descending via the query string.
        const string query =
            "/api/v1/accounts?Search=Supplier&Sort[0].Field=Code&Sort[0].Direction=desc";
        HttpResponseMessage response = await client.GetAsync(query);
        PagedResult<AccountDto>? page = await response.Content.ReadFromJsonAsync<PagedResult<AccountDto>>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(page, Is.Not.Null);
        IReadOnlyList<string> codes = page!.Items.Select(a => a.Code).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(page.TotalCount, Is.EqualTo(2));
            Assert.That(codes, Is.EqualTo(new[] { "402", "401" }));
        });
    }

    /// <summary>Get returns a 404 ProblemDetails carrying ACCOUNT_NOT_FOUND when the account is missing.</summary>
    [Test]
    public async Task Get_Returns404ProblemDetails_WhenAccountDoesNotExist()
    {
        // Arrange
        _factory.PermissionState.Grant(ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();

        // Act
        HttpResponseMessage response = await client.GetAsync("/api/v1/accounts/999999");
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(AccountErrorCodes.ACCOUNT_NOT_FOUND));
    }

    /// <summary>Get returns the account when it exists.</summary>
    [Test]
    public async Task Get_ReturnsAccount_WhenExists()
    {
        // Arrange
        int id = await SeedAccountAsync("304", "Стоки", AccountType.Asset, CountryCode);
        _factory.PermissionState.Grant(ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();

        // Act
        HttpResponseMessage response = await client.GetAsync($"/api/v1/accounts/{id}");
        AccountDto? dto = await response.Content.ReadFromJsonAsync<AccountDto>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(dto, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dto!.Id, Is.EqualTo(id));
            Assert.That(dto.Code, Is.EqualTo("304"));
            Assert.That(dto.Name, Is.EqualTo("Стоки"));
            Assert.That(dto.Type, Is.EqualTo(AccountType.Asset));
            Assert.That(dto.CountryCode, Is.EqualTo(CountryCode));
        });
    }

    /// <summary>Create returns 201 and persists the account to SQL Server.</summary>
    [Test]
    public async Task Create_Returns201_AndPersistsAccount()
    {
        // Arrange
        _factory.PermissionState.Grant(WritePermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        CreateAccountRequest request = new()
        {
            Code = "401",
            Name = "Доставчици",
            Type = AccountType.Liability,
            ParentId = null
        };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/accounts", request);
        AccountDto? created = await response.Content.ReadFromJsonAsync<AccountDto>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(created, Is.Not.Null);
        Account? persisted = await FindAccountAsync(created!.Id);
        Assert.That(persisted, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Code, Is.EqualTo("401"));
            Assert.That(persisted.Name, Is.EqualTo("Доставчици"));
            Assert.That(persisted.Type, Is.EqualTo(AccountType.Liability));
            Assert.That(persisted.CountryCode, Is.EqualTo(CountryCode));
            Assert.That(persisted.IsActive, Is.True);
        });
    }

    /// <summary>Create returns a 400 VALIDATION_FAILED ProblemDetails flagging the Code field when the code is missing.</summary>
    [Test]
    public async Task Create_Returns400ProblemDetails_WhenCodeMissing()
    {
        // Arrange
        _factory.PermissionState.Grant(WritePermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        CreateAccountRequest request = new()
        {
            Code = string.Empty,
            Name = "Доставчици",
            Type = AccountType.Liability,
            ParentId = null
        };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/accounts", request);
        ValidationProblemDetails? problem =
            await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(problem, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(problem!.Title, Is.EqualTo(CommonErrorCodes.VALIDATION_FAILED));
            Assert.That(problem.Errors.Keys, Has.Some.EqualTo("Code"));
        });
    }

    /// <summary>Create returns a 400 VALIDATION_FAILED ProblemDetails flagging the Type field when the type is out of range.</summary>
    [Test]
    public async Task Create_Returns400ProblemDetails_WhenTypeInvalid()
    {
        // Arrange
        _factory.PermissionState.Grant(WritePermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        object request = new
        {
            Code = "401",
            Name = "Доставчици",
            Type = 99,
            ParentId = (int?)null
        };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/accounts", request);
        ValidationProblemDetails? problem =
            await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(problem, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(problem!.Title, Is.EqualTo(CommonErrorCodes.VALIDATION_FAILED));
            Assert.That(problem.Errors.Keys, Has.Some.EqualTo("Type"));
        });
    }

    /// <summary>Create returns a 409 ProblemDetails with DUPLICATE_ACCOUNT_CODE for a duplicate code in the same country.</summary>
    [Test]
    public async Task Create_Returns409ProblemDetails_WhenDuplicateCodeInSameCountry()
    {
        // Arrange
        await SeedAccountAsync("401", "Доставчици", AccountType.Liability, CountryCode);
        _factory.PermissionState.Grant(WritePermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        CreateAccountRequest request = new()
        {
            Code = "401",
            Name = "Друго име",
            Type = AccountType.Liability,
            ParentId = null
        };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/accounts", request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(AccountErrorCodes.DUPLICATE_ACCOUNT_CODE));
    }

    /// <summary>Create returns a 400 ProblemDetails with INVALID_PARENT_ACCOUNT when the parent does not exist.</summary>
    [Test]
    public async Task Create_Returns400ProblemDetails_WhenParentDoesNotExist()
    {
        // Arrange
        _factory.PermissionState.Grant(WritePermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        CreateAccountRequest request = new()
        {
            Code = "4011",
            Name = "Подсметка",
            Type = AccountType.Liability,
            ParentId = 987654
        };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/accounts", request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(AccountErrorCodes.INVALID_PARENT_ACCOUNT));
    }

    /// <summary>Create writes the audit row and the transactional-outbox message in the same transaction as the account.</summary>
    [Test]
    public async Task Create_WritesOutboxMessageAndAuditRow_InSameTransaction()
    {
        // Arrange
        _factory.PermissionState.Grant(WritePermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        CreateAccountRequest request = new()
        {
            Code = "501",
            Name = "Разходи за материали",
            Type = AccountType.Expense,
            ParentId = null
        };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/accounts", request);
        AccountDto? created = await response.Content.ReadFromJsonAsync<AccountDto>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(created, Is.Not.Null);

        using IServiceScope scope = _factory.Services.CreateScope();
        AccountsDbContext db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();

        string entityId = created!.Id.ToString(CultureInfo.InvariantCulture);
        OperationsEvent? audit = await db.OperationsEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.EntityType == "Account" && e.EntityId == entityId);
        int outboxCount = await CountOutboxMessagesAsync(db);

        Assert.Multiple(() =>
        {
            Assert.That(audit, Is.Not.Null, "Audit row must be written in the same transaction as the account.");
            Assert.That(audit!.EventType, Is.EqualTo("AccountCreated"));
            Assert.That(audit.AfterJson, Does.Contain("501"));
            Assert.That(outboxCount, Is.GreaterThanOrEqualTo(1), "AccountCreated outbox message must be persisted.");
        });
    }

    /// <summary>Update changes Name and IsActive without changing the immutable Code, Type, or CountryCode.</summary>
    [Test]
    public async Task Update_ChangesNameAndIsActive_DoesNotChangeImmutableFields()
    {
        // Arrange
        int id = await SeedAccountAsync("304", "Стоки", AccountType.Asset, CountryCode);
        _factory.PermissionState.Grant(WritePermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        AccountDto current = (await client.GetFromJsonAsync<AccountDto>($"/api/v1/accounts/{id}"))!;
        UpdateAccountRequest request = new()
        {
            Name = "Стоки на склад",
            IsActive = false,
            RowVersion = current.RowVersion
        };

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync($"/api/v1/accounts/{id}", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Account? persisted = await FindAccountAsync(id);
        Assert.That(persisted, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Name, Is.EqualTo("Стоки на склад"));
            Assert.That(persisted.IsActive, Is.False);
            Assert.That(persisted.Code, Is.EqualTo("304"));
            Assert.That(persisted.Type, Is.EqualTo(AccountType.Asset));
            Assert.That(persisted.CountryCode, Is.EqualTo(CountryCode));
        });
    }

    /// <summary>Update returns a 404 ProblemDetails with ACCOUNT_NOT_FOUND when the account is missing.</summary>
    [Test]
    public async Task Update_Returns404ProblemDetails_WhenAccountDoesNotExist()
    {
        // Arrange
        _factory.PermissionState.Grant(WritePermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        UpdateAccountRequest request = new()
        {
            Name = "Несъществуваща",
            IsActive = true,
            RowVersion = Convert.ToBase64String(BitConverter.GetBytes(1L))
        };

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync("/api/v1/accounts/999999", request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(AccountErrorCodes.ACCOUNT_NOT_FOUND));
    }

    /// <summary>Update returns a 409 ProblemDetails with CONCURRENT_MODIFICATION when the row version is stale.</summary>
    [Test]
    public async Task Update_Returns409ProblemDetails_WhenRowVersionStale()
    {
        // Arrange
        int id = await SeedAccountAsync("304", "Стоки", AccountType.Asset, CountryCode);
        _factory.PermissionState.Grant(WritePermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        AccountDto current = (await client.GetFromJsonAsync<AccountDto>($"/api/v1/accounts/{id}"))!;

        // First update succeeds and advances the row version.
        UpdateAccountRequest first = new()
        {
            Name = "Първа промяна",
            IsActive = true,
            RowVersion = current.RowVersion
        };
        HttpResponseMessage firstResponse = await client.PutAsJsonAsync($"/api/v1/accounts/{id}", first);
        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act: reuse the now-stale original row version.
        UpdateAccountRequest stale = new()
        {
            Name = "Втора промяна",
            IsActive = true,
            RowVersion = current.RowVersion
        };
        HttpResponseMessage response = await client.PutAsJsonAsync($"/api/v1/accounts/{id}", stale);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(CommonErrorCodes.CONCURRENT_MODIFICATION));
    }

    /// <summary>An endpoint returns 403 when the caller lacks the required permission.</summary>
    [Test]
    public async Task Endpoint_Returns403_WhenPermissionMissing()
    {
        // Arrange: grant only read, then call the write endpoint.
        _factory.PermissionState.Grant(ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        CreateAccountRequest request = new()
        {
            Code = "401",
            Name = "Доставчици",
            Type = AccountType.Liability,
            ParentId = null
        };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/accounts", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>Seeds a single account directly via EF and returns its generated identifier.</summary>
    /// <param name="code">The account code.</param>
    /// <param name="name">The account name.</param>
    /// <param name="type">The account type.</param>
    /// <param name="countryCode">The owning country code.</param>
    /// <returns>The surrogate identifier of the seeded account.</returns>
    private async Task<int> SeedAccountAsync(string code, string name, AccountType type, string countryCode)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        AccountsDbContext db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        Account account = new()
        {
            Code = code,
            Name = name,
            Type = type,
            CountryCode = countryCode,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    /// <summary>Seeds several accounts (each an active asset account) for list-ordering scenarios.</summary>
    /// <param name="accounts">The (code, countryCode) pairs to seed.</param>
    private async Task SeedAccountsAsync(params (string Code, string Country)[] accounts)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        AccountsDbContext db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        foreach ((string code, string country) in accounts)
        {
            db.Accounts.Add(new Account
            {
                Code = code,
                Name = "Account " + code,
                Type = AccountType.Asset,
                CountryCode = country,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Seeds several active BG accounts with explicit names for search scenarios.</summary>
    /// <param name="accounts">The (code, name) pairs to seed.</param>
    private async Task SeedNamedAccountsAsync(params (string Code, string Name)[] accounts)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        AccountsDbContext db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        foreach ((string code, string name) in accounts)
        {
            db.Accounts.Add(new Account
            {
                Code = code,
                Name = name,
                Type = AccountType.Asset,
                CountryCode = CountryCode,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Runs <see cref="DatabaseResetter.ResetAsync"/> with retries on SQL deadlock (error 1205), which
    /// can arise when Respawn's bulk DELETE races the background bus-outbox delivery service.
    /// </summary>
    private async Task ResetWithDeadlockRetryAsync()
    {
        const int maxAttempts = 5;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await _resetter.ResetAsync();
                return;
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205 && attempt < maxAttempts)
            {
                await Task.Delay(200 * attempt);
            }
        }
    }

    /// <summary>Loads an account by id with no tracking, returning <see langword="null"/> when absent.</summary>
    /// <param name="id">The account identifier.</param>
    /// <returns>The persisted account, or <see langword="null"/>.</returns>
    private async Task<Account?> FindAccountAsync(int id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        AccountsDbContext db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        return await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id);
    }

    /// <summary>Counts the rows currently in the MassTransit EF Core outbox table.</summary>
    /// <param name="db">The accounts database context.</param>
    /// <returns>The number of pending outbox messages.</returns>
    private static async Task<int> CountOutboxMessagesAsync(AccountsDbContext db)
    {
        return await db.Set<OutboxMessage>().AsNoTracking().CountAsync();
    }
}
