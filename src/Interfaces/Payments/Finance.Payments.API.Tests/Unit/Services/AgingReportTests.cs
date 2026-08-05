using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Payments.API.Tests.Builders;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the bucketed aging report and the per-counterparty balances (SDD-PAY-003 §2.6, §2.7, §6.3): the
/// bucket totals reconciling to the row total, the (counterparty, currency) grouping key, direction separation, the
/// omitted zero-outstanding counterparty, the echoed boundaries and labels, the deterministic ordering, the SINGLE
/// grouped round trip, the empty window, and the shared aggregation path both surfaces read.
/// </summary>
[TestFixture]
[Category("SDD-PAY-003")]
public sealed class AgingReportTests
{
    private static readonly Guid CustomerA = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CustomerB = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SupplierC = new("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset AsOf = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

    private SqlitePaymentsDbContextScope _scope = null!;
    private AgingServiceTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqlitePaymentsDbContextFactory.Create();
        _harness = AgingServiceTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    [Test]
    public async Task Aging_SumOfBucketOutstanding_EqualsRowTotalOutstanding()
    {
        // Arrange
        await SeedAsync(CustomerA, 1000.00m, AsOf.AddDays(10));
        await SeedAsync(CustomerA, 500.00m, AsOf.AddDays(-5), "SINV-2");
        await SeedAsync(CustomerA, 250.00m, AsOf.AddDays(-45), "SINV-3");
        await SeedAsync(CustomerA, 125.00m, AsOf.AddDays(-200), "SINV-4");

        // Act
        Result<AgingReportDto> result = await AgingAsync();

        // Assert
        AgingRowDto row = result.Value!.Rows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Buckets.Sum(bucket => bucket.Outstanding), Is.EqualTo(row.TotalOutstanding));
            Assert.That(row.Buckets.Sum(bucket => bucket.BaseOutstanding), Is.EqualTo(row.TotalBaseOutstanding));
            Assert.That(row.TotalOutstanding, Is.EqualTo(1875.00m));
            Assert.That(row.OpenItemCount, Is.EqualTo(4));
            Assert.That(row.Buckets.Sum(bucket => bucket.ItemCount), Is.EqualTo(4));
        });
    }

    [Test]
    public async Task Aging_ZeroOutstandingCounterparty_IsOmitted()
    {
        // Arrange
        await SeedAsync(CustomerA, 500.00m, AsOf.AddDays(-1));
        await SeedAsync(CustomerB, 500.00m, AsOf.AddDays(-1), "SINV-B", settledAmount: 500.00m);

        // Act
        Result<AgingReportDto> result = await AgingAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Rows, Has.Count.EqualTo(1));
            Assert.That(result.Value.Rows.Single().CounterpartyId, Is.EqualTo(CustomerA));
        });
    }

    [Test]
    public async Task Aging_GroupsByCounterpartyAndCurrency_OneRowPerPair()
    {
        // Arrange
        await SeedAsync(CustomerA, 100.00m, AsOf.AddDays(-1), "SINV-A-BGN");
        await SeedAsync(CustomerA, 200.00m, AsOf.AddDays(-1), "SINV-A-EUR", currencyCode: "EUR", bookingRate: 1.955830m);
        await SeedAsync(CustomerB, 300.00m, AsOf.AddDays(-1), "SINV-B-BGN");

        // Act
        Result<AgingReportDto> result = await AgingAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Rows, Has.Count.EqualTo(3));
            Assert.That(
                result.Value.Rows.Count(row => row.CounterpartyId == CustomerA),
                Is.EqualTo(2),
                "a multi-currency counterparty produces one row per currency");
            Assert.That(
                result.Value.Rows.Select(row => row.CurrencyCode).Distinct(),
                Is.EquivalentTo(new[] { "BGN", "EUR" }));
        });
    }

    [Test]
    public async Task Aging_ArAndApSeparated_ByDirection()
    {
        // Arrange
        await SeedAsync(CustomerA, 400.00m, AsOf.AddDays(-1), "SINV-AR");
        await SeedAsync(
            SupplierC, 700.00m, AsOf.AddDays(-1), "PINV-AP", documentType: InvoiceDocumentType.PurchaseInvoice);

        // Act
        Result<AgingReportDto> receivables = await AgingAsync(nameof(InvoiceDirection.AR));
        Result<AgingReportDto> payables = await AgingAsync(nameof(InvoiceDirection.AP));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(receivables.Value!.Rows.Single().CounterpartyId, Is.EqualTo(CustomerA));
            Assert.That(receivables.Value.Direction, Is.EqualTo(nameof(InvoiceDirection.AR)));
            Assert.That(payables.Value!.Rows.Single().CounterpartyId, Is.EqualTo(SupplierC));
            Assert.That(payables.Value.GrandTotalBaseOutstanding, Is.EqualTo(700.00m));
        });
    }

    [Test]
    public async Task Aging_ConfirmedCreditNote_IsNotAged()
    {
        // Arrange
        await SeedAsync(
            CustomerA, 200.00m, AsOf.AddDays(-40), "CN-2026-000001",
            documentType: InvoiceDocumentType.CreditNote);

        // Act
        Result<AgingReportDto> receivables = await AgingAsync(nameof(InvoiceDirection.AR));
        Result<AgingReportDto> payables = await AgingAsync(nameof(InvoiceDirection.AP));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(receivables.Value!.Rows, Is.Empty);
            Assert.That(payables.Value!.Rows, Is.Empty, "never a phantom AP payable ageing 1-30 → 90+ forever");
            Assert.That(payables.Value.GrandTotalBaseOutstanding, Is.EqualTo(0.00m));
            Assert.That(payables.Value.OpenItemCount, Is.Zero);
        });
    }

    [Test]
    public async Task Aging_EchoesEffectiveBucketBoundariesAndLabels()
    {
        // Arrange
        await SeedAsync(CustomerA, 100.00m, AsOf.AddDays(-1));

        // Act
        Result<AgingReportDto> defaults = await AgingAsync();
        Result<AgingReportDto> custom = await _harness.Service.GetAgingAsync(
            new AgingReportQueryRequest
            {
                AsOfDate = AsOf,
                Direction = nameof(InvoiceDirection.AR),
                Buckets = [15, 30, 60]
            },
            CancellationToken.None);

        // Assert
        Assert.That(custom.IsSuccess, Is.True, custom.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(defaults.Value!.BucketDayBoundaries, Is.EqualTo(new[] { 30, 60, 90 }));
            Assert.That(
                defaults.Value.BucketLabels,
                Is.EqualTo(new[] { "Current", "1-30", "31-60", "61-90", "90+" }));
            Assert.That(custom.Value!.BucketDayBoundaries, Is.EqualTo(new[] { 15, 30, 60 }));
            Assert.That(
                custom.Value.BucketLabels,
                Is.EqualTo(new[] { "Current", "1-15", "16-30", "31-60", "60+" }));
            Assert.That(
                defaults.Value.BaseCurrencyCode,
                Is.EqualTo(FakePaymentCountryStrategy.BaseCurrency));
            Assert.That(defaults.Value.Totals.Select(total => total.Label), Is.EqualTo(defaults.Value.BucketLabels));
        });
    }

    [Test]
    public async Task Aging_RowsOrderedByBaseOutstandingDescThenGroupingKey_Deterministic()
    {
        // Arrange
        await SeedAsync(CustomerB, 900.00m, AsOf.AddDays(-1), "SINV-B");
        await SeedAsync(CustomerA, 100.00m, AsOf.AddDays(-1), "SINV-A-BGN");
        await SeedAsync(CustomerA, 100.00m, AsOf.AddDays(-1), "SINV-A-EUR", currencyCode: "EUR", bookingRate: 1.000000m);

        // Act
        Result<AgingReportDto> first = await AgingAsync();
        Result<AgingReportDto> second = await AgingAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(first.Value!.Rows[0].CounterpartyId, Is.EqualTo(CustomerB));
            Assert.That(
                first.Value.Rows.Select(row => row.TotalBaseOutstanding),
                Is.Ordered.Descending);
            Assert.That(first.Value.Rows[1].CurrencyCode, Is.EqualTo("BGN"));
            Assert.That(first.Value.Rows[2].CurrencyCode, Is.EqualTo("EUR"));
            Assert.That(
                second.Value!.Rows.Select(row => (row.CounterpartyId, row.CurrencyCode)),
                Is.EqualTo(first.Value.Rows.Select(row => (row.CounterpartyId, row.CurrencyCode))));
        });
    }

    [Test]
    public async Task Aging_ComputesInASingleGroupedQuery_NoPerCounterpartyRoundTrip()
    {
        // Arrange
        await SeedAsync(CustomerA, 100.00m, AsOf.AddDays(-1), "SINV-1");
        _scope.Commands.Reset();
        await AgingAsync();
        int commandsForOneCounterparty = _scope.Commands.CommandCount;
        for (int index = 2; index <= 6; index++)
        {
            await SeedAsync(Guid.NewGuid(), 100.00m * index, AsOf.AddDays(-index), $"SINV-{index}");
        }

        // Act
        _scope.Commands.Reset();
        Result<AgingReportDto> result = await AgingAsync();
        int commandsForSixCounterparties = _scope.Commands.CommandCount;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Rows, Has.Count.EqualTo(6));
            Assert.That(commandsForOneCounterparty, Is.EqualTo(1), "one grouped round trip");
            Assert.That(
                commandsForSixCounterparties,
                Is.EqualTo(commandsForOneCounterparty),
                "the round-trip count must not scale with the grouping key");
        });
    }

    [Test]
    public async Task Aging_EmptyWindow_ReturnsEmptyRowsAndZeroTotals_NotFound()
    {
        // Arrange
        await SeedAsync(CustomerA, 100.00m, AsOf.AddDays(-1));

        // Act
        Result<AgingReportDto> result = await _harness.Service.GetAgingAsync(
            new AgingReportQueryRequest
            {
                AsOfDate = AsOf,
                Direction = nameof(InvoiceDirection.AR),
                CounterpartyId = CustomerB
            },
            CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Rows, Is.Empty);
            Assert.That(result.Value.GrandTotalBaseOutstanding, Is.EqualTo(0.00m));
            Assert.That(result.Value.OpenItemCount, Is.Zero);
            Assert.That(result.Value.Totals, Has.Count.EqualTo(5), "the bucket scaffold is still well-formed");
            Assert.That(result.Value.Totals, Has.All.Property(nameof(AgingBucketTotalDto.ItemCount)).Zero);
        });
    }

    [Test]
    public async Task CounterpartyBalances_OverdueOutstanding_EqualsTotalMinusCurrentBucket()
    {
        // Arrange
        await SeedAsync(CustomerA, 1000.00m, AsOf.AddDays(10), "SINV-CURRENT");
        await SeedAsync(CustomerA, 400.00m, AsOf.AddDays(-20), "SINV-OVERDUE-1");
        await SeedAsync(CustomerA, 600.00m, AsOf.AddDays(-95), "SINV-OVERDUE-2");

        // Act
        Result<AgingReportDto> aging = await AgingAsync();
        Result<PagedResult<CounterpartyBalanceDto>> balances = await BalancesAsync();

        // Assert
        AgingRowDto agingRow = aging.Value!.Rows.Single();
        CounterpartyBalanceDto balance = balances.Value!.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(balance.Outstanding, Is.EqualTo(2000.00m));
            Assert.That(balance.OverdueOutstanding, Is.EqualTo(1000.00m));
            Assert.That(
                balance.OverdueOutstanding,
                Is.EqualTo(agingRow.TotalOutstanding - agingRow.Buckets[0].Outstanding));
            Assert.That(balance.Direction, Is.EqualTo(nameof(InvoiceDirection.AR)));
        });
    }

    [Test]
    public async Task CounterpartyBalances_OldestDueDate_IsEarliestInScopeDueDate()
    {
        // Arrange
        DateTimeOffset oldest = AsOf.AddDays(-95);
        await SeedAsync(CustomerA, 100.00m, AsOf.AddDays(-1), "SINV-1");
        await SeedAsync(CustomerA, 100.00m, oldest, "SINV-2");
        await SeedAsync(CustomerA, 100.00m, AsOf.AddDays(-30), "SINV-3");

        // Act
        Result<PagedResult<CounterpartyBalanceDto>> result = await BalancesAsync();

        // Assert
        Assert.That(result.Value!.Items.Single().OldestDueDate, Is.EqualTo(oldest));
    }

    [Test]
    public async Task CounterpartyBalances_ZeroOutstandingCounterparty_OmittedFromTotalCount()
    {
        // Arrange
        await SeedAsync(CustomerA, 100.00m, AsOf.AddDays(-1), "SINV-A");
        await SeedAsync(CustomerB, 100.00m, AsOf.AddDays(-1), "SINV-B", settledAmount: 100.00m);

        // Act
        Result<PagedResult<CounterpartyBalanceDto>> result = await BalancesAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Items, Has.Count.EqualTo(1));
            Assert.That(result.Value.TotalCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CounterpartyBalances_TotalOutstanding_MatchesAgingForSamePair()
    {
        // Arrange
        await SeedAsync(CustomerA, 333.33m, AsOf.AddDays(-1), "SINV-1");
        await SeedAsync(CustomerA, 666.67m, AsOf.AddDays(-70), "SINV-2");

        // Act
        Result<AgingReportDto> aging = await AgingAsync();
        Result<PagedResult<CounterpartyBalanceDto>> balances = await BalancesAsync();

        // Assert
        AgingRowDto agingRow = aging.Value!.Rows.Single();
        CounterpartyBalanceDto balance = balances.Value!.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(balance.Outstanding, Is.EqualTo(agingRow.TotalOutstanding));
            Assert.That(balance.BaseOutstanding, Is.EqualTo(agingRow.TotalBaseOutstanding));
            Assert.That(balance.OpenItemCount, Is.EqualTo(agingRow.OpenItemCount));
        });
    }

    [Test]
    public async Task CounterpartyBalances_UnknownCounterparty_ReturnsEmpty_NotFound()
    {
        // Arrange
        await SeedAsync(CustomerA, 100.00m, AsOf.AddDays(-1));

        // Act
        Result<PagedResult<CounterpartyBalanceDto>> result = await _harness.Service.GetCounterpartyBalancesAsync(
            new CounterpartyBalanceQueryRequest { AsOfDate = AsOf, Direction = nameof(InvoiceDirection.AP) },
            new FilterRequest { Page = 1, PageSize = 50 },
            CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Items, Is.Empty);
            Assert.That(result.Value.TotalCount, Is.Zero);
        });
    }

    [Test]
    public async Task CounterpartyBalances_RespectsPageSizeCap_200()
    {
        // Arrange
        await SeedAsync(CustomerA, 100.00m, AsOf.AddDays(-1));

        // Act
        Result<PagedResult<CounterpartyBalanceDto>> result = await _harness.Service.GetCounterpartyBalancesAsync(
            new CounterpartyBalanceQueryRequest { AsOfDate = AsOf, Direction = nameof(InvoiceDirection.AR) },
            new FilterRequest { Page = 1, PageSize = 201 },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(FilterErrorCodes.PAGE_SIZE_TOO_LARGE));
        });
    }

    /// <summary>Persists an open-item projection row for the supplied counterparty.</summary>
    /// <param name="counterpartyId">The counterparty reference.</param>
    /// <param name="grossTotal">The invoice gross total.</param>
    /// <param name="dueDate">The invoice due date.</param>
    /// <param name="documentNumber">The invoice document number.</param>
    /// <param name="settledAmount">The locally-owned settled amount.</param>
    /// <param name="currencyCode">The transactional currency.</param>
    /// <param name="bookingRate">The frozen booking rate.</param>
    /// <param name="documentType">The invoice document type.</param>
    /// <returns>A task completing when the row is persisted.</returns>
    private async Task SeedAsync(
        Guid counterpartyId,
        decimal grossTotal,
        DateTimeOffset dueDate,
        string documentNumber = "SINV-2026-000001",
        decimal settledAmount = 0m,
        string currencyCode = FakePaymentCountryStrategy.BaseCurrency,
        decimal bookingRate = 1.000000m,
        InvoiceDocumentType documentType = InvoiceDocumentType.SaleInvoice)
    {
        InvoiceOpenItem openItem = InvoiceOpenItemBuilder.Create()
            .WithCounterpartyId(counterpartyId)
            .WithGrossTotal(grossTotal)
            .WithSettledAmount(settledAmount)
            .WithDueDate(dueDate)
            .WithIssueDate(AsOf.AddDays(-100))
            .WithDocumentNumber(documentNumber)
            .WithCurrencyCode(currencyCode)
            .WithBookingExchangeRate(bookingRate)
            .WithDocumentType(documentType)
            .Build();

        _scope.Context.InvoiceOpenItems.Add(openItem);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Reads the aging report for the supplied direction as of the fixture date.</summary>
    /// <param name="direction">The reported direction.</param>
    /// <returns>The aging report result.</returns>
    private async Task<Result<AgingReportDto>> AgingAsync(string direction = "AR")
    {
        Result<AgingReportDto> result = await _harness.Service.GetAgingAsync(
            new AgingReportQueryRequest { AsOfDate = AsOf, Direction = direction },
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        return result;
    }

    /// <summary>Reads the counterparty balances for the supplied direction as of the fixture date.</summary>
    /// <param name="direction">The reported direction.</param>
    /// <returns>The counterparty balance page.</returns>
    private async Task<Result<PagedResult<CounterpartyBalanceDto>>> BalancesAsync(string direction = "AR")
    {
        Result<PagedResult<CounterpartyBalanceDto>> result = await _harness.Service.GetCounterpartyBalancesAsync(
            new CounterpartyBalanceQueryRequest { AsOfDate = AsOf, Direction = direction },
            new FilterRequest { Page = 1, PageSize = 50 },
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        return result;
    }
}
