namespace Finance.Payments.API.Services;

/// <summary>
/// The two monetary figures a single allocation row derives from the payment's and the invoice's own FROZEN
/// exchange rates (SDD-PAY-002 §2.1, §2.9). Both are <c>decimal</c> and both are rounded through the country
/// strategy — no rounding mode is inlined in core code, and <c>double</c>/<c>float</c> never appear on the
/// allocation path.
/// </summary>
public sealed record AllocationAmounts
{
    /// <summary>
    /// The country-rounded base-currency value of the applied amount at the PAYMENT's frozen rate. A REPORTING
    /// figure only: allocation posts nothing and the ledger stores no rate-converted base amounts, so it is
    /// never reconciled against a journal-entry base amount.
    /// </summary>
    public required decimal BaseAllocatedAmount { get; init; }

    /// <summary>
    /// The SIGNED, country-rounded DOCUMENT-level base-currency difference between the two frozen rates.
    /// Exactly <c>0.00</c> when the rates agree. Informational only until SDD-FIN-005 posts it: it is never
    /// netted into the allocated amount, the base allocated amount, the invoice's settled amount, or the derived
    /// settlement status.
    /// </summary>
    public required decimal RealizedFxDifference { get; init; }
}
