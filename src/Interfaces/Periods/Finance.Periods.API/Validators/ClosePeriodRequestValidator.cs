using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Periods;
using FluentValidation;

namespace Finance.Periods.API.Validators;

/// <summary>
/// FluentValidation rules for <see cref="ClosePeriodRequest"/> (SDD-FIN-004 §3.1). A non-empty
/// <c>Reason</c> is mandatory before any state change (close is a SENSITIVE op, SDD-AUDIT-001).
/// </summary>
public sealed class ClosePeriodRequestValidator : AbstractValidator<ClosePeriodRequest>
{
    /// <summary>Configures the validation rules.</summary>
    public ClosePeriodRequestValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty()
            .WithErrorCode(PeriodErrorCodes.CLOSE_REASON_REQUIRED);
    }
}
