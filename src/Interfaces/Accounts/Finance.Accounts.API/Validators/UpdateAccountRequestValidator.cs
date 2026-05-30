using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Accounts;
using FluentValidation;

namespace Finance.Accounts.API.Validators;

/// <summary>
/// FluentValidation shape rules for <see cref="UpdateAccountRequest"/> (SDD-ACCT-001 §2.4, §3.1).
/// Only <c>Name</c> and <c>IsActive</c> are mutable; <c>Code</c>, <c>Type</c>, <c>ParentId</c>, and
/// <c>CountryCode</c> are immutable after creation.
/// </summary>
public sealed class UpdateAccountRequestValidator : AbstractValidator<UpdateAccountRequest>
{
    /// <summary>Configures the validation rules.</summary>
    public UpdateAccountRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode(AccountErrorCodes.INVALID_ACCOUNT_CODE)
            .MaximumLength(200).WithErrorCode(AccountErrorCodes.INVALID_ACCOUNT_CODE);
    }
}
