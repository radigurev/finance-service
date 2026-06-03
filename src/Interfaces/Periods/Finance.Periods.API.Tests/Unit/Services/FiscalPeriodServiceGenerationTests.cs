using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Periods.API.Tests.Builders;
using Finance.Periods.API.Tests.Fixtures;
using Finance.Periods.DBModel.Models;
using Finance.ServiceModel.Periods;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Finance.Periods.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the generation, single-create, date-lookup, get, and list surface of
/// <see cref="Finance.Periods.API.Services.FiscalPeriodService"/> (SDD-FIN-004 §6.3). Runs fully offline
/// against a SQLite in-memory <see cref="Finance.Periods.DBModel.PeriodsDbContext"/> with the real
/// calendar-month fiscal calendar and the recording reference cache.
/// </summary>
[TestFixture]
[Category("SDD-FIN-004")]
public sealed class FiscalPeriodServiceGenerationTests
{
    private SqlitePeriodsDbContextScope _scope = null!;
    private FiscalPeriodServiceTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqlitePeriodsDbContextFactory.Create();
        _harness = FiscalPeriodServiceTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    /// <summary>Generation creates 12 open, contiguous, non-overlapping calendar months (§2.2, §6.3).</summary>
    [Test]
    public async Task Generate_TwelveCalendarMonths_AllOpen_ContiguousAndNonOverlapping()
    {
        // Arrange
        GeneratePeriodsRequest request = new() { FiscalYear = 2026 };

        // Act
        Result<IReadOnlyList<FiscalPeriodDto>> result =
            await _harness.Service.GenerateAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        IReadOnlyList<FiscalPeriodDto> periods = [.. result.Value!.OrderBy(period => period.PeriodNumber)];
        Assert.Multiple(() =>
        {
            Assert.That(periods, Has.Count.EqualTo(12));
            Assert.That(periods, Has.All.Property(nameof(FiscalPeriodDto.Status)).EqualTo(FiscalPeriodStatus.Open));
            Assert.That(periods[0].PeriodNumber, Is.EqualTo(1));
            Assert.That(periods[11].PeriodNumber, Is.EqualTo(12));
        });

        for (int i = 1; i < periods.Count; i++)
        {
            Assert.That(
                periods[i].StartDate.UtcTicks - periods[i - 1].EndDate.UtcTicks,
                Is.EqualTo(1),
                "Each period must start one tick after the prior period ends (contiguous, non-overlapping).");
        }
    }

