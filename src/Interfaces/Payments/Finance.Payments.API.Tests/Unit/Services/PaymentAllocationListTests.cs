using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
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
/// Unit tests for the payment-scoped allocation list (SDD-PAY-002 §2.7, §6.5): the default ordering, the enrichment
/// from the LOCAL open-item projection with no cross-service read, the empty page for an unallocated payment, the
/// page-size cap, and the recompute-on-every-read no-caching rule.
/// <para>Reads go through <c>ListAsync</c> — never the inherited <c>SearchAsync</c>, which is unscoped and has no
/// <c>PaymentAllocation → PaymentAllocationDto</c> map.</para>
/// </summary>
[TestFixture]
[Category("SDD-PAY-002")]
public sealed class PaymentAllocationListTests
{
    private SqlitePaymentsDbContextScope _scope = null!;
    private PaymentAllocationTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqlitePaymentsDbContextFactory.Create();
        _harness = PaymentAllocationTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    [Test]
    public async Task ListAllocations_ReturnsPagedResultOrderedByAllocatedAtDescendingThenId()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem oldest = await SeedOpenItemAsync(500.00m, "SINV-2026-000001");
        InvoiceOpenItem newest = await SeedOpenItemAsync(500.00m, "SINV-2026-000002");
        _harness.Clock.UtcNow = FixedTimeProvider.DefaultNow;
        await AllocateAsync(payment, (oldest.InvoiceId, 100.00m));
        _harness.Clock.UtcNow = FixedTimeProvider.DefaultNow.AddHours(4);
        await AllocateAsync(payment, (newest.InvoiceId, 200.00m));

