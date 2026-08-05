using System.Reflection;
using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Payments.API.Services;
using Finance.Payments.API.Tests.Builders;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the aging read-path validation, the no-caching rule, and the deliberately EMPTY dependency surface
/// (SDD-PAY-003 §2.3, §2.8, §2.9, §6.5). Every aging code is a 400 validation code — the read surface declares no
/// not-found and no conflict code at all.
/// </summary>
[TestFixture]
[Category("SDD-PAY-003")]
public sealed class AgingValidationTests
{
    private static readonly DateTimeOffset Today = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

    private SqlitePaymentsDbContextScope _scope = null!;
    private AgingServiceTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqlitePaymentsDbContextFactory.Create();
        _harness = AgingServiceTestHarness.Build(_scope.Context);
        _harness.Clock.UtcNow = Today;
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    [Test]
    public async Task Validate_MissingAsOfDate_ReturnsInvalidAgingAsOfDate()
    {
        // Arrange
        AgingReportQueryRequest report = new() { Direction = nameof(InvoiceDirection.AR) };
        CounterpartyBalanceQueryRequest balances = new() { Direction = nameof(InvoiceDirection.AR) };

        // Act
        Result<AgingReportDto> reportResult =
            await _harness.Service.GetAgingAsync(report, CancellationToken.None);
        Result<PagedResult<CounterpartyBalanceDto>> balanceResult =
            await _harness.Service.GetCounterpartyBalancesAsync(balances, DefaultPaging, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(reportResult.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_AGING_AS_OF_DATE));
            Assert.That(balanceResult.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_AGING_AS_OF_DATE));
        });
    }

    [Test]
    public async Task Validate_FutureAsOfDate_ReturnsInvalidAgingAsOfDate()
    {
        // Arrange
        DateTimeOffset tomorrow = Today.AddDays(1);

        // Act
        Result<PagedResult<OpenItemDto>> openItems = await _harness.Service.GetOpenItemsAsync(
            new OpenItemQueryRequest { AsOfDate = tomorrow }, DefaultPaging, CancellationToken.None);
        Result<AgingReportDto> report = await _harness.Service.GetAgingAsync(
            new AgingReportQueryRequest { AsOfDate = tomorrow, Direction = nameof(InvoiceDirection.AR) },
            CancellationToken.None);
        Result<PagedResult<CounterpartyBalanceDto>> balances =
            await _harness.Service.GetCounterpartyBalancesAsync(
                new CounterpartyBalanceQueryRequest
                {
                    AsOfDate = tomorrow,
                    Direction = nameof(InvoiceDirection.AR)
                },
                DefaultPaging,
                CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(openItems.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_AGING_AS_OF_DATE));
            Assert.That(report.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_AGING_AS_OF_DATE));
            Assert.That(balances.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_AGING_AS_OF_DATE));
        });
    }

    [Test]
    public async Task Validate_MissingDirection_ReturnsInvalidAgingDirection()
    {
        // Arrange
        AgingReportQueryRequest report = new() { AsOfDate = Today };

        // Act
        Result<AgingReportDto> result = await _harness.Service.GetAgingAsync(report, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_AGING_DIRECTION));
        });
    }

    [TestCase("ar")]
    [TestCase("BOTH")]
    [TestCase("Receivable")]
    public async Task Validate_UnknownDirectionValue_ReturnsInvalidAgingDirection(string direction)
    {
        // Arrange
        AgingReportQueryRequest report = new() { AsOfDate = Today, Direction = direction };

        // Act
        Result<AgingReportDto> result = await _harness.Service.GetAgingAsync(report, CancellationToken.None);

        // Assert
        Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_AGING_DIRECTION));
    }

    [Test]
    public async Task Validate_EmptyCounterpartyId_ReturnsInvalidCounterpartyId()
    {
        // Arrange
        OpenItemQueryRequest query = new() { AsOfDate = Today, CounterpartyId = Guid.Empty };

        // Act
        Result<PagedResult<OpenItemDto>> result =
            await _harness.Service.GetOpenItemsAsync(query, DefaultPaging, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_COUNTERPARTY_ID));
        });
    }

    [TestCase("bg")]
    [TestCase("BGNN")]
    [TestCase("B1N")]
    [TestCase("")]
    public async Task Validate_MalformedCurrencyCode_ReturnsInvalidAgingCurrency(string currencyCode)
    {
        // Arrange
        OpenItemQueryRequest query = new() { AsOfDate = Today, CurrencyCode = currencyCode };

        // Act
        Result<PagedResult<OpenItemDto>> result =
            await _harness.Service.GetOpenItemsAsync(query, DefaultPaging, CancellationToken.None);

        // Assert
        Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_AGING_CURRENCY));
    }

    [Test]
    public async Task Validate_NonAscendingBuckets_ReturnsInvalidAgingBuckets_BeforeAnyQueryRuns()
    {
        // Arrange
        _scope.Commands.Reset();
        AgingReportQueryRequest report = new()
        {
            AsOfDate = Today,
            Direction = nameof(InvoiceDirection.AR),
            Buckets = [60, 30]
        };

        // Act
        Result<AgingReportDto> result = await _harness.Service.GetAgingAsync(report, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_AGING_BUCKETS));
            Assert.That(_scope.Commands.CommandCount, Is.Zero, "rejected before any query runs");
        });
    }

    [Test]
    public async Task AgingService_DoesNotDependOnCacheService_RecomputesOnEveryCall()
    {
        // Arrange
        InvoiceOpenItem openItem = InvoiceOpenItemBuilder.Create()
            .WithGrossTotal(1000.00m)
            .WithDueDate(Today.AddDays(-10))
            .WithIssueDate(Today.AddDays(-40))
            .Build();
        _scope.Context.InvoiceOpenItems.Add(openItem);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        Result<PagedResult<OpenItemDto>> before = await _harness.Service.GetOpenItemsAsync(
            new OpenItemQueryRequest { AsOfDate = Today }, DefaultPaging, CancellationToken.None);

        // Act
        await _scope.Context.InvoiceOpenItems
            .Where(item => item.InvoiceId == openItem.InvoiceId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.SettledAmount, 600.00m),
                CancellationToken.None);
        Result<PagedResult<OpenItemDto>> after = await _harness.Service.GetOpenItemsAsync(
            new OpenItemQueryRequest { AsOfDate = Today }, DefaultPaging, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(before.Value!.Items.Single().Outstanding, Is.EqualTo(1000.00m));
            Assert.That(
                after.Value!.Items.Single().Outstanding,
                Is.EqualTo(400.00m),
                "every request recomputes from the current projection state");
        });
    }

    [Test]
    public void AgingService_DoesNotDependOnWorkflowAuditOrPublishEndpoint()
    {
        // Arrange
        IReadOnlyList<string> dependencyNames =
        [
            .. typeof(AgingService)
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Single()
                .GetParameters()
                .Select(parameter => parameter.ParameterType.Name)
        ];

        // Act
        IReadOnlyList<string> forbidden =
        [
            .. dependencyNames.Where(name =>
                name.StartsWith("IWorkflowEngine", StringComparison.Ordinal)
                || name.StartsWith("IAuditService", StringComparison.Ordinal)
                || name.StartsWith("IPublishEndpoint", StringComparison.Ordinal)
                || name.StartsWith("ISequenceGenerator", StringComparison.Ordinal)
                || name.StartsWith("ICacheService", StringComparison.Ordinal))
        ];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(forbidden, Is.Empty, "the aging surface changes no state and caches nothing");
            Assert.That(dependencyNames, Does.Contain("PaymentsDbContext"));
            Assert.That(dependencyNames, Does.Contain(nameof(AgingBucketCalculator)));
            Assert.That(dependencyNames, Does.Contain(nameof(SettlementStatusCalculator)));
        });
    }

    /// <summary>The default paging every validation call uses.</summary>
    private static FilterRequest DefaultPaging => new() { Page = 1, PageSize = 50 };
}