    /// <summary>Generating a year that already has periods returns DUPLICATE_PERIOD and persists nothing new (§2.2, §6.3).</summary>
    [Test]
    public async Task Generate_YearWithExistingPeriods_ReturnsDuplicatePeriod_CreatesNothing()
    {
        // Arrange — seed a single period for the target year.
        FiscalPeriod existing = FiscalPeriodBuilder.Create().WithFiscalYear(2026).WithPeriodNumber(1).Build();
        _scope.Context.FiscalPeriods.Add(existing);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        _scope.Context.ChangeTracker.Clear();

        // Act
        Result<IReadOnlyList<FiscalPeriodDto>> result =
            await _harness.Service.GenerateAsync(new GeneratePeriodsRequest { FiscalYear = 2026 }, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PeriodErrorCodes.DUPLICATE_PERIOD));
            Assert.That(_scope.Context.FiscalPeriods.Count(period => period.FiscalYear == 2026), Is.EqualTo(1));
        });
    }

    /// <summary>Creating a period whose range overlaps an existing one returns OVERLAPPING_PERIOD (§2.3, §6.3).</summary>
    [Test]
    public async Task Create_OverlappingRange_ReturnsOverlappingPeriod()
    {
        // Arrange — seed January, then attempt a period overlapping its second half.
        FiscalPeriod january = FiscalPeriodBuilder.Create().WithFiscalYear(2026).WithPeriodNumber(1)
            .WithDates(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero))
            .Build();
        _scope.Context.FiscalPeriods.Add(january);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        _scope.Context.ChangeTracker.Clear();

        CreatePeriodRequest request = new()
        {
            FiscalYear = 2026,
            PeriodNumber = 2,
            StartDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero)
        };

        // Act
        Result<FiscalPeriodDto> result = await _harness.Service.CreateAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PeriodErrorCodes.OVERLAPPING_PERIOD));
        });
    }

    /// <summary>Creating a period that duplicates (FiscalYear, PeriodNumber) returns DUPLICATE_PERIOD (§2.3, §6.3).</summary>
    [Test]
    public async Task Create_DuplicateYearAndNumber_ReturnsDuplicatePeriod()
    {
        // Arrange
        FiscalPeriod january = FiscalPeriodBuilder.Create().WithFiscalYear(2026).WithPeriodNumber(1)
            .WithDates(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero))
            .Build();
        _scope.Context.FiscalPeriods.Add(january);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        _scope.Context.ChangeTracker.Clear();

        CreatePeriodRequest request = new()
        {
            FiscalYear = 2026,
            PeriodNumber = 1,
            StartDate = new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2027, 6, 30, 23, 59, 59, TimeSpan.Zero)
        };

        // Act
        Result<FiscalPeriodDto> result = await _harness.Service.CreateAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PeriodErrorCodes.DUPLICATE_PERIOD));
        });
    }

    /// <summary>By-date returns the containing period with its status (§2.6, §6.3).</summary>
    [Test]
    public async Task ByDate_ReturnsContainingPeriod_WithStatus()
    {
        // Arrange
        await GenerateYearAsync(2026);
        DateTimeOffset midMarch = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

        // Act
        Result<FiscalPeriodDto> result = await _harness.Service.GetByDateAsync(midMarch, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.PeriodNumber, Is.EqualTo(3));
            Assert.That(result.Value.Status, Is.EqualTo(FiscalPeriodStatus.Open));
        });
    }

    /// <summary>By-date for a date no period covers returns NO_PERIOD_FOR_DATE (§2.6, §2.14, §6.3).</summary>
    [Test]
    public async Task ByDate_NoCoveringPeriod_ReturnsNoPeriodForDate()
    {
        // Arrange — generate 2026 only, then look up a 2027 date.
        await GenerateYearAsync(2026);
        DateTimeOffset uncovered = new(2027, 5, 1, 0, 0, 0, TimeSpan.Zero);

        // Act
        Result<FiscalPeriodDto> result = await _harness.Service.GetByDateAsync(uncovered, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PeriodErrorCodes.NO_PERIOD_FOR_DATE));
        });
    }

    /// <summary>By-date is inclusive of both the start and the end instant of a period (§2.6, §6.3).</summary>
    [Test]
    public async Task ByDate_BoundaryDate_IsInclusiveOfStartAndEnd()
    {
        // Arrange
        await GenerateYearAsync(2026);
        DateTimeOffset firstInstant = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset lastInstant = new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero)
            .AddTicks(TimeSpan.TicksPerSecond - 1);

        // Act
        Result<FiscalPeriodDto> atStart = await _harness.Service.GetByDateAsync(firstInstant, CancellationToken.None);
        Result<FiscalPeriodDto> atEnd = await _harness.Service.GetByDateAsync(lastInstant, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(atStart.IsSuccess, Is.True, atStart.ErrorCode);
            Assert.That(atStart.Value!.PeriodNumber, Is.EqualTo(1));
            Assert.That(atEnd.IsSuccess, Is.True, atEnd.ErrorCode);
            Assert.That(atEnd.Value!.PeriodNumber, Is.EqualTo(1));
        });
    }

    /// <summary>Get for a missing period id returns PERIOD_NOT_FOUND (§2.11, §6.3).</summary>
    [Test]
    public async Task Get_ReturnsNotFound_WhenPeriodDoesNotExist()
    {
        // Arrange & Act
        Result<FiscalPeriodDto> result = await _harness.Service.GetAsync(999, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PeriodErrorCodes.PERIOD_NOT_FOUND));
        });
    }

    /// <summary>List defaults to FiscalYear descending then PeriodNumber ascending (§2.11, §6.3).</summary>
    [Test]
    public async Task Search_ReturnsPagedResultOrderedByFiscalYearDescThenPeriodNumberAsc()
    {
        // Arrange — two periods in 2025, two in 2026, inserted out of order.
        await SeedAsync(2025, 2);
        await SeedAsync(2026, 1);
        await SeedAsync(2025, 1);
        await SeedAsync(2026, 2);
        FilterRequest request = new() { Page = 1, PageSize = 50 };

        // Act
        Result<PagedResult<FiscalPeriodDto>> result =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        IReadOnlyList<FiscalPeriodDto> items = result.Value!.Items;
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(4));
            Assert.That(items[0].FiscalYear, Is.EqualTo(2026));
            Assert.That(items[0].PeriodNumber, Is.EqualTo(1));
            Assert.That(items[1].FiscalYear, Is.EqualTo(2026));
            Assert.That(items[1].PeriodNumber, Is.EqualTo(2));
            Assert.That(items[2].FiscalYear, Is.EqualTo(2025));
            Assert.That(items[2].PeriodNumber, Is.EqualTo(1));
            Assert.That(items[3].FiscalYear, Is.EqualTo(2025));
            Assert.That(items[3].PeriodNumber, Is.EqualTo(2));
        });
    }

    /// <summary>List rejects a page size above the 200 cap with PAGE_SIZE_TOO_LARGE (§2.11, §6.3).</summary>
    [Test]
    public async Task Search_CapsPageSizeAt200()
    {
        // Arrange
        FilterRequest request = new() { Page = 1, PageSize = 201 };

        // Act
        Result<PagedResult<FiscalPeriodDto>> result =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(FilterErrorCodes.PAGE_SIZE_TOO_LARGE));
        });
    }

    /// <summary>The filtered list reads live from the DB and is never served from the reference cache (§2.8, §2.11, §6.3).</summary>
    [Test]
    public async Task Search_DoesNotCacheFilteredList()
    {
        // Arrange
        await SeedAsync(2026, 1);
        FilterRequest request = new() { Page = 1, PageSize = 50 };
        Result<PagedResult<FiscalPeriodDto>> first =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Act — add a second period, then search again; a cached list would not see it.
        await SeedAsync(2026, 2);
        Result<PagedResult<FiscalPeriodDto>> second =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(first.Value!.TotalCount, Is.EqualTo(1));
            Assert.That(second.Value!.TotalCount, Is.EqualTo(2));
            Assert.That(_harness.Cache.RemovedKeys, Is.Empty);
        });
    }

    // ---- Helpers ----

    private async Task GenerateYearAsync(int fiscalYear)
    {
        Result<IReadOnlyList<FiscalPeriodDto>> generated =
            await _harness.Service.GenerateAsync(new GeneratePeriodsRequest { FiscalYear = fiscalYear }, CancellationToken.None);
        Assert.That(generated.IsSuccess, Is.True, generated.ErrorCode);
        _scope.Context.ChangeTracker.Clear();
    }

    private async Task SeedAsync(int fiscalYear, int periodNumber)
    {
        DateTimeOffset start = new(fiscalYear, periodNumber, 1, 0, 0, 0, TimeSpan.Zero);
        int daysInMonth = DateTime.DaysInMonth(fiscalYear, periodNumber);
        DateTimeOffset end = new DateTimeOffset(fiscalYear, periodNumber, daysInMonth, 23, 59, 59, TimeSpan.Zero)
            .AddTicks(TimeSpan.TicksPerSecond - 1);

        FiscalPeriod period = FiscalPeriodBuilder.Create()
            .WithFiscalYear(fiscalYear)
            .WithPeriodNumber(periodNumber)
            .WithName($"Period {periodNumber} {fiscalYear}")
            .WithDates(start, end)
            .Build();
        _scope.Context.FiscalPeriods.Add(period);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        _scope.Context.ChangeTracker.Clear();
    }
}
