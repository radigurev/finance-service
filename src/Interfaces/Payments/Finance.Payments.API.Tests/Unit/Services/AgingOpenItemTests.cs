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
/// Unit tests for the open-item aggregation and its two inclusion predicates (SDD-PAY-003 §2.1, §2.5, §6.1): the
/// outstanding formula, the EXPLICIT positive status set, the settleable-document-type predicate, the fully-settled
/// drop-out, the narrowings, the deterministic ordering, and the page-size cap.
/// </summary>
[TestFixture]
[Category("SDD-PAY-003")]
public sealed class AgingOpenItemTests
{
    private static readonly Guid CustomerA = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CustomerB = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

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
    public async Task OpenItems_Outstanding_EqualsGrossTotalMinusSettledAmount()
    {
        // Arrange
        await SeedAsync(InvoiceOpenItemBuilder.Create().WithGrossTotal(1000.00m).WithSettledAmount(250.75m));

        // Act
        Result<PagedResult<OpenItemDto>> result = await ListAsync(new OpenItemQueryRequest());

        // Assert
        OpenItemDto item = result.Value!.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.GrossTotal, Is.EqualTo(1000.00m));
            Assert.That(item.SettledAmount, Is.EqualTo(250.75m));
            Assert.That(item.Outstanding, Is.EqualTo(749.25m));
            Assert.That(item.SettlementStatus, Is.EqualTo(SettlementStatus.PartiallySettled));
        });
    }

    [Test]
    public async Task OpenItems_IncludesConfirmedAndPostedOnly()
    {
        // Arrange
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithDocumentNumber("SINV-1")
            .WithInvoiceStatus(InvoiceStatus.Confirmed));
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithDocumentNumber("SINV-2")
            .WithInvoiceStatus(InvoiceStatus.Posted));
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithDocumentNumber("SINV-3")
            .WithInvoiceStatus(InvoiceStatus.Cancelled));
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithDocumentNumber("SINV-4")
            .WithInvoiceStatus(InvoiceStatus.Reversed));

        // Act
        Result<PagedResult<OpenItemDto>> result = await ListAsync(new OpenItemQueryRequest());

        // Assert
        Assert.That(
            result.Value!.Items.Select(item => item.DocumentNumber),
            Is.EquivalentTo(new[] { "SINV-1", "SINV-2" }));
    }

    [Test]
    public async Task OpenItems_ExcludesCancelledInvoices_FromEveryTotal()
    {
        // Arrange
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithGrossTotal(500.00m)
            .WithInvoiceStatus(InvoiceStatus.Cancelled));

        // Act
        Result<PagedResult<OpenItemDto>> result = await ListAsync(new OpenItemQueryRequest());

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Items, Is.Empty);
            Assert.That(result.Value.TotalCount, Is.Zero);
        });
    }

    [Test]
    public async Task OpenItems_ExcludesReversedInvoices_FromEveryTotal()
    {
        // Arrange
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithGrossTotal(500.00m)
            .WithInvoiceStatus(InvoiceStatus.Reversed));

        // Act
        Result<PagedResult<OpenItemDto>> result = await ListAsync(new OpenItemQueryRequest());

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Items, Is.Empty);
            Assert.That(result.Value.TotalCount, Is.Zero);
        });
    }

    [Test]
    public async Task OpenItems_ExcludesCreditNotes_NoPaymentTypeCanSettleThem()
    {
        // Arrange
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithDocumentType(InvoiceDocumentType.CreditNote)
            .WithDocumentNumber("CN-2026-000001")
            .WithGrossTotal(200.00m));
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithDocumentType(InvoiceDocumentType.SaleInvoice)
            .WithDocumentNumber("SINV-2026-000001"));

        // Act
        Result<PagedResult<OpenItemDto>> result = await ListAsync(new OpenItemQueryRequest());

        // Assert
        Assert.That(
            result.Value!.Items.Select(item => item.DocumentType),
            Is.EqualTo(new[] { nameof(InvoiceDocumentType.SaleInvoice) }));
    }

    [Test]
    public async Task OpenItems_FullySettledItem_Omitted_ZeroOutstanding()
    {
        // Arrange
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithGrossTotal(500.00m)
            .WithSettledAmount(500.00m));

        // Act
        Result<PagedResult<OpenItemDto>> result = await ListAsync(new OpenItemQueryRequest());

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Items, Is.Empty, "a fully settled document is history, not an open item");
            Assert.That(result.Value.TotalCount, Is.Zero);
        });
    }

    [Test]
    public async Task OpenItems_DeallocatedItem_ReappearsWithOutstanding()
    {
        // Arrange
        PaymentAllocationTestHarness allocations = PaymentAllocationTestHarness.Build(_scope.Context);
        Payment payment = PaymentBuilder.Create().WithAmount(500.00m).WithCounterpartyId(CustomerA).Build();
        _scope.Context.Payments.Add(payment);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        InvoiceOpenItem openItem = await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithGrossTotal(500.00m)
            .WithCounterpartyId(CustomerA));
        Result<AllocatePaymentResultDto> allocated = await allocations.Service.AllocateAsync(
            payment.Id,
            new AllocatePaymentRequest
            {
                Items = [new AllocatePaymentItem { InvoiceId = openItem.InvoiceId, AllocatedAmount = 500.00m }],
                RowVersion = Convert.ToBase64String(payment.RowVersion)
            },
            CancellationToken.None);
        Assert.That(allocated.IsSuccess, Is.True, allocated.ErrorCode);
        Result<PagedResult<OpenItemDto>> whileSettled = await ListAsync(new OpenItemQueryRequest());

        // Act
        Result<DeallocatePaymentResultDto> released = await allocations.Service.DeallocateAsync(
            payment.Id,
            allocated.Value!.Allocations.Single().Id,
            allocated.Value.RowVersion,
            reason: null,
            CancellationToken.None);
        Result<PagedResult<OpenItemDto>> afterRelease = await ListAsync(new OpenItemQueryRequest());

        // Assert
        Assert.That(released.IsSuccess, Is.True, released.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(whileSettled.Value!.Items, Is.Empty);
            Assert.That(afterRelease.Value!.Items.Single().Outstanding, Is.EqualTo(500.00m));
        });
    }

    [Test]
    public async Task OpenItems_FiltersByDirection_ArExcludesAp()
    {
        // Arrange
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithDocumentType(InvoiceDocumentType.SaleInvoice)
            .WithDocumentNumber("SINV-1"));
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithDocumentType(InvoiceDocumentType.PurchaseInvoice)
            .WithDocumentNumber("PINV-1"));

        // Act
        Result<PagedResult<OpenItemDto>> receivables =
            await ListAsync(new OpenItemQueryRequest { Direction = nameof(InvoiceDirection.AR) });
        Result<PagedResult<OpenItemDto>> payables =
            await ListAsync(new OpenItemQueryRequest { Direction = nameof(InvoiceDirection.AP) });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(receivables.Value!.Items.Single().DocumentNumber, Is.EqualTo("SINV-1"));
            Assert.That(payables.Value!.Items.Single().DocumentNumber, Is.EqualTo("PINV-1"));
        });
    }

    [Test]
    public async Task OpenItems_FiltersByCounterpartyId()
    {
        // Arrange
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithCounterpartyId(CustomerA)
            .WithDocumentNumber("SINV-A"));
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithCounterpartyId(CustomerB)
            .WithDocumentNumber("SINV-B"));

        // Act
        Result<PagedResult<OpenItemDto>> result =
            await ListAsync(new OpenItemQueryRequest { CounterpartyId = CustomerA });

        // Assert
        Assert.That(result.Value!.Items.Single().DocumentNumber, Is.EqualTo("SINV-A"));
    }

    [Test]
    public async Task OpenItems_FiltersByCurrencyCode()
    {
        // Arrange
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithCurrencyCode("BGN")
            .WithDocumentNumber("SINV-BGN"));
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithCurrencyCode("EUR")
            .WithBookingExchangeRate(1.955830m)
            .WithDocumentNumber("SINV-EUR"));

        // Act
        Result<PagedResult<OpenItemDto>> result =
            await ListAsync(new OpenItemQueryRequest { CurrencyCode = "EUR" });

        // Assert
        Assert.That(result.Value!.Items.Single().DocumentNumber, Is.EqualTo("SINV-EUR"));
    }

    [Test]
    public async Task OpenItems_OverdueOnly_ExcludesNotYetDueItems()
    {
        // Arrange
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithDueDate(FixedTimeProvider.DefaultNow.AddDays(-10))
            .WithDocumentNumber("SINV-OVERDUE"));
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithDueDate(FixedTimeProvider.DefaultNow.AddDays(10))
            .WithDocumentNumber("SINV-FUTURE"));

        // Act
        Result<PagedResult<OpenItemDto>> overdueOnly =
            await ListAsync(new OpenItemQueryRequest { OverdueOnly = true });
        Result<PagedResult<OpenItemDto>> everything = await ListAsync(new OpenItemQueryRequest());

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(overdueOnly.Value!.Items.Single().DocumentNumber, Is.EqualTo("SINV-OVERDUE"));
            Assert.That(overdueOnly.Value.Items.Single().DaysPastDue, Is.EqualTo(10));
            Assert.That(everything.Value!.Items, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task OpenItems_OrderedByDueDateThenInvoiceId_Deterministic()
    {
        // Arrange
        DateTimeOffset sharedDueDate = new(2026, 5, 20, 0, 0, 0, TimeSpan.Zero);
        await SeedAsync(InvoiceOpenItemBuilder.Create()
            .WithDueDate(new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero))
            .WithDocumentNumber("SINV-LATEST"));
        await SeedAsync(InvoiceOpenItemBuilder.Create().WithDueDate(sharedDueDate).WithDocumentNumber("SINV-TIED-1"));
        await SeedAsync(InvoiceOpenItemBuilder.Create().WithDueDate(sharedDueDate).WithDocumentNumber("SINV-TIED-2"));

        // Act
        Result<PagedResult<OpenItemDto>> first = await ListAsync(new OpenItemQueryRequest());
        Result<PagedResult<OpenItemDto>> second = await ListAsync(new OpenItemQueryRequest());

        // Assert
        IReadOnlyList<DateTimeOffset> dueDates = [.. first.Value!.Items.Select(item => item.DueDate)];
        Assert.Multiple(() =>
        {
            Assert.That(dueDates, Is.Ordered.Ascending, "oldest-due-first so the list reads as a worklist");
            Assert.That(first.Value.Items[2].DocumentNumber, Is.EqualTo("SINV-LATEST"));
            Assert.That(
                second.Value!.Items.Select(item => item.InvoiceId),
                Is.EqualTo(first.Value.Items.Select(item => item.InvoiceId)),
                "the projection key is appended as the final deterministic sort term");
        });
    }

    [Test]
    public async Task OpenItems_RespectsPageSizeCap_200()
    {
        // Arrange
        await SeedAsync(InvoiceOpenItemBuilder.Create());

        // Act
        Result<PagedResult<OpenItemDto>> result = await _harness.Service.GetOpenItemsAsync(
            new OpenItemQueryRequest(),
            new FilterRequest { Page = 1, PageSize = 201 },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(FilterErrorCodes.PAGE_SIZE_TOO_LARGE));
        });
    }

    [Test]
    public async Task OpenItems_UnallocatedPayment_ProducesNoNegativeItem()
    {
        // Arrange — a confirmed payment with an unallocated amount and no open invoice at all.
        Payment payment = PaymentBuilder.Create().WithAmount(750.00m).WithCounterpartyId(CustomerA).Build();
        _scope.Context.Payments.Add(payment);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);

        // Act
        Result<PagedResult<OpenItemDto>> result = await ListAsync(new OpenItemQueryRequest());

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(payment.UnallocatedAmount, Is.EqualTo(750.00m));
            Assert.That(result.Value!.Items, Is.Empty, "payments are never aged in v1");
            Assert.That(result.Value.TotalCount, Is.Zero);
        });
    }

    /// <summary>Persists an open-item projection row.</summary>
    /// <param name="builder">The configured builder.</param>
    /// <returns>The persisted open item.</returns>
    private async Task<InvoiceOpenItem> SeedAsync(InvoiceOpenItemBuilder builder)
    {
        InvoiceOpenItem openItem = builder.Build();
        _scope.Context.InvoiceOpenItems.Add(openItem);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        return openItem;
    }

    /// <summary>Reads the open-item page for the supplied narrowing with the default paging.</summary>
    /// <param name="query">The query narrowing.</param>
    /// <returns>The open-item page result.</returns>
    private async Task<Result<PagedResult<OpenItemDto>>> ListAsync(OpenItemQueryRequest query)
    {
        Result<PagedResult<OpenItemDto>> result = await _harness.Service.GetOpenItemsAsync(
            query,
            new FilterRequest { Page = 1, PageSize = 50 },
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        return result;
    }
}
