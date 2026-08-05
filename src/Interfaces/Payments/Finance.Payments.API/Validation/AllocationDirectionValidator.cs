using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Validation;

/// <summary>
/// Chain rule 4 (SDD-PAY-002 §2.5): the payment's direction MUST equal the invoice's, otherwise
/// <c>PAYMENT_ALLOCATION_DIRECTION_MISMATCH</c>. The comparison is by enum member NAME (<c>AR</c>/<c>AP</c>):
/// <c>PaymentDirection</c> and <c>InvoiceDirection</c> are distinct CLR enums with identical members, and the
/// projection stores the invoice value as a string.
/// <para>This is a CHEAP PRE-FILTER on CASH direction and is NOT by itself an accounting match: the shipped
/// invoice direction groups a sale invoice with a debit note under <c>AR</c> and a purchase invoice with a
/// credit note under <c>AP</c>, so equal directions do NOT imply the two documents moved the same control
/// account. Rule 10 states the accounting match explicitly; this rule is retained because it is the cheaper,
/// more specific diagnostic for the common operator error and is registered EARLIER so a request that breaks
/// both short-circuits here.</para>
/// </summary>
public sealed class AllocationDirectionValidator : IChainValidator<PaymentAllocationValidationContext>
{
    /// <inheritdoc />
    public Task<ChainValidationResult> ValidateAsync(
        PaymentAllocationValidationContext request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        string paymentDirection = request.Payment.Direction.ToString();

        foreach (AllocatePaymentItem item in request.Items)
        {
            if (!request.OpenItems.TryGetValue(item.InvoiceId, out InvoiceOpenItem? openItem))
            {
                continue;
            }

            if (!string.Equals(paymentDirection, openItem.Direction, StringComparison.Ordinal))
            {
                return Task.FromResult(ChainValidationResult.Failure(
                    PaymentErrorCodes.PAYMENT_ALLOCATION_DIRECTION_MISMATCH,
                    $"Payment direction '{paymentDirection}' does not match invoice '{item.InvoiceId}' direction '{openItem.Direction}'."));
            }
        }

        return Task.FromResult(ChainValidationResult.Success());
    }
}
