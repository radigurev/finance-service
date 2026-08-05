using Finance.Common.Enums;

namespace Finance.Payments.API.Interfaces;

/// <summary>
/// The input handed to <see cref="IRealizedFxHandler"/> once per allocation row, inside the allocation
/// transaction (SDD-PAY-002 §2.4 step 5b, §2.9). It carries the two DOCUMENT-level frozen rates the difference
/// is computed from — the payment's own <see cref="PaymentExchangeRate"/> and the invoice's mirrored
/// <see cref="BookingExchangeRate"/> — never a journal-entry line rate: allocation posts nothing and the
/// ledger holds no rate-converted base amounts to reconcile against.
/// <para>The gain-versus-loss INTERPRETATION per <see cref="Direction"/> is owned by the eventual SDD-FIN-005
/// posting rule and is deliberately NOT baked into <see cref="RealizedFxDifference"/>, which is stored signed.</para>
/// </summary>
public sealed record RealizedFxContext
{
    /// <summary>The payment whose matching produced the difference.</summary>
    public required Guid PaymentId { get; init; }

    /// <summary>The invoice the amount was matched against.</summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>The payment's ledger direction (<c>AP</c>/<c>AR</c>).</summary>
    public required PaymentDirection Direction { get; init; }

    /// <summary>The transactional currency, identical on both documents (v1 requires currency equality).</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The base currency the difference is expressed in.</summary>
    public required string BaseCurrencyCode { get; init; }

    /// <summary>The transactional amount applied by this allocation row.</summary>
    public required decimal AllocatedAmount { get; init; }

    /// <summary>The rate the PAYMENT froze at creation.</summary>
    public required decimal PaymentExchangeRate { get; init; }

    /// <summary>The rate the INVOICE froze at creation, mirrored onto the local projection.</summary>
    public required decimal BookingExchangeRate { get; init; }

    /// <summary>
    /// The signed, country-rounded base-currency difference
    /// (<c>AllocatedAmount × (PaymentExchangeRate − BookingExchangeRate)</c>). Exactly <c>0.00</c> whenever the
    /// two rates agree, which is the common path.
    /// </summary>
    public required decimal RealizedFxDifference { get; init; }

    /// <summary>The ambient correlation identifier of the allocating request.</summary>
    public required string CorrelationId { get; init; }
}
