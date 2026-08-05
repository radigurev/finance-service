using Finance.Common.ErrorCodes;
using Finance.Payments.API.Services;
using Finance.ServiceModel.Payments;
using FluentValidation;

namespace Finance.Payments.API.Validators;

/// <summary>
/// FluentValidation SHAPE rules for <see cref="AgingReportQueryRequest"/> (SDD-PAY-003 §3.1): the as-of date and
/// direction are REQUIRED, the as-of date must not be in the future, the optional counterparty must be a non-empty
/// GUID, the optional currency must be a three-letter ISO 4217 code, and the optional bucket boundaries must be at
/// most six strictly ascending positive integers.
/// <para>The bucket rule is expressed by delegating to the same <see cref="AgingBucketCalculator"/> the service
/// uses, so the pre-binding rejection and the service-level guard cannot disagree about which boundary sets are
/// legal.</para>
/// </summary>
public sealed class AgingReportQueryRequestValidator : AbstractValidator<AgingReportQueryRequest>
{
    /// <summary>Creates a new <see cref="AgingReportQueryRequestValidator"/>.</summary>
    /// <param name="timeProvider">The clock supplying the "not in the future" upper bound.</param>
    /// <param name="bucketCalculator">The pure bucket calculator owning the boundary rules.</param>
    public AgingReportQueryRequestValidator(TimeProvider timeProvider, AgingBucketCalculator bucketCalculator)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(bucketCalculator);

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

        RuleFor(request => request.CounterpartyId)
            .NotEqual(Guid.Empty)
            .When(request => request.CounterpartyId.HasValue)
            .WithErrorCode(PaymentErrorCodes.INVALID_COUNTERPARTY_ID);

        RuleFor(request => request.CurrencyCode)
            .Must(AgingQueryRules.IsWellFormedCurrency)
            .When(request => request.CurrencyCode is not null)
            .WithErrorCode(PaymentErrorCodes.INVALID_AGING_CURRENCY);

        RuleFor(request => request.Buckets)
            .Must(buckets => bucketCalculator.Build(buckets).IsSuccess)
            .When(request => request.Buckets is not null)
            .WithErrorCode(PaymentErrorCodes.INVALID_AGING_BUCKETS);
    }
}
