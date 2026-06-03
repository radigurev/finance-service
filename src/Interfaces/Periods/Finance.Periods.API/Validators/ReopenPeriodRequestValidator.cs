using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Periods;
using FluentValidation;

namespace Finance.Periods.API.Validators;

/// <summary>
/// FluentValidation rules for <see cref="ReopenPeriodRequest"/> (SDD-FIN-004 §3.1). A non-empty
/// <c>Reason</c> is mandatory before any state change (reopen is a SENSITIVE op, SDD-AUDIT-001).
/// </summary>
public sealed class ReopenPeriodRequestValidator : AbstractValidator<ReopenPeriodRequest>
{
    /// <summary>Configures the validation rules.</summary>
    public ReopenPeriodRequestValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty()
            .WithErrorCode(PeriodErrorCodes.REOPEN_REASON_REQUIRED);
    }
}
