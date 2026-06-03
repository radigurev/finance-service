using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Periods;
using FluentValidation;

namespace Finance.Periods.API.Validators;

/// <summary>
/// FluentValidation rules for <see cref="GeneratePeriodsRequest"/> (SDD-FIN-004 §3.1). Enforces a plausible
/// fiscal-year range; uniqueness and overlap are cross-aggregate guards handled in the service.
/// </summary>
public sealed class GeneratePeriodsRequestValidator : AbstractValidator<GeneratePeriodsRequest>
{
    private const int MinFiscalYear = 2000;
    private const int MaxFiscalYear = 2100;

    /// <summary>Configures the validation rules.</summary>
    public GeneratePeriodsRequestValidator()
    {
        RuleFor(request => request.FiscalYear)
            .InclusiveBetween(MinFiscalYear, MaxFiscalYear)
            .WithErrorCode(PeriodErrorCodes.INVALID_PERIOD);
    }
}
