using Finance.Common.ErrorCodes;
using Finance.ServiceModel.Invoices;
using FluentValidation;

namespace Finance.Invoices.API.Validators;

/// <summary>
/// FluentValidation shape rule for <see cref="CancelInvoiceRequest"/> (SDD-INV-001 §3.1): a non-empty
/// reason is mandatory (cancellation voids a numbered document).
/// </summary>
public sealed class CancelInvoiceRequestValidator : AbstractValidator<CancelInvoiceRequest>
{
    /// <summary>Configures the cancel-request shape rules.</summary>
    public CancelInvoiceRequestValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty()
            .WithErrorCode(InvoiceErrorCodes.INVOICE_CANCEL_REASON_REQUIRED);
    }
}
