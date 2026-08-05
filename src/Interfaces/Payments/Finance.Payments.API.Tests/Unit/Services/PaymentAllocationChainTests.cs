using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Payments.API.Tests.Builders;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the TEN-rule allocation invariant chain (SDD-PAY-002 §2.5, §6.1). Each rule must fail with its own
/// exact error code, the chain must short-circuit on the FIRST failure and write nothing, and the registration order
/// is pinned behaviourally: a request that breaks two rules must surface the EARLIER rule's code.
/// </summary>
[TestFixture]
[Category("SDD-PAY-002")]
public sealed class PaymentAllocationChainTests
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
    public async Task Allocate_ValidSingleItem_CreatesAllocationRow_AndIncrementsPaymentAllocatedAmount()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create().WithAmount(1000.00m));
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create().WithGrossTotal(1000.00m));

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 400.00m));

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        PaymentAllocation row = await _scope.Context.PaymentAllocations
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(row.PaymentId, Is.EqualTo(payment.Id));
            Assert.That(row.InvoiceId, Is.EqualTo(openItem.InvoiceId));
            Assert.That(row.AllocatedAmount, Is.EqualTo(400.00m));
            Assert.That(result.Value!.AllocatedAmount, Is.EqualTo(400.00m));
            Assert.That(result.Value.UnallocatedAmount, Is.EqualTo(600.00m));
        });
    }

    [Test]
    public async Task Allocate_UnknownPayment_ReturnsPaymentNotFound()
    {
        // Arrange
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create());
        AllocatePaymentRequest request = new()
        {
            Items = [new AllocatePaymentItem { InvoiceId = openItem.InvoiceId, AllocatedAmount = 10.00m }],
            RowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        };

        // Act
        Result<AllocatePaymentResultDto> result =
            await _harness.Service.AllocateAsync(Guid.NewGuid(), request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_NOT_FOUND));
        });
    }

    [Test]
    public async Task Allocate_DraftPayment_ReturnsPaymentNotAllocatable()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Draft)
            .WithDocumentNumber(null));
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create());

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 100.00m));

        // Assert
        await AssertFailedAndWroteNothingAsync(result, PaymentErrorCodes.PAYMENT_NOT_ALLOCATABLE);
    }

    [Test]
    public async Task Allocate_ReversedPayment_ReturnsPaymentNotAllocatable()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Reversed)
            .WithJournalEntryId(Guid.NewGuid()));
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create());

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 100.00m));

        // Assert
        await AssertFailedAndWroteNothingAsync(result, PaymentErrorCodes.PAYMENT_NOT_ALLOCATABLE);
    }

    [Test]
    public async Task Allocate_InvoiceMissingFromProjection_ReturnsPaymentAllocationInvoiceNotFound()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create());

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (Guid.NewGuid(), 100.00m));

        // Assert
        await AssertFailedAndWroteNothingAsync(result, PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_NOT_FOUND);
    }

    [Test]
    public async Task Allocate_CancelledInvoiceOpenItem_ReturnsPaymentAllocationInvoiceNotEligible()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create());
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithInvoiceStatus(InvoiceStatus.Cancelled));

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 100.00m));

        // Assert
        await AssertFailedAndWroteNothingAsync(
            result, PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_NOT_ELIGIBLE);
    }

    [Test]
    public async Task Allocate_ReversedInvoiceOpenItem_ReturnsPaymentAllocationInvoiceNotEligible()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create());
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithInvoiceStatus(InvoiceStatus.Reversed));

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 100.00m));

        // Assert
        await AssertFailedAndWroteNothingAsync(
            result, PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_NOT_ELIGIBLE);
    }

    [Test]
    public async Task Allocate_DirectionMismatch_ReturnsPaymentAllocationDirectionMismatch()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create()
            .WithDocumentType(PaymentDocumentType.CustomerReceipt));
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithDocumentType(InvoiceDocumentType.PurchaseInvoice));

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 100.00m));

        // Assert
        await AssertFailedAndWroteNothingAsync(
            result, PaymentErrorCodes.PAYMENT_ALLOCATION_DIRECTION_MISMATCH);
    }

    [Test]
    public async Task Allocate_CounterpartyMismatch_ReturnsPaymentAllocationCounterpartyMismatch()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create());
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithCounterpartyId(Guid.NewGuid()));

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 100.00m));

        // Assert
        await AssertFailedAndWroteNothingAsync(
            result, PaymentErrorCodes.PAYMENT_ALLOCATION_COUNTERPARTY_MISMATCH);
    }

    [Test]
    public async Task Allocate_CurrencyMismatch_ReturnsPaymentAllocationCurrencyMismatch()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create());
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithCurrencyCode("EUR"));

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 100.00m));

        // Assert
        await AssertFailedAndWroteNothingAsync(result, PaymentErrorCodes.PAYMENT_ALLOCATION_CURRENCY_MISMATCH);
    }

    [Test]
    public async Task Allocate_SupplierPaymentAgainstSeededCreditNoteOpenItem_ReturnsPaymentAllocationControlAccountMismatch()
    {
        // Arrange — §2.3 never projects a CreditNote, so the open item MUST be seeded DIRECTLY. Its AP direction
        // matches a supplier payment, which is precisely why the direction rule cannot catch this pair.
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create()
            .WithDocumentType(PaymentDocumentType.SupplierPayment));
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithDocumentType(InvoiceDocumentType.CreditNote)
            .WithDocumentNumber("CN-2026-000001"));

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 100.00m));

        // Assert
        await AssertFailedAndWroteNothingAsync(
            result, PaymentErrorCodes.PAYMENT_ALLOCATION_CONTROL_ACCOUNT_MISMATCH);
    }

    [Test]
    public async Task Allocate_CustomerReceiptAgainstDebitNote_Succeeds_DocumentedSettlementPair()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create()
            .WithDocumentType(PaymentDocumentType.CustomerReceipt));
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithDocumentType(InvoiceDocumentType.DebitNote)
            .WithDocumentNumber("DN-2026-000001"));

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 100.00m));

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.That(await _scope.Context.PaymentAllocations.CountAsync(CancellationToken.None), Is.EqualTo(1));
    }

    [Test]
    public async Task Allocate_ExistingPairAgain_ReturnsPaymentAllocationDuplicate()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create().WithAmount(1000.00m));
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithGrossTotal(1000.00m));
        Result<AllocatePaymentResultDto> first = await AllocateAsync(payment, (openItem.InvoiceId, 100.00m));
        Assert.That(first.IsSuccess, Is.True, first.ErrorCode);

        // Act
        Result<AllocatePaymentResultDto> second = await AllocateAsync(payment, (openItem.InvoiceId, 100.00m));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(second.IsSuccess, Is.False);
            Assert.That(second.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_ALLOCATION_DUPLICATE));
            Assert.That(_scope.Context.PaymentAllocations.Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Allocate_SameInvoiceTwiceInOneRequest_ReturnsPaymentAllocationDuplicate()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create().WithAmount(1000.00m));
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithGrossTotal(1000.00m));

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(
            payment,
            (openItem.InvoiceId, 100.00m),
            (openItem.InvoiceId, 200.00m));

        // Assert
        await AssertFailedAndWroteNothingAsync(result, PaymentErrorCodes.PAYMENT_ALLOCATION_DUPLICATE);
    }

    [Test]
    public async Task Allocate_ExistingPlusRequestedExceedPaymentAmount_ReturnsPaymentAllocationExceedsPayment()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create().WithAmount(1000.00m));
        InvoiceOpenItem first = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithGrossTotal(1000.00m));
        InvoiceOpenItem second = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithGrossTotal(1000.00m)
            .WithDocumentNumber("SINV-2026-000002"));
        Result<AllocatePaymentResultDto> existing = await AllocateAsync(payment, (first.InvoiceId, 900.00m));
        Assert.That(existing.IsSuccess, Is.True, existing.ErrorCode);

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (second.InvoiceId, 200.00m));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_ALLOCATION_EXCEEDS_PAYMENT));
            Assert.That(_scope.Context.PaymentAllocations.Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Allocate_MultipleItemsSumExceedPaymentAmount_ReturnsPaymentAllocationExceedsPayment()
    {
        // Arrange — each item fits its own invoice's outstanding, but the item SUM exceeds the payment.
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create().WithAmount(1000.00m));
        InvoiceOpenItem first = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithGrossTotal(700.00m));
        InvoiceOpenItem second = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithGrossTotal(700.00m)
            .WithDocumentNumber("SINV-2026-000002"));

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(
            payment,
            (first.InvoiceId, 600.00m),
            (second.InvoiceId, 600.00m));

        // Assert
        await AssertFailedAndWroteNothingAsync(result, PaymentErrorCodes.PAYMENT_ALLOCATION_EXCEEDS_PAYMENT);
    }

    [Test]
    public async Task Allocate_OneCentOverOutstanding_ReturnsPaymentAllocationExceedsOutstanding()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create().WithAmount(1000.00m));
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithGrossTotal(500.00m)
            .WithSettledAmount(100.00m));

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 400.01m));

        // Assert
        await AssertFailedAndWroteNothingAsync(
            result, PaymentErrorCodes.PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING);
    }

    [Test]
    public async Task Allocate_ExactOutstandingToTheCent_Succeeds()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create().WithAmount(1000.00m));
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithGrossTotal(500.00m)
            .WithSettledAmount(100.00m));

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 400.00m));

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.That(
            result.Value!.AffectedInvoices.Single().SettlementStatus,
            Is.EqualTo(SettlementStatus.Settled));
    }

    [Test]
    public async Task Allocate_ChainShortCircuitsOnFirstFailure_WritesNothing()
    {
        // Arrange — the payment is Draft (rule 1) AND the invoice is unknown (rule 2): rule 1 must win.
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Draft)
            .WithDocumentNumber(null));

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (Guid.NewGuid(), 100.00m));

        // Assert
        await AssertFailedAndWroteNothingAsync(result, PaymentErrorCodes.PAYMENT_NOT_ALLOCATABLE);
        Assert.Multiple(() =>
        {
            Assert.That(_harness.RecordedAudits, Is.Empty);
            Assert.That(_harness.PublishedEvents, Is.Empty);
            Assert.That(_harness.RealizedFx.Invocations, Is.Empty);
        });
    }

    [Test]
    public async Task AllocationChain_RegistersValidatorsInDocumentedOrder()
    {
        // Arrange
        string stateBeforeUnknownInvoice = await DraftPaymentAgainstUnknownInvoiceAsync();
        string eligibilityBeforeDirection = await CancelledApItemAgainstArReceiptAsync();
        string directionBeforeCounterparty = await ApItemWithForeignCounterpartyAsync();
        string counterpartyBeforeCurrency = await ForeignCounterpartyWithForeignCurrencyAsync();
        string paymentBoundBeforeOutstandingBound = await ItemSumOverPaymentAndOverOutstandingAsync();
        string outstandingBoundBeforeControlAccount = await CreditNoteOverOutstandingAsync();

        // Act
        IReadOnlyList<string> observed =
        [
            stateBeforeUnknownInvoice,
            eligibilityBeforeDirection,
            directionBeforeCounterparty,
            counterpartyBeforeCurrency,
            paymentBoundBeforeOutstandingBound,
            outstandingBoundBeforeControlAccount
        ];

        // Assert
        Assert.That(observed, Is.EqualTo(new[]
        {
            PaymentErrorCodes.PAYMENT_NOT_ALLOCATABLE,
            PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_NOT_ELIGIBLE,
            PaymentErrorCodes.PAYMENT_ALLOCATION_DIRECTION_MISMATCH,
            PaymentErrorCodes.PAYMENT_ALLOCATION_COUNTERPARTY_MISMATCH,
            PaymentErrorCodes.PAYMENT_ALLOCATION_EXCEEDS_PAYMENT,
            PaymentErrorCodes.PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING
        }));
    }

    /// <summary>Rule 1 must fire before rule 2: a draft payment against an invoice the projection never saw.</summary>
    /// <returns>The failing error code.</returns>
    private async Task<string> DraftPaymentAgainstUnknownInvoiceAsync()
    {
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Draft)
            .WithDocumentNumber(null));
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (Guid.NewGuid(), 10.00m));
        return result.ErrorCode!;
    }

    /// <summary>Rule 3 must fire before rule 4: a cancelled AP item requested by an AR receipt.</summary>
    /// <returns>The failing error code.</returns>
    private async Task<string> CancelledApItemAgainstArReceiptAsync()
    {
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create()
            .WithDocumentType(PaymentDocumentType.CustomerReceipt));
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithDocumentType(InvoiceDocumentType.PurchaseInvoice)
            .WithInvoiceStatus(InvoiceStatus.Cancelled));
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 10.00m));
        return result.ErrorCode!;
    }

    /// <summary>Rule 4 must fire before rule 5: an AP item with a foreign counterparty for an AR receipt.</summary>
    /// <returns>The failing error code.</returns>
    private async Task<string> ApItemWithForeignCounterpartyAsync()
    {
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create()
            .WithDocumentType(PaymentDocumentType.CustomerReceipt));
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithDocumentType(InvoiceDocumentType.PurchaseInvoice)
            .WithCounterpartyId(Guid.NewGuid()));
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 10.00m));
        return result.ErrorCode!;
    }

    /// <summary>Rule 5 must fire before rule 6: a foreign counterparty AND a foreign currency.</summary>
    /// <returns>The failing error code.</returns>
    private async Task<string> ForeignCounterpartyWithForeignCurrencyAsync()
    {
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create());
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithCounterpartyId(Guid.NewGuid())
            .WithCurrencyCode("EUR"));
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 10.00m));
        return result.ErrorCode!;
    }

    /// <summary>Rule 8 must fire before rule 9: the item sum breaks the payment bound AND an invoice bound.</summary>
    /// <returns>The failing error code.</returns>
    private async Task<string> ItemSumOverPaymentAndOverOutstandingAsync()
    {
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create().WithAmount(100.00m));
        InvoiceOpenItem first = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create().WithGrossTotal(50.00m));
        InvoiceOpenItem second = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithGrossTotal(50.00m)
            .WithDocumentNumber("SINV-2026-000099"));
        Result<AllocatePaymentResultDto> result = await AllocateAsync(
            payment, (first.InvoiceId, 80.00m), (second.InvoiceId, 80.00m));
        return result.ErrorCode!;
    }

    /// <summary>Rule 9 must fire before rule 10: an over-outstanding request against a credit-note item.</summary>
    /// <returns>The failing error code.</returns>
    private async Task<string> CreditNoteOverOutstandingAsync()
    {
        Payment payment = await SeedPaymentAsync(PaymentBuilder.Create()
            .WithDocumentType(PaymentDocumentType.SupplierPayment)
            .WithAmount(1000.00m));
        InvoiceOpenItem openItem = await SeedOpenItemAsync(InvoiceOpenItemBuilder.Create()
            .WithDocumentType(InvoiceDocumentType.CreditNote)
            .WithDocumentNumber("CN-2026-000099")
            .WithGrossTotal(100.00m));
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 200.00m));
        return result.ErrorCode!;
    }

    /// <summary>Persists a payment built directly so any lifecycle state can be exercised.</summary>
    /// <param name="builder">The configured payment builder.</param>
    /// <returns>The persisted payment.</returns>
    private async Task<Payment> SeedPaymentAsync(PaymentBuilder builder)
    {
        Payment payment = builder.Build();
        _scope.Context.Payments.Add(payment);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        return payment;
    }

    /// <summary>Persists an open-item projection row built directly.</summary>
    /// <param name="builder">The configured open-item builder.</param>
    /// <returns>The persisted open item.</returns>
    private async Task<InvoiceOpenItem> SeedOpenItemAsync(InvoiceOpenItemBuilder builder)
    {
        InvoiceOpenItem openItem = builder.Build();
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

    /// <summary>Asserts the allocation failed with the expected code and persisted no allocation row.</summary>
    /// <param name="result">The allocation result.</param>
    /// <param name="expectedErrorCode">The expected error code.</param>
    /// <returns>A task completing when the assertions have run.</returns>
    private async Task AssertFailedAndWroteNothingAsync(
        Result<AllocatePaymentResultDto> result,
        string expectedErrorCode)
    {
        int allocationCount = await _scope.Context.PaymentAllocations.CountAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(expectedErrorCode));
            Assert.That(allocationCount, Is.Zero);
        });
    }
}
