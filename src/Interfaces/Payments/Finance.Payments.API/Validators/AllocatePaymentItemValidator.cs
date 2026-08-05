using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Payments;
using FluentValidation;

namespace Finance.Payments.API.Validators;

/// <summary>
/// FluentValidation SHAPE rules for a single allocation item (SDD-PAY-002 §3.1): a present invoice reference
/// and a strictly positive amount carrying at most two decimal places. Every stateful or cross-aggregate rule —
/// invoice existence, eligibility, direction, counterparty, currency, duplication, the two over-allocation
/// bounds, and the control-account pairing — runs in the SDD-INFRA-007 chain, never here.
/// </summary>
public sealed class AllocatePaymentItemValidator : AbstractValidator<AllocatePaymentItem>
{
    private const int MonetaryScale = 2;

    /// <summary>Creates a new <see cref="AllocatePaymentItemValidator"/>.</summary>
    public AllocatePaymentItemValidator()
    {
        RuleFor(item => item.InvoiceId)
            .NotEqual(Guid.Empty)
            .WithErrorCode(PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_REQUIRED);

        RuleFor(item => item.AllocatedAmount)
            .GreaterThan(0m)
            .WithErrorCode(PaymentErrorCodes.INVALID_PAYMENT_ALLOCATION_AMOUNT);

        RuleFor(item => item.AllocatedAmount)
            .Must(HasMonetaryScale)
            .WithErrorCode(PaymentErrorCodes.INVALID_PAYMENT_ALLOCATION_AMOUNT);
    }

    /// <summary>
    /// Determines whether the amount carries at most two decimal places, so an allocation can never be a
    /// fraction of a cent (SDD-PAY-002 §2.1; every bound is compared as an exact <c>DECIMAL(18,2)</c> value).
    /// </summary>
    /// <param name="amount">The requested allocation amount.</param>
    /// <returns><c>true</c> when the amount has at most two decimal places; otherwise <c>false</c>.</returns>
    private static bool HasMonetaryScale(decimal amount)
    {
        return decimal.Round(amount, MonetaryScale) == amount;
    }
}
