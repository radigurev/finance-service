using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Payments;
using FluentValidation;

namespace Finance.Payments.API.Validators;

/// <summary>
/// FluentValidation shape rule for <see cref="CancelPaymentRequest"/> (SDD-PAY-001 §3.1): a non-empty reason is
/// mandatory because cancellation is a SENSITIVE audit operation — it voids an operator-entered cash document
/// and keeps the row for audit instead of deleting it (SDD-AUDIT-001).
/// </summary>
public sealed class CancelPaymentRequestValidator : AbstractValidator<CancelPaymentRequest>
{
    /// <summary>Configures the cancel-request shape rules.</summary>
    public CancelPaymentRequestValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty()
            .WithErrorCode(PaymentErrorCodes.PAYMENT_CANCEL_REASON_REQUIRED);

        RuleFor(request => request.RowVersion)
            .NotEmpty()
            .WithErrorCode(CommonErrorCodes.CONCURRENT_MODIFICATION);
    }
}
