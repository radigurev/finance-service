using AutoMapper;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Tests.Services.Fixtures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Finance.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for <see cref="Finance.Infrastructure.Services.BaseEntityService{TContext}.FindOrNotFoundAsync{TEntity}"/>
/// and <see cref="Finance.Infrastructure.Services.SearchableServiceBase{TEntity, TDto, TContext}.SearchAsync"/>,
/// run against a kept-alive SQLite in-memory database (SDD-INFRA-009 §2.1, §2.2).
/// </summary>
[TestFixture]
[Category("SDD-INFRA-009")]
public sealed class SearchableServiceBaseTests
{
    private SqliteConnection _connection = null!;
    private SampleDbContext _context = null!;
    private IMapper _mapper = null!;

    /// <summary>Opens a shared in-memory connection, creates the schema, and seeds rows.</summary>
    [SetUp]
    public async Task SetUp()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync(CancellationToken.None);

        DbContextOptions<SampleDbContext> options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new SampleDbContext(options);
        await _context.Database.EnsureCreatedAsync(CancellationToken.None);

        _context.Samples.AddRange(
            new SampleEntity { Name = "Alpha", IsActive = true },
            new SampleEntity { Name = "Beta", IsActive = true },
            new SampleEntity { Name = "Gamma", IsActive = false });
        await _context.SaveChangesAsync(CancellationToken.None);

        MapperConfiguration config = new(cfg => cfg.AddProfile<SampleMappingProfile>());
        _mapper = config.CreateMapper();
    }

    /// <summary>Disposes the context and the kept-alive connection.</summary>
    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>A present entity is returned successfully.</summary>
    [Test]
    public async Task FindOrNotFoundAsync_ReturnsEntity_WhenPresent()
    {
        // Arrange
        SampleSearchService service = new(_context, _mapper, new TestCorrelationIdAccessor());

        // Act
        Result<SampleEntity> result = await service.FindAsync(1, "SAMPLE_NOT_FOUND", CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value!.Name, Is.EqualTo("Alpha"));
        });
    }

    /// <summary>A missing entity yields a failure with the supplied not-found code.</summary>
    [Test]
    public async Task FindOrNotFoundAsync_ReturnsNotFoundFailure_WhenMissing()
    {
        // Arrange
        SampleSearchService service = new(_context, _mapper, new TestCorrelationIdAccessor());

        // Act
        Result<SampleEntity> result = await service.FindAsync(999, "SAMPLE_NOT_FOUND", CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("SAMPLE_NOT_FOUND"));
        });
    }

    /// <summary>SearchAsync applies the filter, counts before paging, and projects to the DTO.</summary>
    [Test]
    public async Task SearchAsync_AppliesFilterPaginationAndProjection()
    {
        // Arrange
        SampleSearchService service = new(_context, _mapper, new TestCorrelationIdAccessor());
        FilterRequest request = new()
        {
            Filters = [new FilterCriterion { Field = "Name", Operator = "contains", Value = "a" }],
            Sort = [new SortCriterion { Field = "Name", Direction = "asc" }],
            Page = 1,
            PageSize = 1
        };

        // Act
        Result<PagedResult<SampleDto>> result = await service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.TotalCount, Is.EqualTo(3));
            Assert.That(result.Value.Items, Has.Count.EqualTo(1));
            Assert.That(result.Value.Items[0].Name, Is.EqualTo("Alpha"));
        });
    }

    /// <summary>SearchAsync honors a BuildBaseQuery override that scopes to active rows only.</summary>
    [Test]
    public async Task SearchAsync_RespectsBaseQueryOverride()
    {
        // Arrange
        ActiveOnlySearchService service = new(_context, _mapper, new TestCorrelationIdAccessor());
        FilterRequest request = new() { Page = 1, PageSize = 50 };

        // Act
        Result<PagedResult<SampleDto>> result = await service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.TotalCount, Is.EqualTo(2));
            Assert.That(result.Value.Items, Has.All.Matches<SampleDto>(dto => dto.Name == "Alpha" || dto.Name == "Beta"));
        });
    }

    /// <summary>SearchAsync translates a FilterValidationException into a failure carrying the filter code.</summary>
    [Test]
    public async Task SearchAsync_TranslatesFilterValidationException_ToFailure()
    {
        // Arrange
        SampleSearchService service = new(_context, _mapper, new TestCorrelationIdAccessor());
        FilterRequest request = new() { PageSize = 999 };

        // Act
        Result<PagedResult<SampleDto>> result = await service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("PAGE_SIZE_TOO_LARGE"));
        });
    }
}
