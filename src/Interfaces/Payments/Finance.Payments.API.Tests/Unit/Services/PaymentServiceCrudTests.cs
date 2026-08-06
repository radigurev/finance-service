using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Audit.Models;
using Finance.Payments.API.Auditing;
using Finance.Payments.API.Tests.Builders;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the draft CRUD, list, and get surface of <c>PaymentService</c> (SDD-PAY-001 §6.4): the derived and
/// frozen direction, the frozen base currency, the zero allocated amount, the audit rows, the immutable document
/// type, optimistic concurrency, the default list ordering, and the no-caching recompute.
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
public sealed class PaymentServiceCrudTests
{
    private SqlitePaymentsDbContextScope _scope = null!;
    private PaymentServiceTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqlitePaymentsDbContextFactory.Create();
        _harness = PaymentServiceTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    [Test]
    public async Task CreateDraft_UnsupportedDocumentType_ReturnsInvalidPaymentDocumentType_WithoutThrowing()
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create()
            .WithDocumentType((PaymentDocumentType)99).Build();

        // Act
        Result<PaymentDto> result = await _harness.Service.CreateDraftAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_PAYMENT_DOCUMENT_TYPE));
        });
    }

    [Test]
    public async Task CreateDraft_UnsupportedDocumentType_PersistsNothing()
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create()
            .WithDocumentType((PaymentDocumentType)99).Build();

        // Act
        await _harness.Service.CreateDraftAsync(request, CancellationToken.None);

        // Assert
        Assert.That(await _scope.Context.Payments.CountAsync(CancellationToken.None), Is.Zero);
    }

    [Test]
    public void IsSupported_OnlyTheTwoDocumentTypesSddPay001Defines_AreSupported()
    {
        // Arrange
        PaymentDocumentType outOfRange = (PaymentDocumentType)99;

        // Act
        bool receipt = Finance.Payments.API.Services.PaymentDocumentTypeMap
            .IsSupported(PaymentDocumentType.CustomerReceipt);
        bool supplierPayment = Finance.Payments.API.Services.PaymentDocumentTypeMap
            .IsSupported(PaymentDocumentType.SupplierPayment);
        bool unsupported = Finance.Payments.API.Services.PaymentDocumentTypeMap.IsSupported(outOfRange);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(receipt, Is.True);
            Assert.That(supplierPayment, Is.True);
            Assert.That(unsupported, Is.False);
        });
    }

    [Test]
    public async Task CreateDraft_ValidRequest_PersistsInDraft_WithNullDocumentNumber()
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create().Build();

        // Act
        Result<PaymentDto> result = await _harness.Service.CreateDraftAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Status, Is.EqualTo(PaymentStatus.Draft));
            Assert.That(result.Value.DocumentNumber, Is.Null);
            Assert.That(result.Value.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(result.Value.JournalEntryId, Is.Null);
            Assert.That(result.Value.ConfirmedAt, Is.Null);
            Assert.That(result.Value.PostedAt, Is.Null);
            Assert.That(result.Value.ReversedAt, Is.Null);
            Assert.That(result.Value.CancellationReason, Is.Null);
            Assert.That(_harness.TotalSequenceValuesConsumed(), Is.Zero);
        });
    }

    [Test]
    public async Task CreateDraft_DerivesDirectionFromDocumentType_AndIgnoresClientDirection()
    {
        // Arrange
        CreatePaymentRequest receipt = CreatePaymentRequestBuilder.Create()
            .WithDocumentType(PaymentDocumentType.CustomerReceipt).Build();
        CreatePaymentRequest supplierPayment = CreatePaymentRequestBuilder.Create()
            .WithDocumentType(PaymentDocumentType.SupplierPayment).Build();

        // Act
        Result<PaymentDto> receiptResult =
            await _harness.Service.CreateDraftAsync(receipt, CancellationToken.None);
        Result<PaymentDto> supplierResult =
            await _harness.Service.CreateDraftAsync(supplierPayment, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(receiptResult.Value!.Direction, Is.EqualTo(PaymentDirection.AR));
            Assert.That(supplierResult.Value!.Direction, Is.EqualTo(PaymentDirection.AP));
        });
    }

    [Test]
    public async Task CreateDraft_SetsBaseCurrencyFromCountryStrategy()
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create()
            .WithCurrencyCode("EUR")
            .WithExchangeRate(1.955830m)
            .Build();

        // Act
        Result<PaymentDto> result = await _harness.Service.CreateDraftAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.BaseCurrencyCode, Is.EqualTo(FakePaymentCountryStrategy.BaseCurrency));
            Assert.That(result.Value.CurrencyCode, Is.EqualTo("EUR"));
            Assert.That(result.Value.BaseAmount, Is.EqualTo(1955.83m));
        });
    }

    [Test]
    public async Task CreateDraft_InitializesAllocatedAmountToZero()
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create().WithAmount(750.00m).Build();

        // Act
        Result<PaymentDto> result = await _harness.Service.CreateDraftAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.AllocatedAmount, Is.EqualTo(0.00m));
            Assert.That(result.Value.UnallocatedAmount, Is.EqualTo(750.00m));
        });
    }

    [Test]
    public async Task CreateDraft_RecordsAuditCreate_WithNullBeforeJson()
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create().Build();

        // Act
        Result<PaymentDto> result = await _harness.Service.CreateDraftAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        AuditEntry recorded = _harness.RecordedAudits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(recorded.Operation, Is.EqualTo(AuditOperation.Create));
            Assert.That(recorded.EventType, Is.EqualTo(PaymentAuditEventTypes.PaymentCreated));
            Assert.That(recorded.BeforeJson, Is.Null);
            Assert.That(recorded.AfterJson, Is.Not.Empty);
            Assert.That(_harness.PublishedEvents, Is.Empty, "draft creation publishes no domain event");
        });
    }

    [Test]
    public async Task Update_ChangingDocumentType_ReturnsInvalidPaymentDocumentType()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();
        UpdatePaymentRequest request = UpdateRequestFor(draft) with
        {
            DocumentType = PaymentDocumentType.SupplierPayment
        };

        // Act
        Result<PaymentDto> result =
            await _harness.Service.UpdateDraftAsync(draft.Id, request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_PAYMENT_DOCUMENT_TYPE));
        });
    }

    [Test]
    public async Task Update_StaleRowVersion_ReturnsConcurrentModification()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();
        UpdatePaymentRequest request = UpdateRequestFor(draft) with
        {
            RowVersion = Convert.ToBase64String(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 })
        };

        // Act
        Result<PaymentDto> result =
            await _harness.Service.UpdateDraftAsync(draft.Id, request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(CommonErrorCodes.CONCURRENT_MODIFICATION));
        });
    }

    [TestCase("not-base-64!")]
    [TestCase("QUJD=====")]
    public async Task Update_MalformedBase64RowVersion_ReturnsConcurrentModification(string rowVersion)
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();
        _harness.Timeline.Clear();
        UpdatePaymentRequest request = UpdateRequestFor(draft) with
        {
            Amount = 4321.00m,
            RowVersion = rowVersion
        };

        // Act
        Result<PaymentDto> result =
            await _harness.Service.UpdateDraftAsync(draft.Id, request, CancellationToken.None);

        // Assert
        Payment stored = await _scope.Context.Payments
            .AsNoTracking()
            .SingleAsync(payment => payment.Id == draft.Id, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(CommonErrorCodes.CONCURRENT_MODIFICATION));
            Assert.That(stored.Amount, Is.EqualTo(draft.Amount), "a malformed token writes nothing");
            Assert.That(_harness.RecordedAudits, Is.Empty);
        });
    }

    [Test]
    public async Task Update_DraftPayment_RecordsAuditUpdate_WithNonEmptyBeforeJson()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();
        _harness.Timeline.Clear();
        UpdatePaymentRequest request = UpdateRequestFor(draft) with { Amount = 1500.00m };

        // Act
        Result<PaymentDto> result =
            await _harness.Service.UpdateDraftAsync(draft.Id, request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        AuditEntry recorded = _harness.RecordedAudits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(recorded.Operation, Is.EqualTo(AuditOperation.Update));
            Assert.That(recorded.EventType, Is.EqualTo(PaymentAuditEventTypes.PaymentUpdated));
            Assert.That(recorded.BeforeJson, Is.Not.Null.And.Not.Empty);
            Assert.That(recorded.BeforeJson, Does.Contain("\"Amount\":1000.00"));
            Assert.That(recorded.AfterJson, Does.Contain("\"Amount\":1500.00"));
            Assert.That(result.Value!.BaseAmount, Is.EqualTo(1500.00m), "the base amount is recomputed");
        });
    }

    [Test]
    public async Task Get_ReturnsPaymentNotFound_WhenPaymentDoesNotExist()
    {
        // Arrange
        Guid unknownId = Guid.NewGuid();

        // Act
        Result<PaymentDto> result = await _harness.Service.GetAsync(unknownId, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_NOT_FOUND));
        });
    }

    [Test]
    public async Task Search_ReturnsPagedResultOrderedByPaymentDateDescendingThenId()
    {
        // Arrange
        await CreateDraftAsync(new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero));
        await CreateDraftAsync(new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero));
        await CreateDraftAsync(new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero));
        FilterRequest request = new() { Page = 1, PageSize = 50 };

        // Act
        Result<PagedResult<PaymentDto>> result =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        IReadOnlyList<PaymentDto> items = result.Value!.Items;
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items[0].PaymentDate.Month, Is.EqualTo(3));
            Assert.That(items[1].PaymentDate.Month, Is.EqualTo(2));
            Assert.That(items[2].PaymentDate.Month, Is.EqualTo(1));
            Assert.That(result.Value.TotalCount, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Search_IncludesConfirmedButUnlinkedPayments()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();
        Result<PaymentDto> confirmed = await _harness.Service.ConfirmAsync(
            draft.Id,
            new ConfirmPaymentRequest { RowVersion = draft.RowVersion },
            CancellationToken.None);
        Assert.That(confirmed.IsSuccess, Is.True, confirmed.ErrorCode);

        // Act
        Result<PagedResult<PaymentDto>> result = await _harness.Service.SearchAsync(
            new FilterRequest { Page = 1, PageSize = 50 },
            CancellationToken.None);

        // Assert
        PaymentDto listed = result.Value!.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(listed.Status, Is.EqualTo(PaymentStatus.Confirmed));
            Assert.That(listed.JournalEntryId, Is.Null, "the pending post is observable, not hidden");
        });
    }

    [Test]
    public async Task Search_DoesNotCacheTransactionalData_RecomputesFromDatabase()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();
        Result<PagedResult<PaymentDto>> first = await _harness.Service.SearchAsync(
            new FilterRequest { Page = 1, PageSize = 50 },
            CancellationToken.None);
        Assert.That(first.Value!.Items.Single().Amount, Is.EqualTo(1000.00m));

        // Act
        await _scope.Context.Payments
            .Where(payment => payment.Id == draft.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(payment => payment.Amount, 4321.00m),
                CancellationToken.None);
        Result<PagedResult<PaymentDto>> second = await _harness.Service.SearchAsync(
            new FilterRequest { Page = 1, PageSize = 50 },
            CancellationToken.None);

        // Assert
        Assert.That(second.Value!.Items.Single().Amount, Is.EqualTo(4321.00m));
    }

    [Test]
    public void PaymentDto_DoesNotExposeCreatedByConfirmedByOrCorrelationId()
    {
        // Arrange
        IReadOnlyList<string> exposed =
            [.. typeof(PaymentDto).GetProperties().Select(property => property.Name)];

        // Act
        IReadOnlyList<string> forbidden =
            [.. new[] { "CreatedBy", "ConfirmedBy", "CorrelationId" }.Where(exposed.Contains)];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(forbidden, Is.Empty);
            Assert.That(exposed, Does.Contain(nameof(PaymentDto.RowVersion)));
            Assert.That(exposed, Does.Contain(nameof(PaymentDto.UnallocatedAmount)));
        });
    }

    [Test]
    public async Task Search_PageSizeAboveCap_ReturnsPageSizeTooLarge()
    {
        // Arrange
        await CreateDraftAsync();

        // Act
        Result<PagedResult<PaymentDto>> result = await _harness.Service.SearchAsync(
            new FilterRequest { Page = 1, PageSize = 201 },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(FilterErrorCodes.PAGE_SIZE_TOO_LARGE));
        });
    }

    /// <summary>Creates a valid draft payment through the production service path.</summary>
    /// <returns>The created payment DTO.</returns>
    private Task<PaymentDto> CreateDraftAsync() =>
        CreateDraftAsync(CreatePaymentRequestBuilder.DefaultPaymentDate);

    /// <summary>Creates a valid draft payment dated on the supplied day.</summary>
    /// <param name="paymentDate">The payment date to record.</param>
    /// <returns>The created payment DTO.</returns>
    private async Task<PaymentDto> CreateDraftAsync(DateTimeOffset paymentDate)
    {
        Result<PaymentDto> created = await _harness.Service.CreateDraftAsync(
            CreatePaymentRequestBuilder.Create().WithPaymentDate(paymentDate).Build(),
            CancellationToken.None);

        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        return created.Value!;
    }

    /// <summary>Builds a same-shape update request from a payment DTO.</summary>
    /// <param name="payment">The payment to echo.</param>
    /// <returns>The update request.</returns>
    private static UpdatePaymentRequest UpdateRequestFor(PaymentDto payment) => new()
    {
        DocumentType = payment.DocumentType,
        Method = payment.Method,
        CounterpartyId = payment.CounterpartyId,
        CurrencyCode = payment.CurrencyCode,
        Amount = payment.Amount,
        ExchangeRate = payment.ExchangeRate,
        SettlementAccountId = payment.SettlementAccountId,
        PaymentDate = payment.PaymentDate,
        BankReference = payment.BankReference,
        RowVersion = payment.RowVersion
    };
}
