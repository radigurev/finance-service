using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Payments;
using FluentValidation;

namespace Finance.Payments.API.Validators;

/// <summary>
/// FluentValidation shape rule for <see cref="ReversePaymentRequest"/> (SDD-PAY-001 §3.1): a non-empty reason is
/// mandatory because reversal is a SENSITIVE audit operation (SDD-AUDIT-001). The source-state, allocation, and
/// fiscal-period pre-checks run in the service (SDD-PAY-001 §2.7).
/// </summary>
public sealed class ReversePaymentRequestValidator : AbstractValidator<ReversePaymentRequest>
{
    /// <summary>Configures the reverse-request shape rules.</summary>
    public ReversePaymentRequestValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty()
            .WithErrorCode(PaymentErrorCodes.PAYMENT_REVERSE_REASON_REQUIRED);

        RuleFor(request => request.RowVersion)
            .NotEmpty()
            .WithErrorCode(CommonErrorCodes.CONCURRENT_MODIFICATION);
    }
}
