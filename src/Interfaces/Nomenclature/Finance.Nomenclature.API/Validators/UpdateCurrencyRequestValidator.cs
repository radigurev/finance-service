using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Nomenclature;
using FluentValidation;

namespace Finance.Nomenclature.API.Validators;

/// <summary>
/// FluentValidation shape rules for <see cref="UpdateCurrencyRequest"/> (SDD-NOM-001 §2.1, §3). Only
/// <c>Name</c>, <c>Symbol</c>, and <c>IsActive</c> are mutable; <c>IsoCode</c> is immutable and is taken
/// from the request path, not the body.
/// </summary>
public sealed class UpdateCurrencyRequestValidator : AbstractValidator<UpdateCurrencyRequest>
{
    /// <summary>Configures the validation rules.</summary>
    public UpdateCurrencyRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode(NomenclatureErrorCodes.INVALID_CURRENCY_NAME)
            .MaximumLength(100).WithErrorCode(NomenclatureErrorCodes.INVALID_CURRENCY_NAME);

        RuleFor(x => x.Symbol)
            .MaximumLength(5).WithErrorCode(NomenclatureErrorCodes.INVALID_CURRENCY_SYMBOL);
    }
}
