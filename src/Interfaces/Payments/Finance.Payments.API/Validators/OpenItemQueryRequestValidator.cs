using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Payments;
using FluentValidation;

namespace Finance.Payments.API.Validators;

/// <summary>
/// FluentValidation SHAPE rules for <see cref="OpenItemQueryRequest"/> (SDD-PAY-003 §3.1). Every narrowing is
/// optional, so each rule is conditional: an as-of date must not be in the future, a supplied direction must be
/// <c>AR</c>/<c>AP</c>, a supplied counterparty must be a non-empty GUID, and a supplied currency must be a
/// three-letter ISO 4217 code.
/// <para>All of these rules are shape-only, so no <c>IChainValidator</c> chain is registered for the aging surface
/// (SDD-INFRA-007 does not apply). The service re-asserts the same rules through
/// <see cref="AgingQueryRules"/> so the guard holds even when a caller reaches the service directly.</para>
/// </summary>
public sealed class OpenItemQueryRequestValidator : AbstractValidator<OpenItemQueryRequest>
{
    /// <summary>Creates a new <see cref="OpenItemQueryRequestValidator"/>.</summary>
    /// <param name="timeProvider">The clock supplying the "not in the future" upper bound.</param>
    public OpenItemQueryRequestValidator(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        RuleFor(request => request.AsOfDate)
            .Must(asOfDate => AgingQueryRules.IsNotInFuture(asOfDate!.Value, timeProvider))
            .When(request => request.AsOfDate.HasValue)
            .WithErrorCode(PaymentErrorCodes.INVALID_AGING_AS_OF_DATE);

        RuleFor(request => request.Direction)
            .Must(AgingQueryRules.IsRecognizedDirection)
            .When(request => request.Direction is not null)
            .WithErrorCode(PaymentErrorCodes.INVALID_AGING_DIRECTION);

        RuleFor(request => request.CounterpartyId)
            .NotEqual(Guid.Empty)
            .When(request => request.CounterpartyId.HasValue)
            .WithErrorCode(PaymentErrorCodes.INVALID_COUNTERPARTY_ID);

        RuleFor(request => request.CurrencyCode)
            .Must(AgingQueryRules.IsWellFormedCurrency)
            .When(request => request.CurrencyCode is not null)
            .WithErrorCode(PaymentErrorCodes.INVALID_AGING_CURRENCY);
    }
}
