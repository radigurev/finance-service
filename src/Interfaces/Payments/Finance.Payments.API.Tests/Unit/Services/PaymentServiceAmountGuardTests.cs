using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Payments.API.Tests.Builders;
using Finance.Payments.API.Tests.Fixtures;
using Finance.ServiceModel.Payments;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the SERVICE-side amount, FX, and settlement-account assertions of <c>PaymentService</c>
/// (SDD-PAY-001 §2.8, §6.3). These are the guards the service re-asserts on its own — independently of the
/// FluentValidation field rules — so no request can bypass them by reaching the service directly.
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
public sealed class PaymentServiceAmountGuardTests
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
    public async Task Validate_BaseCurrencyPaymentWithRateOtherThanOne_ReturnsInvalidPaymentExchangeRate()
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create()
            .WithCurrencyCode(FakePaymentCountryStrategy.BaseCurrency)
            .WithExchangeRate(1.955830m)
            .Build();

        // Act
        Result<PaymentDto> result = await _harness.Service.CreateDraftAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_PAYMENT_EXCHANGE_RATE));
            Assert.That(_scope.Context.Payments.Count(), Is.Zero);
        });
    }

    [TestCase(0.00)]
    [TestCase(-1.00)]
    public async Task Validate_ZeroOrNegativeAmount_ReturnsInvalidPaymentAmount(decimal amount)
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create().WithAmount(amount).Build();

        // Act
        Result<PaymentDto> result = await _harness.Service.CreateDraftAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_PAYMENT_AMOUNT));
            Assert.That(_scope.Context.Payments.Count(), Is.Zero);
        });
    }

    [Test]
    public async Task SettlementAccount_Missing_ReturnsPaymentSettlementAccountNotFound()
    {
        // Arrange
        _harness.SettlementAccounts.Outcome = FakeSettlementAccountReader.ReaderOutcome.NotFound;
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create()
            .WithSettlementAccountId(9999)
            .Build();

        // Act
        Result<PaymentDto> result = await _harness.Service.CreateDraftAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND));
            Assert.That(_harness.SettlementAccounts.RequestedAccountIds, Is.EqualTo(new[] { 9999 }));
            Assert.That(_scope.Context.Payments.Count(), Is.Zero);
        });
    }

    [Test]
    public async Task ComputeBaseAmount_ForeignCurrencyDraft_IsStoredAlongsideTheTransactionalAmount()
    {
        // Arrange
        CreatePaymentRequest request = CreatePaymentRequestBuilder.Create()
            .WithCurrencyCode("EUR")
            .WithAmount(1000.00m)
            .WithExchangeRate(1.955830m)
            .Build();

        // Act
        Result<PaymentDto> result = await _harness.Service.CreateDraftAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Amount, Is.EqualTo(1000.00m));
            Assert.That(result.Value.ExchangeRate, Is.EqualTo(1.955830m));
            Assert.That(result.Value.BaseAmount, Is.EqualTo(1955.83m));
        });
    }
}
