using Finance.Country.Abstractions;
using Finance.Payments.DBModel.Models;

namespace Finance.Payments.API.Services;

/// <summary>
/// Computes the base-currency value of a payment via <see cref="ICountryStrategy"/> (SDD-PAY-001 §2.8;
/// SDD-FIN-005). All arithmetic is <c>decimal</c> — <c>double</c>/<c>float</c> MUST NEVER appear on a payment
/// code path — and the rounding mode is delegated to
/// <see cref="ICountryStrategy.ApplyTaxRounding"/> so the core never inlines one. Pure and side-effect-free
/// apart from stamping <see cref="Payment.BaseAmount"/> in <see cref="Recompute"/>.
/// </summary>
public sealed class PaymentAmountCalculator
{
    private readonly ICountryStrategy _countryStrategy;

    /// <summary>Creates a new <see cref="PaymentAmountCalculator"/>.</summary>
    /// <param name="countryStrategy">The country strategy owning monetary rounding (SDD-CTRY-001).</param>
    public PaymentAmountCalculator(ICountryStrategy countryStrategy)
    {
        ArgumentNullException.ThrowIfNull(countryStrategy);
        _countryStrategy = countryStrategy;
    }

    /// <summary>
    /// Computes the country-rounded base amount for a transactional amount and rate.
    /// </summary>
    /// <param name="amount">The transactional cash amount.</param>
    /// <param name="exchangeRate">The rate at the payment date.</param>
    /// <returns>The rounded <c>amount × exchangeRate</c> in the base currency.</returns>
    public decimal ComputeBaseAmount(decimal amount, decimal exchangeRate)
    {
        return _countryStrategy.ApplyTaxRounding(amount * exchangeRate);
    }

    /// <summary>
    /// Recomputes and stamps <see cref="Payment.BaseAmount"/> from the payment's own
    /// <see cref="Payment.Amount"/> and <see cref="Payment.ExchangeRate"/>. A client-supplied base amount is
    /// always discarded by this call.
    /// </summary>
    /// <param name="payment">The payment whose base amount is (re)computed.</param>
    public void Recompute(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        payment.BaseAmount = ComputeBaseAmount(payment.Amount, payment.ExchangeRate);
    }

    /// <summary>
    /// Determines whether the payment's stored base amount reconciles to the recomputed value to the cent. The
    /// service always recomputes first, so a mismatch is unreachable through the v1 paths — the check is
    /// retained as defense-in-depth against a future path that trusts a client value (SDD-PAY-001 §3.2).
    /// </summary>
    /// <param name="payment">The payment to reconcile.</param>
    /// <returns><c>true</c> when the stored base amount matches the recomputed value.</returns>
    public bool Reconciles(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return payment.BaseAmount == ComputeBaseAmount(payment.Amount, payment.ExchangeRate);
    }
}
