using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Payments;
using FluentValidation;

namespace Finance.Payments.API.Validators;

/// <summary>
/// FluentValidation SHAPE rules for <see cref="AllocatePaymentRequest"/> (SDD-PAY-002 §3.1): a non-empty item
/// list, a valid base64 row version, and per-item shape delegated to
/// <see cref="AllocatePaymentItemValidator"/>.
/// <para>An EMPTY item list is a validation error, never an implicit "apply the whole payment": v1 requires the
/// explicit invoice list and FIFO / oldest-due-first auto-matching is deferred.</para>
/// </summary>
public sealed class AllocatePaymentRequestValidator : AbstractValidator<AllocatePaymentRequest>
{
    /// <summary>Creates a new <see cref="AllocatePaymentRequestValidator"/>.</summary>
    public AllocatePaymentRequestValidator()
    {
        RuleFor(request => request.Items)
            .NotEmpty()
            .WithErrorCode(PaymentErrorCodes.PAYMENT_ALLOCATION_ITEMS_REQUIRED);

        RuleForEach(request => request.Items)
            .SetValidator(new AllocatePaymentItemValidator());

        RuleFor(request => request.RowVersion)
            .NotEmpty()
            .WithErrorCode(CommonErrorCodes.CONCURRENT_MODIFICATION);

        RuleFor(request => request.RowVersion)
            .Must(RowVersionTokenRule.IsBase64)
            .WithErrorCode(CommonErrorCodes.CONCURRENT_MODIFICATION);
    }
}
