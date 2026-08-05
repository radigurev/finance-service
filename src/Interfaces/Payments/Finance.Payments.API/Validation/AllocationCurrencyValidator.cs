using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Validation;

/// <summary>
/// Chain rule 6 (SDD-PAY-002 §2.5): the payment's transactional currency MUST equal the invoice's, otherwise
/// <c>PAYMENT_ALLOCATION_CURRENCY_MISMATCH</c>.
/// <para>v1 REQUIRES equality even when the two documents' own base amounts would reconcile — cross-currency
/// allocation is DEFERRED to SDD-FIN-005, and relaxing this invariant is a BREAKING change. Because currencies
/// must match, no transactional conversion ever happens while matching; the only difference that can arise is
/// the DOCUMENT-level base-currency one the realized-FX seam handles (§2.9).</para>
/// </summary>
public sealed class AllocationCurrencyValidator : IChainValidator<PaymentAllocationValidationContext>
{
    /// <inheritdoc />
    public Task<ChainValidationResult> ValidateAsync(
        PaymentAllocationValidationContext request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        string paymentCurrency = request.Payment.CurrencyCode;

        foreach (AllocatePaymentItem item in request.Items)
        {
            if (!request.OpenItems.TryGetValue(item.InvoiceId, out InvoiceOpenItem? openItem))
            {
                continue;
            }

            if (!string.Equals(paymentCurrency, openItem.CurrencyCode, StringComparison.Ordinal))
            {
                return Task.FromResult(ChainValidationResult.Failure(
                    PaymentErrorCodes.PAYMENT_ALLOCATION_CURRENCY_MISMATCH,
                    $"Payment currency '{paymentCurrency}' does not match invoice '{item.InvoiceId}' currency '{openItem.CurrencyCode}'; cross-currency allocation is not supported in v1."));
            }
        }

        return Task.FromResult(ChainValidationResult.Success());
    }
}
