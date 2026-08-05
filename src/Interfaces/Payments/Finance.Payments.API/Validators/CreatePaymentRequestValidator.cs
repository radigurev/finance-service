using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Payments;
using FluentValidation;

namespace Finance.Payments.API.Validators;

/// <summary>
/// FluentValidation shape rules for <see cref="CreatePaymentRequest"/> (SDD-PAY-001 §3.1): a valid document
/// type and method, a present counterparty, an ISO-4217 currency, a strictly positive amount and rate, a
/// payment date that is present and not in the future, a positive settlement account, and a bounded bank
/// reference. The cross-field exchange-rate and base-amount rules, the settlement-account existence/activeness
/// guard, and the period and confirm-year guards all run in the service (SDD-PAY-001 §2.2, §2.8).
/// </summary>
public sealed class CreatePaymentRequestValidator : AbstractValidator<CreatePaymentRequest>
{
    /// <summary>Creates a new <see cref="CreatePaymentRequestValidator"/>.</summary>
    /// <param name="timeProvider">The clock supplying the "not in the future" upper bound.</param>
    public CreatePaymentRequestValidator(TimeProvider timeProvider)
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
    }
}
