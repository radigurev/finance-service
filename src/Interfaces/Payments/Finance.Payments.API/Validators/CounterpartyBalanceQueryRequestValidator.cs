using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Payments;
using FluentValidation;

namespace Finance.Payments.API.Validators;

/// <summary>
/// FluentValidation SHAPE rules for <see cref="CounterpartyBalanceQueryRequest"/> (SDD-PAY-003 §3.1): the as-of
/// date and direction are REQUIRED, the as-of date must not be in the future, and the optional currency must be a
/// three-letter ISO 4217 code.
/// <para>There is deliberately no counterparty rule: the endpoint is the per-counterparty roll-up and takes no
/// counterparty narrowing.</para>
/// </summary>
public sealed class CounterpartyBalanceQueryRequestValidator : AbstractValidator<CounterpartyBalanceQueryRequest>
{
    /// <summary>Creates a new <see cref="CounterpartyBalanceQueryRequestValidator"/>.</summary>
    /// <param name="timeProvider">The clock supplying the "not in the future" upper bound.</param>
    public CounterpartyBalanceQueryRequestValidator(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        RuleFor(request => request.AsOfDate)
            .NotNull()
            .WithErrorCode(PaymentErrorCodes.INVALID_AGING_AS_OF_DATE);

        RuleFor(request => request.AsOfDate)
            .Must(asOfDate => AgingQueryRules.IsNotInFuture(asOfDate!.Value, timeProvider))
            .When(request => request.AsOfDate.HasValue)
            .WithErrorCode(PaymentErrorCodes.INVALID_AGING_AS_OF_DATE);

        RuleFor(request => request.Direction)
            .Must(AgingQueryRules.IsRecognizedDirection)
            .WithErrorCode(PaymentErrorCodes.INVALID_AGING_DIRECTION);

        RuleFor(request => request.CurrencyCode)
            .Must(AgingQueryRules.IsWellFormedCurrency)
            .When(request => request.CurrencyCode is not null)
            .WithErrorCode(PaymentErrorCodes.INVALID_AGING_CURRENCY);
    }
}
