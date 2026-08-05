using System.Reflection;
using Finance.Common.Enums;
using Finance.Common.Results;
using Finance.Payments.API.Interfaces;
using Finance.Payments.API.Services;
using Finance.Payments.API.Tests.Builders;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Events.Payments;
using Finance.ServiceModel.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the derived settlement state and the DORMANT realized-FX seam (SDD-PAY-002 §2.8, §2.9, §6.3): the
/// exact two-decimal derivation with no tolerance band, the country-rounded base allocated amount, the signed
/// document-level FX difference, and the seam being invoked once per allocation row INSIDE the transaction while
/// posting nothing.
/// </summary>
[TestFixture]
[Category("SDD-PAY-002")]
public sealed class SettlementDerivationTests
{
    private SqlitePaymentsDbContextScope _scope = null!;
    private PaymentAllocationTestHarness _harness = null!;
    private SettlementStatusCalculator _settlement = null!;
    private FakePaymentCountryStrategy _country = null!;
    private AllocationAmountCalculator _amounts = null!;

    /// <summary>Creates fresh calculators and a fresh SQLite-backed harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqlitePaymentsDbContextFactory.Create();
        _harness = PaymentAllocationTestHarness.Build(_scope.Context);
        _settlement = new SettlementStatusCalculator();
        _country = new FakePaymentCountryStrategy();
        _amounts = new AllocationAmountCalculator(_country);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    [Test]
    public void SettlementStatus_NothingAllocated_IsUnsettled()
    {
        // Arrange
        decimal grossTotal = 1000.00m;

        // Act
        SettlementStatus status = _settlement.Calculate(0.00m, grossTotal);

        // Assert
        Assert.That(status, Is.EqualTo(SettlementStatus.Unsettled));
    }

    [Test]
    public void SettlementStatus_PartialAllocation_IsPartiallySettled()
    {
        // Arrange
        decimal grossTotal = 1000.00m;

        // Act
        SettlementStatus status = _settlement.Calculate(400.00m, grossTotal);

        // Assert
        Assert.That(status, Is.EqualTo(SettlementStatus.PartiallySettled));
    }

    [Test]
    public void SettlementStatus_ExactGrossTotal_IsSettled_ExactDecimalComparison()
    {
        // Arrange
        decimal grossTotal = 1000.00m;

        // Act
        SettlementStatus status = _settlement.Calculate(1000.00m, grossTotal);

        // Assert
        Assert.That(status, Is.EqualTo(SettlementStatus.Settled));
    }

    [Test]
    public void SettlementStatus_OneCentShortOfGrossTotal_IsPartiallySettled_NoTolerance()
    {
        // Arrange
        decimal grossTotal = 1000.00m;

        // Act
        SettlementStatus status = _settlement.Calculate(999.99m, grossTotal);

        // Assert
        Assert.That(
            status,
            Is.EqualTo(SettlementStatus.PartiallySettled),
            "there is no tolerance band and no automatic write-off");
    }

