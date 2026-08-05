using System.Reflection;
using Finance.Payments.API.Services;
using Finance.Payments.API.Tests.Builders;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel.Models;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="PaymentAmountCalculator"/> (SDD-PAY-001 §2.8, §6.3): the base amount is the
/// country-rounded <c>Amount × ExchangeRate</c>, the rounding mode is DELEGATED to
/// <c>ICountryStrategy.ApplyTaxRounding</c> rather than inlined, and every step is <c>decimal</c> — a
/// <c>double</c>/<c>float</c> code path would produce a different cent.
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
public sealed class PaymentAmountCalculatorTests
{
    private FakePaymentCountryStrategy _country = null!;
    private PaymentAmountCalculator _sut = null!;

    /// <summary>Creates a fresh calculator over a fresh fake country strategy before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _country = new FakePaymentCountryStrategy();
        _sut = new PaymentAmountCalculator(_country);
    }

    [Test]
    public void ComputeBaseAmount_UsesCountryStrategyRounding_OnAmountTimesRate()
    {
        // Arrange
        decimal amount = 1000.00m;
        decimal exchangeRate = 1.955830m;

        // Act
        decimal baseAmount = _sut.ComputeBaseAmount(amount, exchangeRate);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(baseAmount, Is.EqualTo(1955.83m));
            Assert.That(
                _country.ApplyTaxRoundingCallCount,
                Is.EqualTo(1),
                "the rounding mode must be delegated to the country strategy, never inlined");
        });
    }

    [Test]
    public void ComputeBaseAmount_UsesDecimalArithmetic_NoFloatingPoint()
    {
        // Arrange
        MethodInfo compute = typeof(PaymentAmountCalculator)
            .GetMethod(nameof(PaymentAmountCalculator.ComputeBaseAmount))!;
        IReadOnlyList<Type> monetaryTypes =
        [
            .. typeof(Payment)
                .GetProperties()
                .Where(property => property.PropertyType == typeof(double)
                    || property.PropertyType == typeof(float))
                .Select(property => property.PropertyType)
        ];

        // Act
        decimal baseAmount = _sut.ComputeBaseAmount(100.00m, 1.234567m);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(baseAmount, Is.EqualTo(123.46m), "the six-decimal rate product rounds to the cent");
            Assert.That(baseAmount.GetType(), Is.EqualTo(typeof(decimal)));
            Assert.That(compute.ReturnType, Is.EqualTo(typeof(decimal)));
            Assert.That(
                compute.GetParameters().Select(parameter => parameter.ParameterType),
                Is.All.EqualTo(typeof(decimal)));
            Assert.That(
                monetaryTypes,
                Is.Empty,
                "double/float MUST NEVER appear on a payment code path (SDD-PAY-001 §2.8)");
        });
    }

    [Test]
    public void Recompute_DiscardsClientSuppliedBaseAmount_AndStampsTheRecomputedValue()
    {
        // Arrange
        Payment payment = PaymentBuilder.Create()
            .WithAmount(500.00m)
            .WithExchangeRate(1.955830m)
            .Build();
        payment.BaseAmount = 1m;

        // Act
        _sut.Recompute(payment);

        // Assert
        Assert.That(payment.BaseAmount, Is.EqualTo(977.92m));
    }

    [Test]
    public void Reconciles_StoredBaseAmountMatchingRecomputedValue_ReturnsTrue()
    {
        // Arrange
        Payment payment = PaymentBuilder.Create()
            .WithAmount(250.00m)
            .WithExchangeRate(1.000000m)
            .Build();
        _sut.Recompute(payment);

        // Act
        bool reconciles = _sut.Reconciles(payment);

        // Assert
        Assert.That(reconciles, Is.True);
    }

    [Test]
    public void Reconciles_StoredBaseAmountOneCentOff_ReturnsFalse()
    {
        // Arrange
        Payment payment = PaymentBuilder.Create()
            .WithAmount(250.00m)
            .WithExchangeRate(1.000000m)
            .Build();
        _sut.Recompute(payment);
        payment.BaseAmount += 0.01m;

        // Act
        bool reconciles = _sut.Reconciles(payment);

        // Assert
        Assert.That(reconciles, Is.False);
    }

    [Test]
    public void UnallocatedAmount_IsComputedFromAmountMinusAllocatedAmount_NotStored()
    {
        // Arrange
        Payment payment = PaymentBuilder.Create()
            .WithAmount(1000.00m)
            .WithAllocatedAmount(400.00m)
            .Build();

        // Act
        decimal unallocated = payment.UnallocatedAmount;
        payment.AllocatedAmount = 1000.00m;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(unallocated, Is.EqualTo(600.00m));
            Assert.That(payment.UnallocatedAmount, Is.EqualTo(0.00m), "it recomputes, so it cannot be stored");
        });
    }
}
