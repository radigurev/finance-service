using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Audit.Models;
using Finance.Nomenclature.API.Auditing;
using Finance.Nomenclature.API.Caching;
using Finance.Nomenclature.API.Tests.Builders;
using Finance.Nomenclature.API.Tests.Fixtures;
using Finance.Nomenclature.DBModel.Models;
using Finance.ServiceModel.Events.Nomenclature;
using Finance.ServiceModel.Nomenclature;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;

namespace Finance.Nomenclature.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="Finance.Nomenclature.API.Services.CurrencyService"/> covering the
/// List/Get/Create/Update/Deactivate result paths, cross-aggregate duplicate-code validation, immutable
/// ISO code on update, default IsoCode ordering, audit-first ordering, domain-event publication, and
/// cache invalidation (SDD-NOM-001 §2.1, §2.6, §6). Runs fully offline against a SQLite in-memory
/// <c>NomenclatureDbContext</c> with faked cache, audit, and publish dependencies.
/// </summary>
[TestFixture]
[Category("SDD-NOM-001")]
public sealed class CurrencyServiceTests
{
    private SqliteNomenclatureDbContextScope _scope = null!;
    private CurrencyServiceTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteNomenclatureDbContextFactory.Create();
        _harness = CurrencyServiceTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    /// <summary>Get-by-iso-code for a missing currency returns a CURRENCY_NOT_FOUND failure (§2.1).</summary>
    [Test]
    public async Task GetByIsoCodeAsync_UnknownCode_ReturnsCurrencyNotFound()
    {
        // Arrange & Act
        Result<CurrencyDto> result = await _harness.Service.GetByIsoCodeAsync("ZZZ", CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("CURRENCY_NOT_FOUND"));
        });
    }

    /// <summary>Get-by-iso-code returns the currency when it exists (§2.1).</summary>
    [Test]
    public async Task GetByIsoCodeAsync_ExistingCode_ReturnsCurrency()
    {
        // Arrange
        Currency seeded = await SeedAsync(CurrencyBuilder.Create().WithIsoCode("EUR").WithName("Euro"));

        // Act
        Result<CurrencyDto> result = await _harness.Service.GetByIsoCodeAsync("EUR", CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Id, Is.EqualTo(seeded.Id));
            Assert.That(result.Value.IsoCode, Is.EqualTo("EUR"));
            Assert.That(result.Value.Name, Is.EqualTo("Euro"));
        });
    }

    /// <summary>SearchAsync defaults to ascending IsoCode ordering when no sort is supplied (§2.1).</summary>
    [Test]
    public async Task SearchAsync_DefaultOrder_ReturnsCurrenciesOrderedByIsoCode()
    {
        // Arrange
        await SeedAsync(CurrencyBuilder.Create().WithIsoCode("USD"));
        await SeedAsync(CurrencyBuilder.Create().WithIsoCode("BGN"));
        await SeedAsync(CurrencyBuilder.Create().WithIsoCode("EUR"));
        FilterRequest request = new() { Page = 1, PageSize = 50 };

        // Act
        Result<PagedResult<CurrencyDto>> result =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        IReadOnlyList<CurrencyDto> items = result.Value!.Items;
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items[0].IsoCode, Is.EqualTo("BGN"));
            Assert.That(items[1].IsoCode, Is.EqualTo("EUR"));
            Assert.That(items[2].IsoCode, Is.EqualTo("USD"));
        });
    }

    /// <summary>SearchAsync includes both active and inactive currencies (§2.1).</summary>
    [Test]
    public async Task SearchAsync_ReturnsActiveAndInactive()
    {
        // Arrange
        await SeedAsync(CurrencyBuilder.Create().WithIsoCode("BGN").WithIsActive(true));
        await SeedAsync(CurrencyBuilder.Create().WithIsoCode("EUR").WithIsActive(false));
        FilterRequest request = new() { Page = 1, PageSize = 50 };

        // Act
        Result<PagedResult<CurrencyDto>> result =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.TotalCount, Is.EqualTo(2));
            Assert.That(result.Value.Items, Has.Some.Matches<CurrencyDto>(c => !c.IsActive));
            Assert.That(result.Value.Items, Has.Some.Matches<CurrencyDto>(c => c.IsActive));
        });
    }

    /// <summary>SearchAsync honours a client-supplied sort over the default IsoCode ordering (§2.1).</summary>
    [Test]
    public async Task SearchAsync_AppliesClientSort_OverDefaultOrder()
    {
        // Arrange
        await SeedAsync(CurrencyBuilder.Create().WithIsoCode("BGN"));
        await SeedAsync(CurrencyBuilder.Create().WithIsoCode("EUR"));
        await SeedAsync(CurrencyBuilder.Create().WithIsoCode("USD"));
        FilterRequest request = new()
        {
            Sort = [new SortCriterion { Field = "IsoCode", Direction = "desc" }],
            Page = 1,
            PageSize = 50
        };

        // Act
        Result<PagedResult<CurrencyDto>> result =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Items[0].IsoCode, Is.EqualTo("USD"));
            Assert.That(result.Value.Items[2].IsoCode, Is.EqualTo("BGN"));
        });
    }

    /// <summary>Create persists a new currency and returns it with a populated row version (§2.1).</summary>
    [Test]
    public async Task CreateAsync_Valid_PersistsCurrency()
    {
        // Arrange
        CreateCurrencyRequest request = CreateCurrencyRequestBuilder.Create().WithIsoCode("USD").Build();

        // Act
        Result<CurrencyDto> result = await _harness.Service.CreateAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Id, Is.GreaterThan(0));
            Assert.That(result.Value.IsoCode, Is.EqualTo("USD"));
            Assert.That(result.Value.IsActive, Is.True);
            Assert.That(result.Value.RowVersion, Is.Not.Empty);
        });
    }

    /// <summary>Create returns DUPLICATE_CURRENCY_CODE when the ISO code already exists (§2.1, §3).</summary>
    [Test]
    public async Task CreateAsync_ReturnsDuplicateCurrencyCode_WhenIsoCodeExists()
    {
        // Arrange
        await SeedAsync(CurrencyBuilder.Create().WithIsoCode("USD"));
        CreateCurrencyRequest request = CreateCurrencyRequestBuilder.Create().WithIsoCode("USD").Build();

        // Act
        Result<CurrencyDto> result = await _harness.Service.CreateAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("DUPLICATE_CURRENCY_CODE"));
        });
    }

    /// <summary>Create records an audit Create entry before publishing the outbox event (§2.1).</summary>
    [Test]
    public async Task CreateAsync_Valid_RecordsAuditBeforeOutboxAndInvalidatesCache()
    {
        // Arrange
        CreateCurrencyRequest request = CreateCurrencyRequestBuilder.Create().WithIsoCode("USD").Build();

        // Act
        Result<CurrencyDto> created = await _harness.Service.CreateAsync(request, CancellationToken.None);

        // Assert
        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        AuditEntry recorded = _harness.RecordedAudits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(recorded.Operation, Is.EqualTo(AuditOperation.Create));
            Assert.That(recorded.EventType, Is.EqualTo(CurrencyAuditEventTypes.CurrencyCreated));
            Assert.That(recorded.BeforeJson, Is.Null);
            Assert.That(_harness.PublishedEvents.Single(), Is.TypeOf<CurrencyCreatedEvent>());
        });
        _harness.CurrencyListCacheMock.Verify(
            c => c.RemoveByPatternAsync(CurrencyCacheKeys.InvalidationPattern, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>Create publishes CurrencyCreatedEvent carrying the ambient correlation id (§2.1).</summary>
    [Test]
    public async Task CreateAsync_PublishesCurrencyCreatedEvent_WithCorrelationId()
    {
        // Arrange
        CreateCurrencyRequest request = CreateCurrencyRequestBuilder.Create().WithIsoCode("USD").Build();

        // Act
        Result<CurrencyDto> result = await _harness.Service.CreateAsync(request, CancellationToken.None);

        // Assert
        CurrencyCreatedEvent published = (CurrencyCreatedEvent)_harness.PublishedEvents.Single();
        Assert.Multiple(() =>
        {
            Assert.That(published.CorrelationId, Is.EqualTo(StubCorrelationIdAccessor.CorrelationId));
            Assert.That(published.MessageId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(published.CurrencyId, Is.EqualTo(result.Value!.Id));
            Assert.That(published.IsoCode, Is.EqualTo("USD"));
        });
        _harness.PublishMock.Verify(
            p => p.Publish(It.IsAny<CurrencyCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Update changes Name/Symbol but leaves the immutable ISO code untouched (§2.1, §2.6).</summary>
    [Test]
    public async Task UpdateAsync_AttemptToChangeIsoCode_IsRejected()
    {
        // Arrange
        Currency seeded = await SeedAsync(CurrencyBuilder.Create().WithIsoCode("BGN").WithName("Bulgarian Lev"));
        UpdateCurrencyRequest request = UpdateCurrencyRequestBuilder.Create()
            .WithName("Renamed Lev")
            .WithRowVersion(Convert.ToBase64String(seeded.RowVersion))
            .Build();

        // Act
        Result<CurrencyDto> result =
            await _harness.Service.UpdateAsync("BGN", request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.IsoCode, Is.EqualTo("BGN"));
            Assert.That(result.Value.Name, Is.EqualTo("Renamed Lev"));
            Assert.That(result.Value.Id, Is.EqualTo(seeded.Id));
        });
    }

    /// <summary>Update on a missing currency returns a CURRENCY_NOT_FOUND failure (§2.1).</summary>
    [Test]
    public async Task UpdateAsync_UnknownCode_ReturnsCurrencyNotFound()
    {
        // Arrange
        UpdateCurrencyRequest request = UpdateCurrencyRequestBuilder.Create().Build();

        // Act
        Result<CurrencyDto> result =
            await _harness.Service.UpdateAsync("ZZZ", request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("CURRENCY_NOT_FOUND"));
        });
    }

    /// <summary>A non-deactivating update publishes CurrencyUpdatedEvent + audit Update (§2.1).</summary>
    [Test]
    public async Task UpdateAsync_PublishesCurrencyUpdatedEvent_AndAuditUpdate_WhenNameChanged()
    {
        // Arrange
        Currency seeded = await SeedAsync(CurrencyBuilder.Create().WithIsoCode("BGN").WithName("Lev"));
        UpdateCurrencyRequest request = UpdateCurrencyRequestBuilder.Create()
            .WithName("Bulgarian Lev")
            .WithIsActive(true)
            .WithRowVersion(Convert.ToBase64String(seeded.RowVersion))
            .Build();

        // Act
        Result<CurrencyDto> result =
            await _harness.Service.UpdateAsync("BGN", request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        AuditEntry recorded = _harness.RecordedAudits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(recorded.Operation, Is.EqualTo(AuditOperation.Update));
            Assert.That(recorded.EventType, Is.EqualTo(CurrencyAuditEventTypes.CurrencyUpdated));
            Assert.That(recorded.BeforeJson, Is.Not.Null);
            Assert.That(_harness.PublishedEvents.Single(), Is.TypeOf<CurrencyUpdatedEvent>());
        });
    }

    /// <summary>Deactivating (IsActive true→false) publishes CurrencyDeactivatedEvent + audit StateChange with a reason (§2.1).</summary>
    [Test]
    public async Task UpdateAsync_DeactivatesCurrency_PublishesDeactivatedEvent_AndAuditWithReason()
    {
        // Arrange
        Currency seeded = await SeedAsync(CurrencyBuilder.Create().WithIsoCode("BGN").WithIsActive(true));
        UpdateCurrencyRequest request = UpdateCurrencyRequestBuilder.Create()
            .WithName(seeded.Name)
            .WithIsActive(false)
            .WithRowVersion(Convert.ToBase64String(seeded.RowVersion))
            .Build();

        // Act
        Result<CurrencyDto> result =
            await _harness.Service.UpdateAsync("BGN", request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        AuditEntry recorded = _harness.RecordedAudits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(recorded.Operation, Is.EqualTo(AuditOperation.StateChange));
            Assert.That(recorded.EventType, Is.EqualTo(CurrencyAuditEventTypes.CurrencyDeactivated));
            Assert.That(recorded.Reason, Is.Not.Null.And.Not.Empty);
            Assert.That(recorded.BeforeJson, Is.Not.Null);
            Assert.That(_harness.PublishedEvents.Single(), Is.TypeOf<CurrencyDeactivatedEvent>());
            Assert.That(result.Value!.IsActive, Is.False);
        });
    }

    /// <summary>Re-activating a soft-deleted currency publishes CurrencyUpdatedEvent on the same row (§2.6).</summary>
    [Test]
    public async Task UpdateAsync_ReactivatesSoftDeletedCurrency_PublishesUpdatedEvent()
    {
        // Arrange
        Currency seeded = await SeedAsync(CurrencyBuilder.Create().WithIsoCode("BGN").WithIsActive(false));
        UpdateCurrencyRequest request = UpdateCurrencyRequestBuilder.Create()
            .WithName(seeded.Name)
            .WithIsActive(true)
            .WithRowVersion(Convert.ToBase64String(seeded.RowVersion))
            .Build();

        // Act
        Result<CurrencyDto> result =
            await _harness.Service.UpdateAsync("BGN", request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Id, Is.EqualTo(seeded.Id));
            Assert.That(result.Value.IsActive, Is.True);
            Assert.That(_harness.PublishedEvents.Single(), Is.TypeOf<CurrencyUpdatedEvent>());
        });
    }

    /// <summary>Update invalidates the bounded finance-nomenclature cache region on success (§2.1).</summary>
    [Test]
    public async Task UpdateAsync_InvalidatesFinanceNomenclatureCacheRegion()
    {
        // Arrange
        Currency seeded = await SeedAsync(CurrencyBuilder.Create().WithIsoCode("BGN"));
        UpdateCurrencyRequest request = UpdateCurrencyRequestBuilder.Create()
            .WithName("Renamed")
            .WithRowVersion(Convert.ToBase64String(seeded.RowVersion))
            .Build();

        // Act
        await _harness.Service.UpdateAsync("BGN", request, CancellationToken.None);

        // Assert
        _harness.CurrencyListCacheMock.Verify(
            c => c.RemoveByPatternAsync(CurrencyCacheKeys.InvalidationPattern, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>Update with a malformed (non-base64) row version yields CONCURRENT_MODIFICATION (§2.1).</summary>
    [Test]
    public async Task UpdateAsync_ReturnsConcurrentModification_WhenRowVersionMalformed()
    {
        // Arrange
        await SeedAsync(CurrencyBuilder.Create().WithIsoCode("BGN"));
        UpdateCurrencyRequest request = UpdateCurrencyRequestBuilder.Create()
            .WithName("Renamed")
            .WithRowVersion("!!!not-base64!!!")
            .Build();

        // Act
        Result<CurrencyDto> result =
            await _harness.Service.UpdateAsync("BGN", request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("CONCURRENT_MODIFICATION"));
        });
    }

    /// <summary>GetActiveAsync returns only active currencies ordered by IsoCode (§2.1).</summary>
    [Test]
    public async Task GetActiveAsync_ReturnsOnlyActiveCurrencies_OrderedByIsoCode()
    {
        // Arrange
        await SeedAsync(CurrencyBuilder.Create().WithIsoCode("USD").WithIsActive(true));
        await SeedAsync(CurrencyBuilder.Create().WithIsoCode("EUR").WithIsActive(false));
        await SeedAsync(CurrencyBuilder.Create().WithIsoCode("BGN").WithIsActive(true));

        // Act
        Result<IReadOnlyList<CurrencyDto>> result =
            await _harness.Service.GetActiveAsync(CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Has.Count.EqualTo(2));
            Assert.That(result.Value, Has.All.Matches<CurrencyDto>(c => c.IsActive));
            Assert.That(result.Value![0].IsoCode, Is.EqualTo("BGN"));
            Assert.That(result.Value[1].IsoCode, Is.EqualTo("USD"));
        });
    }

    private async Task<Currency> SeedAsync(CurrencyBuilder builder)
    {
        Currency currency = builder.Build();
        _scope.Context.Currencies.Add(currency);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        _scope.Context.Entry(currency).State = EntityState.Detached;
        return currency;
    }
}
