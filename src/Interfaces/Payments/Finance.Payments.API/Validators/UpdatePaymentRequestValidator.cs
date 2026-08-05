using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Payments;
using FluentValidation;

namespace Finance.Payments.API.Validators;

/// <summary>
/// FluentValidation shape rules for <see cref="UpdatePaymentRequest"/> (SDD-PAY-001 §3.1). Identical to the
/// create surface plus the base64 row-version token. The immutability check (only a <c>Draft</c> is editable)
/// and the "document type unchanged" assertion run in the service (SDD-PAY-001 §2.6).
/// </summary>
public sealed class UpdatePaymentRequestValidator : AbstractValidator<UpdatePaymentRequest>
{
    /// <summary>Creates a new <see cref="UpdatePaymentRequestValidator"/>.</summary>
    /// <param name="timeProvider">The clock supplying the "not in the future" upper bound.</param>
    public UpdatePaymentRequestValidator(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        RuleFor(request => request.DocumentType)
            .IsInEnum()
            .WithErrorCode(PaymentErrorCodes.INVALID_PAYMENT_DOCUMENT_TYPE);

        RuleFor(request => request.Method)
            .IsInEnum()
            .WithErrorCode(PaymentErrorCodes.INVALID_PAYMENT_METHOD);

        RuleFor(request => request.CounterpartyId)
            .NotEqual(Guid.Empty)
            .WithErrorCode(PaymentErrorCodes.PAYMENT_COUNTERPARTY_REQUIRED);

        RuleFor(request => request.CurrencyCode)
            .Matches("^[A-Z]{3}$")
            .WithErrorCode(PaymentErrorCodes.INVALID_PAYMENT_CURRENCY);

        RuleFor(request => request.Amount)
            .GreaterThan(0m)
            .WithErrorCode(PaymentErrorCodes.INVALID_PAYMENT_AMOUNT);

        RuleFor(request => request.ExchangeRate)
            .GreaterThan(0m)
            .WithErrorCode(PaymentErrorCodes.INVALID_PAYMENT_EXCHANGE_RATE);

        RuleFor(request => request.PaymentDate)
            .Must(date => PaymentDateRule.IsWithinBounds(date, timeProvider))
            .WithErrorCode(PaymentErrorCodes.INVALID_PAYMENT_DATE);

        RuleFor(request => request.SettlementAccountId)
            .GreaterThan(0)
            .WithErrorCode(PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_REQUIRED);

        RuleFor(request => request.BankReference)
            .MaximumLength(64)
            .WithErrorCode(PaymentErrorCodes.INVALID_PAYMENT_BANK_REFERENCE);

        RuleFor(request => request.RowVersion)
            .NotEmpty()
            .WithErrorCode(CommonErrorCodes.CONCURRENT_MODIFICATION);
    }
}
