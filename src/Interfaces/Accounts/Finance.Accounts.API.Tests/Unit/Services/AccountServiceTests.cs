using Finance.Accounts.API.Auditing;
using Finance.Accounts.API.Caching;
using Finance.Accounts.API.Tests.Builders;
using Finance.Accounts.API.Tests.Fixtures;
using Finance.Accounts.DBModel.Models;
using Finance.Common.Enums;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Audit.Models;
using Finance.ServiceModel.Accounts;
using Finance.ServiceModel.Events.Accounts;
using MassTransit;
using Moq;
using NUnit.Framework;

namespace Finance.Accounts.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="Finance.Accounts.API.Services.AccountService"/> covering the List/Get/Create/
/// Update/Deactivate result paths, cross-aggregate validation, audit-first ordering, domain-event
/// publication, cache invalidation, and optimistic concurrency (SDD-ACCT-001 §6.1). Runs fully offline
/// against a SQLite in-memory <c>AccountsDbContext</c> with faked cache, audit, and publish dependencies.
/// </summary>
[TestFixture]
[Category("SDD-ACCT-001")]
public sealed class AccountServiceTests
{
    private SqliteAccountsDbContextScope _scope = null!;
    private AccountServiceTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteAccountsDbContextFactory.Create();
        _harness = AccountServiceTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    /// <summary>Get-by-id for a missing account returns an ACCOUNT_NOT_FOUND failure (SDD-ACCT-001 §2.2).</summary>
    [Test]
    public async Task GetAsync_ReturnsNotFoundResult_WhenAccountDoesNotExist()
    {
        // Arrange & Act
        Result<AccountDto> result = await _harness.Service.GetAsync(9999, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("ACCOUNT_NOT_FOUND"));
        });
    }

    /// <summary>Get-by-id returns the account when it exists (SDD-ACCT-001 §2.2).</summary>
    [Test]
    public async Task GetAsync_ReturnsAccount_WhenExists()
    {
        // Arrange
        Account seeded = await SeedAsync(AccountBuilder.Create().WithCode("501").WithName("Каса"));

        // Act
        Result<AccountDto> result = await _harness.Service.GetAsync(seeded.Id, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Id, Is.EqualTo(seeded.Id));
            Assert.That(result.Value.Code, Is.EqualTo("501"));
            Assert.That(result.Value.Name, Is.EqualTo("Каса"));
        });
    }

    /// <summary>SearchAsync defaults to CountryCode-then-Code ordering when no sort is supplied (§2.1).</summary>
    [Test]
    public async Task SearchAsync_ReturnsPagedResultOrderedByCountryThenCode()
    {
        // Arrange
        await SeedAsync(AccountBuilder.Create().WithCountryCode("DE").WithCode("100"));
        await SeedAsync(AccountBuilder.Create().WithCountryCode("BG").WithCode("501"));
        await SeedAsync(AccountBuilder.Create().WithCountryCode("BG").WithCode("304"));
        FilterRequest request = new() { Page = 1, PageSize = 50 };

        // Act
        Result<PagedResult<AccountDto>> result =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        IReadOnlyList<AccountDto> items = result.Value!.Items;
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items[0].CountryCode, Is.EqualTo("BG"));
            Assert.That(items[0].Code, Is.EqualTo("304"));
            Assert.That(items[1].CountryCode, Is.EqualTo("BG"));
            Assert.That(items[1].Code, Is.EqualTo("501"));
            Assert.That(items[2].CountryCode, Is.EqualTo("DE"));
        });
    }

    /// <summary>SearchAsync includes inactive accounts when no IsActive filter is applied (§2.1).</summary>
    [Test]
    public async Task SearchAsync_IncludesInactiveAccounts_WhenNoFilterApplied()
    {
        // Arrange
        await SeedAsync(AccountBuilder.Create().WithCode("304").WithIsActive(true));
        await SeedAsync(AccountBuilder.Create().WithCode("401").WithIsActive(false));
        FilterRequest request = new() { Page = 1, PageSize = 50 };

        // Act
        Result<PagedResult<AccountDto>> result =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.TotalCount, Is.EqualTo(2));
            Assert.That(result.Value.Items, Has.Some.Matches<AccountDto>(a => !a.IsActive));
        });
    }

    /// <summary>SearchAsync rejects a page size above the SDD-INFRA-005 cap of 200 (§2.1).</summary>
    [Test]
    public async Task SearchAsync_CapsPageSizeAt200()
    {
        // Arrange
        FilterRequest request = new() { Page = 1, PageSize = 201 };

        // Act
        Result<PagedResult<AccountDto>> result =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("PAGE_SIZE_TOO_LARGE"));
        });
    }

    /// <summary>SearchAsync applies a client-supplied filter and sort from the request (§2.1).</summary>
    [Test]
    public async Task SearchAsync_AppliesFilterAndSort_FromRequest()
    {
        // Arrange
        await SeedAsync(AccountBuilder.Create().WithCode("304").WithType(AccountType.Asset));
        await SeedAsync(AccountBuilder.Create().WithCode("401").WithType(AccountType.Liability));
        await SeedAsync(AccountBuilder.Create().WithCode("411").WithType(AccountType.Asset));
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Type", Operator = "eq", Value = "Asset" }],
            Sort = [new SortCriterion { Field = "Code", Direction = "desc" }],
            Page = 1,
            PageSize = 50
        };

        // Act
        Result<PagedResult<AccountDto>> result =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.TotalCount, Is.EqualTo(2));
            Assert.That(result.Value.Items[0].Code, Is.EqualTo("411"));
            Assert.That(result.Value.Items[1].Code, Is.EqualTo("304"));
        });
    }

    /// <summary>Create persists the account with IsActive defaulted to true (SDD-ACCT-001 §2.3).</summary>
    [Test]
    public async Task CreateAsync_PersistsAccount_WithDefaultIsActiveTrue()
    {
        // Arrange
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithCode("401").Build();

        // Act
        Result<AccountDto> result = await _harness.Service.CreateAsync(
            request, AccountServiceTestHarness.CountryCode, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.IsActive, Is.True);
            Assert.That(result.Value.Id, Is.GreaterThan(0));
            Assert.That(result.Value.RowVersion, Is.Not.Empty);
        });
    }

    /// <summary>Create stamps the owning country code passed from configuration (SDD-ACCT-001 §2.3, §2.6).</summary>
    [Test]
    public async Task CreateAsync_SetsCountryCodeFromConfiguration()
    {
        // Arrange
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithCode("501").Build();

        // Act
        Result<AccountDto> result = await _harness.Service.CreateAsync(
            request, AccountServiceTestHarness.CountryCode, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.CountryCode, Is.EqualTo(AccountServiceTestHarness.CountryCode));
    }

    /// <summary>Create returns DUPLICATE_ACCOUNT_CODE when the code already exists in the country (§2.3).</summary>
    [Test]
    public async Task CreateAsync_ReturnsDuplicateAccountCodeFailure_WhenCodeExistsInSameCountry()
    {
        // Arrange
        await SeedAsync(AccountBuilder.Create().WithCountryCode("BG").WithCode("401"));
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithCode("401").Build();

        // Act
        Result<AccountDto> result = await _harness.Service.CreateAsync(
            request, AccountServiceTestHarness.CountryCode, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("DUPLICATE_ACCOUNT_CODE"));
        });
    }

    /// <summary>Create returns INVALID_PARENT_ACCOUNT when the parent is missing (SDD-ACCT-001 §2.3).</summary>
    [Test]
    public async Task CreateAsync_ReturnsInvalidParentAccountFailure_WhenParentMissing()
    {
        // Arrange
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithCode("4011").WithParentId(7777).Build();

        // Act
        Result<AccountDto> result = await _harness.Service.CreateAsync(
            request, AccountServiceTestHarness.CountryCode, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("INVALID_PARENT_ACCOUNT"));
        });
    }

    /// <summary>Create returns INVALID_PARENT_ACCOUNT when the parent is in a different country (§2.3).</summary>
    [Test]
    public async Task CreateAsync_ReturnsInvalidParentAccountFailure_WhenParentWrongCountry()
    {
        // Arrange
        Account foreignParent = await SeedAsync(AccountBuilder.Create().WithCountryCode("DE").WithCode("100"));
        CreateAccountRequest request =
            CreateAccountRequestBuilder.Create().WithCode("4011").WithParentId(foreignParent.Id).Build();

        // Act
        Result<AccountDto> result = await _harness.Service.CreateAsync(
            request, AccountServiceTestHarness.CountryCode, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("INVALID_PARENT_ACCOUNT"));
        });
    }

    /// <summary>Create records an audit Create entry before publishing the outbox event (§2.3, §2.9).</summary>
    [Test]
    public async Task CreateAsync_RecordsAuditCreate_BeforeOutboxPublish()
    {
        // Arrange
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithCode("401").Build();

        // Act
        Result<AccountDto> created = await _harness.Service.CreateAsync(
            request, AccountServiceTestHarness.CountryCode, CancellationToken.None);

        // Assert
        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        AuditEntry recorded = _harness.RecordedAudits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(recorded.Operation, Is.EqualTo(AuditOperation.Create));
            Assert.That(recorded.EventType, Is.EqualTo(AccountAuditEventTypes.AccountCreated));
            Assert.That(recorded.BeforeJson, Is.Null);
            Assert.That(_harness.PublishedEvents.Single(), Is.TypeOf<AccountCreatedEvent>());
        });
        _harness.AuditMock.Verify(
            a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>Create publishes AccountCreatedEvent carrying the ambient correlation id (§2.8).</summary>
    [Test]
    public async Task CreateAsync_PublishesAccountCreatedEvent_WithCorrelationId()
    {
        // Arrange
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithCode("401").Build();

        // Act
        Result<AccountDto> result = await _harness.Service.CreateAsync(
            request, AccountServiceTestHarness.CountryCode, CancellationToken.None);

        // Assert
        AccountCreatedEvent published = (AccountCreatedEvent)_harness.PublishedEvents.Single();
        Assert.Multiple(() =>
        {
            Assert.That(published.CorrelationId, Is.EqualTo(StubCorrelationIdAccessor.CorrelationId));
            Assert.That(published.MessageId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(published.AccountId, Is.EqualTo(result.Value!.Id));
            Assert.That(published.Code, Is.EqualTo("401"));
        });
        _harness.PublishMock.Verify(
            p => p.Publish(It.IsAny<AccountCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Create invalidates the bounded finance-accounts cache region (SDD-ACCT-001 §2.7).</summary>
    [Test]
    public async Task CreateAsync_InvalidatesFinanceAccountsCacheRegion()
    {
        // Arrange
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithCode("401").Build();

        // Act
        await _harness.Service.CreateAsync(
            request, AccountServiceTestHarness.CountryCode, CancellationToken.None);

        // Assert
        _harness.AccountCacheMock.Verify(
            c => c.RemoveByPatternAsync(AccountCacheKeys.InvalidationPattern, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>Update changes Name and IsActive but leaves immutable fields untouched (§2.4).</summary>
    [Test]
    public async Task UpdateAsync_ChangesNameAndIsActive_DoesNotChangeImmutableFields()
    {
        // Arrange
        Account seeded = await SeedAsync(
            AccountBuilder.Create().WithCode("401").WithName("Доставчици").WithType(AccountType.Liability));
        string rowVersion = Convert.ToBase64String(seeded.RowVersion);
        UpdateAccountRequest request = UpdateAccountRequestBuilder.Create()
            .WithName("Доставчици (renamed)")
            .WithIsActive(true)
            .WithRowVersion(rowVersion)
            .Build();

        // Act
        Result<AccountDto> result = await _harness.Service.UpdateAsync(seeded.Id, request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Name, Is.EqualTo("Доставчици (renamed)"));
            Assert.That(result.Value.Code, Is.EqualTo("401"));
            Assert.That(result.Value.Type, Is.EqualTo(AccountType.Liability));
            Assert.That(result.Value.CountryCode, Is.EqualTo("BG"));
        });
    }

    /// <summary>Update on a missing account returns an ACCOUNT_NOT_FOUND failure (SDD-ACCT-001 §2.4).</summary>
    [Test]
    public async Task UpdateAsync_ReturnsNotFoundResult_WhenAccountDoesNotExist()
    {
        // Arrange
        UpdateAccountRequest request = UpdateAccountRequestBuilder.Create()
            .WithRowVersion(Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }))
            .Build();

        // Act
        Result<AccountDto> result = await _harness.Service.UpdateAsync(9999, request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("ACCOUNT_NOT_FOUND"));
        });
    }

    /// <summary>Deactivating (IsActive true→false) publishes AccountDeactivatedEvent + audit StateChange (§2.5).</summary>
    [Test]
    public async Task UpdateAsync_PublishesAccountDeactivatedEvent_AndAuditStateChange_WhenIsActiveSetFalse()
    {
        // Arrange
        Account seeded = await SeedAsync(AccountBuilder.Create().WithCode("401").WithIsActive(true));
        UpdateAccountRequest request = UpdateAccountRequestBuilder.Create()
            .WithName(seeded.Name)
            .WithIsActive(false)
            .WithRowVersion(Convert.ToBase64String(seeded.RowVersion))
            .Build();

        // Act
        Result<AccountDto> result = await _harness.Service.UpdateAsync(seeded.Id, request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        AuditEntry recorded = _harness.RecordedAudits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(recorded.Operation, Is.EqualTo(AuditOperation.StateChange));
            Assert.That(recorded.Reason, Is.Not.Null.And.Not.Empty);
            Assert.That(recorded.BeforeJson, Is.Not.Null);
            Assert.That(_harness.PublishedEvents.Single(), Is.TypeOf<AccountDeactivatedEvent>());
            Assert.That(result.Value!.IsActive, Is.False);
        });
    }

    /// <summary>A non-deactivating update publishes AccountUpdatedEvent + audit Update (§2.4, §2.9).</summary>
    [Test]
    public async Task UpdateAsync_PublishesAccountUpdatedEvent_AndAuditUpdate_WhenNameChanged()
    {
        // Arrange
        Account seeded = await SeedAsync(AccountBuilder.Create().WithCode("401").WithName("Доставчици"));
        UpdateAccountRequest request = UpdateAccountRequestBuilder.Create()
            .WithName("Доставчици (v2)")
            .WithIsActive(true)
            .WithRowVersion(Convert.ToBase64String(seeded.RowVersion))
            .Build();

        // Act
        Result<AccountDto> result = await _harness.Service.UpdateAsync(seeded.Id, request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        AuditEntry recorded = _harness.RecordedAudits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(recorded.Operation, Is.EqualTo(AuditOperation.Update));
            Assert.That(recorded.EventType, Is.EqualTo(AccountAuditEventTypes.AccountUpdated));
            Assert.That(recorded.BeforeJson, Is.Not.Null);
            Assert.That(_harness.PublishedEvents.Single(), Is.TypeOf<AccountUpdatedEvent>());
        });
    }

    /// <summary>Update invalidates the bounded finance-accounts cache region on success (§2.7).</summary>
    [Test]
    public async Task UpdateAsync_InvalidatesFinanceAccountsCacheRegion()
    {
        // Arrange
        Account seeded = await SeedAsync(AccountBuilder.Create().WithCode("401"));
        UpdateAccountRequest request = UpdateAccountRequestBuilder.Create()
            .WithName("Renamed")
            .WithRowVersion(Convert.ToBase64String(seeded.RowVersion))
            .Build();

        // Act
        await _harness.Service.UpdateAsync(seeded.Id, request, CancellationToken.None);

        // Assert
        _harness.AccountCacheMock.Verify(
            c => c.RemoveByPatternAsync(AccountCacheKeys.InvalidationPattern, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>Update with a malformed (non-base64) row version yields CONCURRENT_MODIFICATION (§2.10).</summary>
    [Test]
    public async Task UpdateAsync_ReturnsConcurrentModificationFailure_WhenRowVersionMalformed()
    {
        // Arrange
        Account seeded = await SeedAsync(AccountBuilder.Create().WithCode("401"));
        UpdateAccountRequest request = UpdateAccountRequestBuilder.Create()
            .WithName("Renamed")
            .WithRowVersion("!!!not-base64!!!")
            .Build();

        // Act
        Result<AccountDto> result = await _harness.Service.UpdateAsync(seeded.Id, request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("CONCURRENT_MODIFICATION"));
        });
    }

    /// <summary>Update with a stale (but well-formed) row version yields CONCURRENT_MODIFICATION (§2.10).</summary>
    [Test]
    public async Task UpdateAsync_ReturnsConcurrentModificationFailure_WhenRowVersionStale()
    {
        // Arrange
        Account seeded = await SeedAsync(AccountBuilder.Create().WithCode("401"));
        string staleButValid = Convert.ToBase64String(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 });
        UpdateAccountRequest request = UpdateAccountRequestBuilder.Create()
            .WithName("Renamed")
            .WithRowVersion(staleButValid)
            .Build();

        // Act
        Result<AccountDto> result = await _harness.Service.UpdateAsync(seeded.Id, request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("CONCURRENT_MODIFICATION"));
        });
    }

    /// <summary>GetActiveChart returns only active accounts ordered by CountryCode then Code (§2.7).</summary>
    [Test]
    public async Task GetActiveChartAsync_ReturnsOnlyActiveAccounts_Ordered()
    {
        // Arrange
        await SeedAsync(AccountBuilder.Create().WithCode("304").WithIsActive(true));
        await SeedAsync(AccountBuilder.Create().WithCode("401").WithIsActive(false));
        await SeedAsync(AccountBuilder.Create().WithCode("501").WithIsActive(true));

        // Act
        Result<IReadOnlyList<AccountDto>> result =
            await _harness.Service.GetActiveChartAsync(CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Has.Count.EqualTo(2));
            Assert.That(result.Value, Has.All.Matches<AccountDto>(a => a.IsActive));
            Assert.That(result.Value![0].Code, Is.EqualTo("304"));
            Assert.That(result.Value[1].Code, Is.EqualTo("501"));
        });
    }

    private async Task<Account> SeedAsync(AccountBuilder builder)
    {
        Account account = builder.Build();
        _scope.Context.Accounts.Add(account);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        _scope.Context.Entry(account).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        return account;
    }
}
