using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Validation;

/// <summary>
/// Chain rule 5 (SDD-PAY-002 §2.5): the payment's counterparty MUST equal the invoice's, otherwise
/// <c>PAYMENT_ALLOCATION_COUNTERPARTY_MISMATCH</c>. No Warehouse read is performed — both values are opaque
/// GUIDs compared locally, never resolved or joined across services.
/// </summary>
public sealed class AllocationCounterpartyValidator : IChainValidator<PaymentAllocationValidationContext>
{
    /// <inheritdoc />
    public Task<ChainValidationResult> ValidateAsync(
        PaymentAllocationValidationContext request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid paymentCounterparty = request.Payment.CounterpartyId;

        foreach (AllocatePaymentItem item in request.Items)
        {
            if (!request.OpenItems.TryGetValue(item.InvoiceId, out InvoiceOpenItem? openItem))
            {
                continue;
            }

            if (paymentCounterparty != openItem.CounterpartyId)
            {
                return Task.FromResult(ChainValidationResult.Failure(
                    PaymentErrorCodes.PAYMENT_ALLOCATION_COUNTERPARTY_MISMATCH,
                    $"Payment counterparty '{paymentCounterparty}' does not match invoice '{item.InvoiceId}' counterparty '{openItem.CounterpartyId}'."));
            }
        }

        return Task.FromResult(ChainValidationResult.Success());
    }
}
