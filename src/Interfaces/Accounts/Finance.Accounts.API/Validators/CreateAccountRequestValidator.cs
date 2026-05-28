using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Accounts;
using FluentValidation;

namespace Finance.Accounts.API.Validators;

/// <summary>
/// FluentValidation rules for <see cref="CreateAccountRequest"/>.
/// </summary>
public sealed class CreateAccountRequestValidator : AbstractValidator<CreateAccountRequest>
{
    /// <summary>Configures the validation rules.</summary>
    public CreateAccountRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithErrorCode(AccountErrorCodes.INVALID_ACCOUNT_CODE)
            .MaximumLength(20).WithErrorCode(AccountErrorCodes.INVALID_ACCOUNT_CODE);

        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode(AccountErrorCodes.INVALID_ACCOUNT_CODE)
            .MaximumLength(200).WithErrorCode(AccountErrorCodes.INVALID_ACCOUNT_CODE);

        RuleFor(x => x.Type)
            .IsInEnum().WithErrorCode(AccountErrorCodes.INVALID_ACCOUNT_TYPE);
    }
}