        // Act
        Result<PagedResult<PaymentAllocationDto>> result = await _harness.Service.ListAsync(
            payment.Id,
            new FilterRequest { Page = 1, PageSize = 50 },
            CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        IReadOnlyList<PaymentAllocationDto> items = result.Value!.Items;
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].InvoiceId, Is.EqualTo(newest.InvoiceId));
            Assert.That(items[1].InvoiceId, Is.EqualTo(oldest.InvoiceId));
            Assert.That(result.Value.TotalCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ListAllocations_EnrichesFromLocalProjection_NoCrossServiceRead()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(500.00m);
        await AllocateAsync(payment, (openItem.InvoiceId, 250.00m));

        // Act
        Result<PagedResult<PaymentAllocationDto>> result = await _harness.Service.ListAsync(
            payment.Id,
            new FilterRequest { Page = 1, PageSize = 50 },
            CancellationToken.None);

        // Assert
        PaymentAllocationDto row = result.Value!.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.InvoiceDocumentNumber, Is.EqualTo(openItem.DocumentNumber));
            Assert.That(row.InvoiceDueDate, Is.EqualTo(openItem.DueDate));
            Assert.That(row.InvoiceStatus, Is.EqualTo(nameof(InvoiceStatus.Confirmed)));
            Assert.That(row.InvoiceGrossTotal, Is.EqualTo(500.00m));
            Assert.That(row.InvoiceSettlementStatus, Is.EqualTo(SettlementStatus.PartiallySettled));
            Assert.That(row.AllocatedAmount, Is.EqualTo(250.00m));
        });
    }

    [Test]
    public async Task ListAllocations_UnknownPayment_ReturnsPaymentNotFound()
    {
        // Arrange
        Guid unknownPaymentId = Guid.NewGuid();

        // Act
        Result<PagedResult<PaymentAllocationDto>> result = await _harness.Service.ListAsync(
            unknownPaymentId,
            new FilterRequest { Page = 1, PageSize = 50 },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_NOT_FOUND));
        });
    }

    [Test]
    public async Task ListAllocations_PaymentWithoutAllocations_ReturnsEmptyPage()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);

        // Act
        Result<PagedResult<PaymentAllocationDto>> result = await _harness.Service.ListAsync(
            payment.Id,
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
    public async Task ListAllocations_ScopesToTheRoutePayment_NeverAnotherPaymentsRows()
    {
        // Arrange
        Payment owner = await SeedPaymentAsync(1000.00m);
        Payment other = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem ownerItem = await SeedOpenItemAsync(500.00m, "SINV-2026-000001");
        InvoiceOpenItem otherItem = await SeedOpenItemAsync(500.00m, "SINV-2026-000002");
        await AllocateAsync(owner, (ownerItem.InvoiceId, 100.00m));
        await AllocateAsync(other, (otherItem.InvoiceId, 200.00m));

        // Act
        Result<PagedResult<PaymentAllocationDto>> result = await _harness.Service.ListAsync(
            owner.Id,
            new FilterRequest { Page = 1, PageSize = 50 },
            CancellationToken.None);

        // Assert
        Assert.That(result.Value!.Items.Single().InvoiceId, Is.EqualTo(ownerItem.InvoiceId));
    }

    [Test]
    public async Task ListAllocations_RespectsPageSizeCap_200()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);

        // Act
        Result<PagedResult<PaymentAllocationDto>> result = await _harness.Service.ListAsync(
            payment.Id,
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
    public async Task ListAllocations_DoesNotCacheTransactionalData()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem first = await SeedOpenItemAsync(500.00m, "SINV-2026-000001");
        InvoiceOpenItem second = await SeedOpenItemAsync(500.00m, "SINV-2026-000002");
        await AllocateAsync(payment, (first.InvoiceId, 100.00m));
        Result<PagedResult<PaymentAllocationDto>> before = await _harness.Service.ListAsync(
            payment.Id, new FilterRequest { Page = 1, PageSize = 50 }, CancellationToken.None);

        // Act
        await AllocateAsync(payment, (second.InvoiceId, 200.00m));
        Result<PagedResult<PaymentAllocationDto>> after = await _harness.Service.ListAsync(
            payment.Id, new FilterRequest { Page = 1, PageSize = 50 }, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(before.Value!.TotalCount, Is.EqualTo(1));
            Assert.That(after.Value!.TotalCount, Is.EqualTo(2), "every read recomputes from the database");
        });
    }

    /// <summary>Persists a confirmed base-currency payment of the supplied amount.</summary>
    /// <param name="amount">The payment amount.</param>
    /// <returns>The persisted payment.</returns>
    private async Task<Payment> SeedPaymentAsync(decimal amount)
    {
        Payment payment = PaymentBuilder.Create().WithAmount(amount).Build();
        _scope.Context.Payments.Add(payment);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        return payment;
    }

    /// <summary>Persists a confirmed, unsettled sale-invoice open item.</summary>
    /// <param name="grossTotal">The invoice gross total.</param>
    /// <param name="documentNumber">The invoice document number.</param>
    /// <returns>The persisted open item.</returns>
    private async Task<InvoiceOpenItem> SeedOpenItemAsync(
        decimal grossTotal,
        string documentNumber = "SINV-2026-000001")
    {
        InvoiceOpenItem openItem = InvoiceOpenItemBuilder.Create()
            .WithGrossTotal(grossTotal)
            .WithDocumentNumber(documentNumber)
            .Build();
        _scope.Context.InvoiceOpenItems.Add(openItem);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        return openItem;
    }

    /// <summary>Allocates the supplied items against the payment using its current concurrency token.</summary>
    /// <param name="payment">The payment to match.</param>
    /// <param name="items">The invoice/amount pairs to request.</param>
    /// <returns>The allocation result.</returns>
    private async Task<Result<AllocatePaymentResultDto>> AllocateAsync(
        Payment payment,
        params (Guid InvoiceId, decimal Amount)[] items)
    {
        AllocatePaymentRequest request = new()
        {
            Items =
            [
                .. items.Select(item => new AllocatePaymentItem
                {
                    InvoiceId = item.InvoiceId,
                    AllocatedAmount = item.Amount
                })
            ],
            RowVersion = Convert.ToBase64String(payment.RowVersion)
        };

        Result<AllocatePaymentResultDto> result =
            await _harness.Service.AllocateAsync(payment.Id, request, CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        return result;
    }
}
