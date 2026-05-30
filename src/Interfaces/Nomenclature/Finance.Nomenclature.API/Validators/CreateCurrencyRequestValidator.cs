using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Nomenclature;
using FluentValidation;

namespace Finance.Nomenclature.API.Validators;

/// <summary>
/// FluentValidation shape rules for <see cref="CreateCurrencyRequest"/> (SDD-NOM-001 §3). Uniqueness is
/// enforced separately by the <see cref="DuplicateCurrencyCodeValidator"/> chain validator.
/// </summary>
public sealed class CreateCurrencyRequestValidator : AbstractValidator<CreateCurrencyRequest>
{
    private const string IsoCodePattern = "^[A-Z]{3}$";

    /// <summary>Configures the validation rules.</summary>
    public CreateCurrencyRequestValidator()
    {
        RuleFor(x => x.IsoCode)
            .NotEmpty().WithErrorCode(NomenclatureErrorCodes.INVALID_CURRENCY_CODE)
            .Matches(IsoCodePattern).WithErrorCode(NomenclatureErrorCodes.INVALID_CURRENCY_CODE);

        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode(NomenclatureErrorCodes.INVALID_CURRENCY_NAME)
            .MaximumLength(100).WithErrorCode(NomenclatureErrorCodes.INVALID_CURRENCY_NAME);

        RuleFor(x => x.Symbol)
            .MaximumLength(5).WithErrorCode(NomenclatureErrorCodes.INVALID_CURRENCY_SYMBOL);
    }
}
