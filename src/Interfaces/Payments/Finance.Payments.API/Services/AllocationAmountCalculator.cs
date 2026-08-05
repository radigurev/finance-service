using Finance.Country.Abstractions;
using Finance.Payments.DBModel.Models;

namespace Finance.Payments.API.Services;

/// <summary>
/// Computes the per-row allocation amounts via <see cref="ICountryStrategy"/> (SDD-PAY-002 §2.1, §2.9). All
/// arithmetic is <c>decimal</c> and the rounding mode is delegated to
/// <see cref="ICountryStrategy.ApplyTaxRounding"/> so the country-agnostic core never inlines one — mirroring
/// <see cref="PaymentAmountCalculator"/>. Pure and side-effect-free.
/// <para>The realized-FX inputs are the two AGGREGATES' stored, frozen rates — the payment's own rate and the
/// invoice's booking rate mirrored onto the local projection — never a journal-entry line rate: allocation posts
/// nothing, and the ledger holds no rate-converted base amounts to reconcile against.</para>
/// </summary>
public sealed class AllocationAmountCalculator
{
    private readonly ICountryStrategy _countryStrategy;

    /// <summary>Creates a new <see cref="AllocationAmountCalculator"/>.</summary>
    /// <param name="countryStrategy">The country strategy owning monetary rounding (SDD-CTRY-001).</param>
    public AllocationAmountCalculator(ICountryStrategy countryStrategy)
    {
        ArgumentNullException.ThrowIfNull(countryStrategy);
        _countryStrategy = countryStrategy;
    }

    /// <summary>
    /// Computes the base allocated amount and the signed realized-FX difference for one allocation row.
    /// </summary>
    /// <param name="payment">The paying aggregate, supplying its own frozen exchange rate.</param>
    /// <param name="openItem">The matched open item, supplying the invoice's frozen booking rate.</param>
    /// <param name="allocatedAmount">The transactional amount being applied.</param>
    /// <returns>The two rounded figures stored on the allocation row.</returns>
    public AllocationAmounts Compute(Payment payment, InvoiceOpenItem openItem, decimal allocatedAmount)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(openItem);

        decimal rateDifference = payment.ExchangeRate - openItem.BookingExchangeRate;

        return new AllocationAmounts
        {
            BaseAllocatedAmount = _countryStrategy.ApplyTaxRounding(allocatedAmount * payment.ExchangeRate),
            RealizedFxDifference = _countryStrategy.ApplyTaxRounding(allocatedAmount * rateDifference)
        };
    }
}
