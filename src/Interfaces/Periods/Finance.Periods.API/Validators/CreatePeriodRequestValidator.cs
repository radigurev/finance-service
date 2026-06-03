using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Periods;
using FluentValidation;

namespace Finance.Periods.API.Validators;

/// <summary>
/// FluentValidation rules for <see cref="CreatePeriodRequest"/> (SDD-FIN-004 §3.1). Enforces a plausible
/// fiscal year, a 1–12 period number, and a <c>StartDate</c> strictly before <c>EndDate</c>; uniqueness and
/// overlap are cross-aggregate guards handled in the service.
/// </summary>
public sealed class CreatePeriodRequestValidator : AbstractValidator<CreatePeriodRequest>
{
    private const int MinFiscalYear = 2000;
    private const int MaxFiscalYear = 2100;
    private const int MinPeriodNumber = 1;
    private const int MaxPeriodNumber = 12;

    /// <summary>Configures the validation rules.</summary>
    public CreatePeriodRequestValidator()
    {
        RuleFor(request => request.FiscalYear)
            .InclusiveBetween(MinFiscalYear, MaxFiscalYear)
            .WithErrorCode(PeriodErrorCodes.INVALID_PERIOD);

        RuleFor(request => request.PeriodNumber)
            .InclusiveBetween(MinPeriodNumber, MaxPeriodNumber)
            .WithErrorCode(PeriodErrorCodes.INVALID_PERIOD);

        RuleFor(request => request.EndDate)
            .GreaterThan(request => request.StartDate)
            .WithErrorCode(PeriodErrorCodes.INVALID_PERIOD);
    }
}
