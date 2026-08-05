using Finance.Common.Enums;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Payments.API.Tests.Builders;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the as-of-date semantics and the dual-currency reporting (SDD-PAY-003 §2.2, §2.3, §6.4): the
/// current-state path reading the maintained projection column, the historical path replaying only the surviving
/// allocations of <c>Confirmed</c>/<c>Posted</c> payments, the issue-date bound, and the base-currency counterpart
/// computed at the FROZEN booking rate through <c>ICountryStrategy.ApplyTaxRounding</c>.
/// </summary>
[TestFixture]
[Category("SDD-PAY-003")]
public sealed class AgingAsOfDateTests
{
    private static readonly Guid CustomerA = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Today = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AllocationDay = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);

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
    public async Task AsOfDate_Today_UsesProjectionSettledAmount()
    {
        // Arrange — the projection column deliberately disagrees with the allocation rows, so the path is provable.
        InvoiceOpenItem openItem = await SeedOpenItemAsync(1000.00m, settledAmount: 400.00m);
        await SeedAllocationAsync(openItem.InvoiceId, 250.00m, AllocationDay, PaymentStatus.Posted);

        // Act
        Result<PagedResult<OpenItemDto>> result = await ListAsync(Today);

        // Assert
        OpenItemDto item = result.Value!.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.SettledAmount, Is.EqualTo(400.00m), "the maintained projection column is authority");
            Assert.That(item.Outstanding, Is.EqualTo(600.00m));
        });
    }

    [Test]
    public async Task AsOfDate_Historical_DerivesSettledFromAllocationsUpToDate()
    {
        // Arrange
        InvoiceOpenItem openItem = await SeedOpenItemAsync(1000.00m, settledAmount: 700.00m);
        await SeedAllocationAsync(openItem.InvoiceId, 300.00m, AllocationDay, PaymentStatus.Posted);
        await SeedAllocationAsync(openItem.InvoiceId, 400.00m, Today.AddDays(-1), PaymentStatus.Posted);

        // Act
        Result<PagedResult<OpenItemDto>> beforeSecondAllocation = await ListAsync(AllocationDay);
        Result<PagedResult<OpenItemDto>> afterBoth = await ListAsync(Today.AddDays(-1));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(beforeSecondAllocation.Value!.Items.Single().SettledAmount, Is.EqualTo(300.00m));
            Assert.That(beforeSecondAllocation.Value.Items.Single().Outstanding, Is.EqualTo(700.00m));
            Assert.That(afterBoth.Value!.Items.Single().SettledAmount, Is.EqualTo(700.00m));
            Assert.That(afterBoth.Value.Items.Single().Outstanding, Is.EqualTo(300.00m));
        });
    }

    [Test]
    public async Task AsOfDate_Historical_ExcludesAllocationsOfCancelledAndReversedPayments()
    {
        // Arrange
        InvoiceOpenItem openItem = await SeedOpenItemAsync(1000.00m, settledAmount: 600.00m);
        await SeedAllocationAsync(openItem.InvoiceId, 200.00m, AllocationDay, PaymentStatus.Posted);
        await SeedAllocationAsync(openItem.InvoiceId, 150.00m, AllocationDay, PaymentStatus.Cancelled);
        await SeedAllocationAsync(openItem.InvoiceId, 250.00m, AllocationDay, PaymentStatus.Reversed);

        // Act
        Result<PagedResult<OpenItemDto>> result = await ListAsync(AllocationDay);

        // Assert
        OpenItemDto item = result.Value!.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.SettledAmount, Is.EqualTo(200.00m));
            Assert.That(item.Outstanding, Is.EqualTo(800.00m));
        });
    }

    [Test]
    public async Task AsOfDate_Historical_ExcludesAllocationsOfDraftPayments()
    {
        // Arrange
        InvoiceOpenItem openItem = await SeedOpenItemAsync(1000.00m, settledAmount: 500.00m);
        await SeedAllocationAsync(openItem.InvoiceId, 200.00m, AllocationDay, PaymentStatus.Confirmed);
        await SeedAllocationAsync(openItem.InvoiceId, 300.00m, AllocationDay, PaymentStatus.Draft);

        // Act
        Result<PagedResult<OpenItemDto>> result = await ListAsync(AllocationDay);

        // Assert
        Assert.That(result.Value!.Items.Single().SettledAmount, Is.EqualTo(200.00m));
    }

    [Test]
    public async Task AsOfDate_Now_AllocationDerivedSettled_EqualsProjectionSettledAmount()
    {
        // Arrange — a CONSISTENT sub-ledger built through the real allocate path.
        PaymentAllocationTestHarness allocations = PaymentAllocationTestHarness.Build(_scope.Context);
        Payment payment = PaymentBuilder.Create().WithAmount(1000.00m).WithCounterpartyId(CustomerA).Build();
        _scope.Context.Payments.Add(payment);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(1000.00m);
        Result<AllocatePaymentResultDto> allocated = await allocations.Service.AllocateAsync(
            payment.Id,
            new AllocatePaymentRequest
            {
                Items = [new AllocatePaymentItem { InvoiceId = openItem.InvoiceId, AllocatedAmount = 350.00m }],
                RowVersion = Convert.ToBase64String(payment.RowVersion)
            },
            CancellationToken.None);
        Assert.That(allocated.IsSuccess, Is.True, allocated.ErrorCode);

        // Act
        Result<PagedResult<OpenItemDto>> reported = await ListAsync(Today);
        decimal projectionColumn = await _scope.Context.InvoiceOpenItems
            .AsNoTracking()
            .Where(item => item.InvoiceId == openItem.InvoiceId)
            .Select(item => item.SettledAmount)
            .SingleAsync(CancellationToken.None);
        decimal allocationDerived = await _scope.Context.PaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.InvoiceId == openItem.InvoiceId)
            .Where(allocation => allocation.Payment!.Status == PaymentStatus.Confirmed
                || allocation.Payment!.Status == PaymentStatus.Posted)
            .SumAsync(allocation => allocation.AllocatedAmount, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(projectionColumn, Is.EqualTo(350.00m));
            Assert.That(
                allocationDerived,
                Is.EqualTo(projectionColumn),
                "a mismatch here indicates a projection defect, never something to correct at read time");
            Assert.That(reported.Value!.Items.Single().SettledAmount, Is.EqualTo(projectionColumn));
        });
    }

    [Test]
    public async Task AsOfDate_ExcludesItemsIssuedAfterAsOfDate_FromCountsAndTotals()
    {
        // Arrange
        await SeedOpenItemAsync(1000.00m, issueDate: Today.AddDays(-30), documentNumber: "SINV-EARLY");
        await SeedOpenItemAsync(500.00m, issueDate: Today.AddDays(-2), documentNumber: "SINV-LATE");

        // Act
        Result<PagedResult<OpenItemDto>> result = await ListAsync(Today.AddDays(-10));
        Result<AgingReportDto> report = await _harness.Service.GetAgingAsync(
            new AgingReportQueryRequest
            {
                AsOfDate = Today.AddDays(-10),
                Direction = nameof(InvoiceDirection.AR)
            },
            CancellationToken.None);

        // Assert
        Assert.That(report.IsSuccess, Is.True, report.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Items.Select(item => item.DocumentNumber), Is.EqualTo(new[] { "SINV-EARLY" }));
            Assert.That(report.Value!.OpenItemCount, Is.EqualTo(1));
            Assert.That(report.Value.GrandTotalBaseOutstanding, Is.EqualTo(1000.00m));
            Assert.That(report.Value.Rows.Single().TotalOutstanding, Is.EqualTo(1000.00m));
        });
    }

    [Test]
    public async Task DualCurrency_ReportsTransactionalAndBaseOutstanding()
    {
        // Arrange
        await SeedOpenItemAsync(1000.00m, currencyCode: "EUR", bookingRate: 1.955830m);

        // Act
        Result<PagedResult<OpenItemDto>> result = await ListAsync(Today);

        // Assert
        OpenItemDto item = result.Value!.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.CurrencyCode, Is.EqualTo("EUR"));
            Assert.That(item.Outstanding, Is.EqualTo(1000.00m));
            Assert.That(item.BaseCurrencyCode, Is.EqualTo(FakePaymentCountryStrategy.BaseCurrency));
            Assert.That(item.BaseOutstanding, Is.EqualTo(1955.83m));
        });
    }

    [Test]
    public async Task DualCurrency_BaseOutstanding_RoundsThroughCountryStrategyApplyTaxRounding()
    {
        // Arrange
        await SeedOpenItemAsync(333.33m, currencyCode: "EUR", bookingRate: 1.955830m);
        int roundingCallsBefore = _harness.Country.ApplyTaxRoundingCallCount;

        // Act
        Result<PagedResult<OpenItemDto>> result = await ListAsync(Today);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Items.Single().BaseOutstanding, Is.EqualTo(651.94m));
            Assert.That(
                _harness.Country.ApplyTaxRoundingCallCount,
                Is.GreaterThan(roundingCallsBefore),
                "the country strategy owns monetary rounding; the read path must not inline a mode");
        });
    }

    [Test]
    public async Task DualCurrency_BaseCurrencyItem_BaseOutstandingEqualsOutstanding_RateIsOne()
    {
        // Arrange
        await SeedOpenItemAsync(777.77m);

        // Act
        Result<PagedResult<OpenItemDto>> result = await ListAsync(Today);

        // Assert
        OpenItemDto item = result.Value!.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.CurrencyCode, Is.EqualTo(item.BaseCurrencyCode));
            Assert.That(item.BaseOutstanding, Is.EqualTo(item.Outstanding));
            Assert.That(item.BaseOutstanding, Is.EqualTo(777.77m));
        });
    }

    [Test]
    public async Task DualCurrency_MixedCurrencyCounterparty_ProducesOneRowPerCurrency_NoCrossCurrencyTotal()
    {
        // Arrange
        await SeedOpenItemAsync(100.00m, documentNumber: "SINV-BGN");
        await SeedOpenItemAsync(
            200.00m, currencyCode: "EUR", bookingRate: 1.955830m, documentNumber: "SINV-EUR");

        // Act
        Result<AgingReportDto> report = await _harness.Service.GetAgingAsync(
            new AgingReportQueryRequest { AsOfDate = Today, Direction = nameof(InvoiceDirection.AR) },
            CancellationToken.None);

        // Assert
        Assert.That(report.IsSuccess, Is.True, report.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(report.Value!.Rows, Has.Count.EqualTo(2));
            Assert.That(
                report.Value.Rows.Select(row => row.CurrencyCode),
                Is.EquivalentTo(new[] { "BGN", "EUR" }));
            Assert.That(
                report.Value.Rows.Single(row => row.CurrencyCode == "EUR").TotalOutstanding,
                Is.EqualTo(200.00m),
                "a transactional total is never summed across currencies");
            Assert.That(report.Value.GrandTotalBaseOutstanding, Is.EqualTo(100.00m + 391.17m));
        });
    }

    [Test]
    public async Task DualCurrency_UsesFrozenBookingExchangeRate_NoRateLookup()
    {
        // Arrange
        await SeedOpenItemAsync(500.00m, currencyCode: "EUR", bookingRate: 1.500000m);

        // Act
        Result<PagedResult<OpenItemDto>> result = await ListAsync(Today);

        // Assert
        Assert.That(
            result.Value!.Items.Single().BaseOutstanding,
            Is.EqualTo(750.00m),
            "the FROZEN booking rate is used verbatim — the read path never looks up a current rate");
    }

    /// <summary>Persists an open-item projection row for the fixture counterparty.</summary>
    /// <param name="grossTotal">The invoice gross total.</param>
    /// <param name="settledAmount">The locally-owned settled amount.</param>
    /// <param name="currencyCode">The transactional currency.</param>
    /// <param name="bookingRate">The frozen booking rate.</param>
    /// <param name="issueDate">The invoice issue date.</param>
    /// <param name="documentNumber">The invoice document number.</param>
    /// <returns>The persisted open item.</returns>
    private async Task<InvoiceOpenItem> SeedOpenItemAsync(
        decimal grossTotal,
        decimal settledAmount = 0m,
        string currencyCode = FakePaymentCountryStrategy.BaseCurrency,
        decimal bookingRate = 1.000000m,
        DateTimeOffset? issueDate = null,
        string documentNumber = "SINV-2026-000001")
    {
        InvoiceOpenItem openItem = InvoiceOpenItemBuilder.Create()
            .WithCounterpartyId(CustomerA)
            .WithGrossTotal(grossTotal)
            .WithSettledAmount(settledAmount)
            .WithCurrencyCode(currencyCode)
            .WithBookingExchangeRate(bookingRate)
            .WithIssueDate(issueDate ?? Today.AddDays(-60))
            .WithDueDate(Today.AddDays(-30))
            .WithDocumentNumber(documentNumber)
            .Build();

        _scope.Context.InvoiceOpenItems.Add(openItem);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        return openItem;
    }

    /// <summary>Persists a payment in the supplied state plus one allocation row against the invoice.</summary>
    /// <param name="invoiceId">The invoice the allocation matches.</param>
    /// <param name="amount">The allocated amount.</param>
    /// <param name="allocatedAt">The moment the match was recorded.</param>
    /// <param name="status">The owning payment's lifecycle state.</param>
    /// <returns>A task completing when the rows are persisted.</returns>
    private async Task SeedAllocationAsync(
        Guid invoiceId,
        decimal amount,
        DateTimeOffset allocatedAt,
        PaymentStatus status)
    {
        Payment payment = PaymentBuilder.Create()
            .WithStatus(status)
            .WithDocumentNumber(null)
            .WithAmount(1000.00m)
            .WithCounterpartyId(CustomerA)
            .WithAllocatedAmount(amount)
            .Build();

        payment.Allocations.Add(new PaymentAllocation
        {
            PaymentId = payment.Id,
            InvoiceId = invoiceId,
            AllocatedAmount = amount,
            BaseAllocatedAmount = amount,
            AllocatedAt = allocatedAt,
            AllocatedBy = StubCurrentUserAccessor.TestUserId,
            CorrelationId = StubCorrelationIdAccessor.DefaultCorrelationId
        });

        _scope.Context.Payments.Add(payment);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Reads the open-item page as of the supplied date.</summary>
    /// <param name="asOfDate">The as-of date.</param>
    /// <returns>The open-item page result.</returns>
    private async Task<Result<PagedResult<OpenItemDto>>> ListAsync(DateTimeOffset asOfDate)
    {
        Result<PagedResult<OpenItemDto>> result = await _harness.Service.GetOpenItemsAsync(
            new OpenItemQueryRequest { AsOfDate = asOfDate },
            new FilterRequest { Page = 1, PageSize = 50 },
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        return result;
    }
}
