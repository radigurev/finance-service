using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Settlement;
using Finance.Common.Validation;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Validation;

/// <summary>
/// Chain rule 10 (SDD-PAY-002 §2.5): the pair <c>(Payment.DocumentType, InvoiceOpenItem.DocumentType)</c> MUST
/// be a documented <see cref="SettlementPairing"/> pair, otherwise
/// <c>PAYMENT_ALLOCATION_CONTROL_ACCOUNT_MISMATCH</c>. This is the rule that makes a match an ACCOUNTING match:
/// the payment's posting rule is chosen solely from its own document type and is fixed in the GL at confirm, so
/// unless the invoice's document type moved the SAME control account, allocating drifts both control accounts
/// from their sub-ledgers by the allocated amount.
/// <para><b>UNREACHABLE through the v1 paths — retained as defense-in-depth.</b> Once §2.3 keeps every
/// non-settleable document type out of the projection, the only types an eligible open item can carry are a
/// sale invoice and a debit note (both <c>AR</c>) and a purchase invoice (<c>AP</c>), so every pair this rule
/// would reject also breaks the direction rule and short-circuits six rules earlier; the one same-direction
/// mismatch the direction rule cannot see — a supplier payment against a customer credit note — can no longer
/// be REQUESTED, because the credit note has no open item and rule 2 rejects it eight rules earlier. The rule
/// becomes REACHABLE the moment the pairing table is widened, and it is the only rule that states the
/// accounting invariant explicitly.</para>
/// <para>An unparseable projected document type (in practice only a cancellation tombstone, which rule 3
/// already rejects) is treated as a mismatch rather than faulting the request.</para>
/// </summary>
public sealed class AllocationControlAccountValidator : IChainValidator<PaymentAllocationValidationContext>
{
    /// <inheritdoc />
    public Task<ChainValidationResult> ValidateAsync(
        PaymentAllocationValidationContext request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        PaymentDocumentType paymentDocumentType = request.Payment.DocumentType;

        foreach (AllocatePaymentItem item in request.Items)
        {
            if (!request.OpenItems.TryGetValue(item.InvoiceId, out InvoiceOpenItem? openItem))
            {
                continue;
            }

            if (!IsDocumentedPair(paymentDocumentType, openItem.DocumentType))
            {
                return Task.FromResult(ChainValidationResult.Failure(
                    PaymentErrorCodes.PAYMENT_ALLOCATION_CONTROL_ACCOUNT_MISMATCH,
                    $"A '{paymentDocumentType}' cannot settle a '{openItem.DocumentType}': the two documents moved different control accounts."));
            }
        }

        return Task.FromResult(ChainValidationResult.Success());
    }

    private static bool IsDocumentedPair(PaymentDocumentType paymentDocumentType, string invoiceDocumentType)
    {
        if (!Enum.TryParse(invoiceDocumentType, ignoreCase: false, out InvoiceDocumentType parsed))
        {
            return false;
        }

        return SettlementPairing.CanSettle(paymentDocumentType, parsed);
    }
}