    [Test]
    public void BaseAllocatedAmount_RoundedViaCountryStrategy_NotInlineRounding()
    {
        // Arrange
        Payment payment = PaymentBuilder.Create().WithCurrencyCode("EUR").WithExchangeRate(1.955830m).Build();
        InvoiceOpenItem openItem = InvoiceOpenItemBuilder.Create()
            .WithCurrencyCode("EUR")
            .WithBookingExchangeRate(1.955830m)
            .Build();

        // Act
        AllocationAmounts amounts = _amounts.Compute(payment, openItem, 333.33m);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(amounts.BaseAllocatedAmount, Is.EqualTo(651.94m));
            Assert.That(
                _country.ApplyTaxRoundingCallCount,
                Is.EqualTo(2),
                "both the base amount and the FX difference are rounded through the country strategy");
        });
    }

    [Test]
    public void RealizedFx_SameBookingRate_IsZero()
    {
        // Arrange
        Payment payment = PaymentBuilder.Create().WithExchangeRate(1.955830m).Build();
        InvoiceOpenItem openItem = InvoiceOpenItemBuilder.Create()
            .WithBookingExchangeRate(1.955830m)
            .Build();

        // Act
        AllocationAmounts amounts = _amounts.Compute(payment, openItem, 500.00m);

        // Assert
        Assert.That(amounts.RealizedFxDifference, Is.EqualTo(0.00m));
    }

    [Test]
    public void RealizedFx_DifferentBookingRate_StoresSignedBaseDifference()
    {
        // Arrange
        Payment strongerPayment = PaymentBuilder.Create().WithExchangeRate(2.000000m).Build();
        Payment weakerPayment = PaymentBuilder.Create().WithExchangeRate(1.900000m).Build();
        InvoiceOpenItem openItem = InvoiceOpenItemBuilder.Create()
            .WithBookingExchangeRate(1.950000m)
            .Build();

        // Act
        AllocationAmounts positive = _amounts.Compute(strongerPayment, openItem, 1000.00m);
        AllocationAmounts negative = _amounts.Compute(weakerPayment, openItem, 1000.00m);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(positive.RealizedFxDifference, Is.EqualTo(50.00m));
            Assert.That(negative.RealizedFxDifference, Is.EqualTo(-50.00m));
        });
    }

    [Test]
    public async Task Allocate_InvokesRealizedFxHandler_OncePerAllocationRow_InsideTransaction()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem first = await SeedOpenItemAsync(400.00m, "SINV-2026-000001");
        InvoiceOpenItem second = await SeedOpenItemAsync(400.00m, "SINV-2026-000002");

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(
            payment, (first.InvoiceId, 100.00m), (second.InvoiceId, 200.00m));

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        IReadOnlyList<RealizedFxContext> invocations = _harness.RealizedFx.Invocations;
        Assert.Multiple(() =>
        {
            Assert.That(invocations, Has.Count.EqualTo(2), "once per allocation row, even when the value is zero");
            Assert.That(invocations[0].InvoiceId, Is.EqualTo(first.InvoiceId));
            Assert.That(invocations[0].AllocatedAmount, Is.EqualTo(100.00m));
            Assert.That(invocations[1].InvoiceId, Is.EqualTo(second.InvoiceId));
            Assert.That(invocations[1].AllocatedAmount, Is.EqualTo(200.00m));
            Assert.That(
                invocations,
                Has.All.Property(nameof(RealizedFxContext.PaymentId)).EqualTo(payment.Id));
            Assert.That(
                invocations,
                Has.All.Property(nameof(RealizedFxContext.RealizedFxDifference)).EqualTo(0.00m));
        });
    }

    [Test]
    public async Task Allocate_RealizedFxHandlerReturnsFailure_FailsWholeAllocation_WritesNothing()
    {
        // Arrange
        _harness.RealizedFx.ShouldFail = true;
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(400.00m);

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 100.00m));

        // Assert
        int rowCount = await _scope.Context.PaymentAllocations.CountAsync(CancellationToken.None);
        InvoiceOpenItem storedItem = await LoadOpenItemAsync(openItem.InvoiceId);
        Payment storedPayment = await LoadPaymentAsync(payment.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(RecordingRealizedFxHandler.FailureCode));
            Assert.That(rowCount, Is.Zero);
            Assert.That(storedItem.SettledAmount, Is.EqualTo(0.00m));
            Assert.That(storedPayment.AllocatedAmount, Is.EqualTo(0.00m));
            Assert.That(_harness.PublishedEvents, Is.Empty);
            Assert.That(_harness.RecordedAudits, Is.Empty);
        });
    }

    [Test]
    public async Task RealizedFx_NoOpHandlerInvoked_PostsNothing_NoGlEffect()
    {
        // Arrange — the PRODUCTION handler, so the dormant seam's real v1 behaviour is exercised.
        PaymentAllocationTestHarness production = PaymentAllocationTestHarness.Build(
            _scope.Context,
            new NoOpRealizedFxHandler(NullLogger<NoOpRealizedFxHandler>.Instance));
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(400.00m);
        AllocatePaymentRequest request = new()
        {
            Items = [new AllocatePaymentItem { InvoiceId = openItem.InvoiceId, AllocatedAmount = 100.00m }],
            RowVersion = Convert.ToBase64String(payment.RowVersion)
        };

        // Act
        Result<AllocatePaymentResultDto> result =
            await production.Service.AllocateAsync(payment.Id, request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(production.PublishedEvents, Has.All.InstanceOf<PaymentAllocatedEvent>());
            Assert.That(production.EventsOf<PaymentConfirmedEvent>(), Is.Empty, "nothing is posted");
            Assert.That(result.Value!.Allocations.Single().RealizedFxDifference, Is.EqualTo(0.00m));
        });
    }

    [Test]
    public async Task RealizedFx_ExcludedFromAllocatedAmount_SettledAmount_AndSettlementStatus()
    {
        // Arrange — the payment's rate differs from the invoice's booking rate, so the difference is non-zero.
        Payment payment = PaymentBuilder.Create()
            .WithCurrencyCode("EUR")
            .WithAmount(1000.00m)
            .WithExchangeRate(2.000000m)
            .Build();
        _scope.Context.Payments.Add(payment);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        InvoiceOpenItem openItem = InvoiceOpenItemBuilder.Create()
            .WithCurrencyCode("EUR")
            .WithGrossTotal(500.00m)
            .WithBookingExchangeRate(1.950000m)
            .Build();
        _scope.Context.InvoiceOpenItems.Add(openItem);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 500.00m));

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        PaymentAllocationDto row = result.Value!.Allocations.Single();
        InvoiceOpenItem storedItem = await LoadOpenItemAsync(openItem.InvoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(row.RealizedFxDifference, Is.EqualTo(25.00m));
            Assert.That(row.AllocatedAmount, Is.EqualTo(500.00m));
            Assert.That(row.BaseAllocatedAmount, Is.EqualTo(1000.00m));
            Assert.That(result.Value.AllocatedAmount, Is.EqualTo(500.00m), "never netted into the allocated total");
            Assert.That(storedItem.SettledAmount, Is.EqualTo(500.00m));
            Assert.That(
                result.Value.AffectedInvoices.Single().SettlementStatus,
                Is.EqualTo(SettlementStatus.Settled),
                "the FX difference never affects the derived settlement status");
        });
    }

    [Test]
    public void Allocation_UsesDecimalArithmetic_NoFloatingPoint()
    {
        // Arrange
        MethodInfo compute = typeof(AllocationAmountCalculator)
            .GetMethod(nameof(AllocationAmountCalculator.Compute))!;

        // Act
        IReadOnlyList<Type> floatingPointMembers =
        [
            .. typeof(PaymentAllocation).GetProperties()
                .Concat(typeof(InvoiceOpenItem).GetProperties())
                .Concat(typeof(AllocationAmounts).GetProperties())
                .Select(property => property.PropertyType)
                .Where(type => type == typeof(double) || type == typeof(float))
        ];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(compute.ReturnType, Is.EqualTo(typeof(AllocationAmounts)));
            Assert.That(
                compute.GetParameters()[2].ParameterType,
                Is.EqualTo(typeof(decimal)),
                "the allocated amount is decimal");
            Assert.That(floatingPointMembers, Is.Empty);
            Assert.That(
                _amounts.Compute(
                    PaymentBuilder.Create().WithExchangeRate(1.000001m).Build(),
                    InvoiceOpenItemBuilder.Create().WithBookingExchangeRate(1.000000m).Build(),
                    100000.00m).RealizedFxDifference,
                Is.EqualTo(0.10m));
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
    private Task<Result<AllocatePaymentResultDto>> AllocateAsync(
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

        return _harness.Service.AllocateAsync(payment.Id, request, CancellationToken.None);
    }

    /// <summary>Reads the persisted payment without tracking.</summary>
    /// <param name="id">The payment identifier.</param>
    /// <returns>The persisted payment.</returns>
    private Task<Payment> LoadPaymentAsync(Guid id) => _scope.Context.Payments
        .AsNoTracking()
        .SingleAsync(payment => payment.Id == id, CancellationToken.None);

    /// <summary>Reads the persisted open item without tracking.</summary>
    /// <param name="invoiceId">The invoice identifier.</param>
    /// <returns>The persisted open item.</returns>
    private Task<InvoiceOpenItem> LoadOpenItemAsync(Guid invoiceId) => _scope.Context.InvoiceOpenItems
        .AsNoTracking()
        .SingleAsync(item => item.InvoiceId == invoiceId, CancellationToken.None);
}
