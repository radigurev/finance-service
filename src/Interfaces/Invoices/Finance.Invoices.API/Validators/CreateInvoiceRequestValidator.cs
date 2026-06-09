using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Country.Abstractions;
using Finance.ServiceModel.Invoices;
using FluentValidation;

namespace Finance.Invoices.API.Validators;

/// <summary>
/// FluentValidation shape rules for <see cref="CreateInvoiceRequest"/> (SDD-INV-001 §3.1): a valid document
/// type, a present counterparty, an ISO-4217 currency, valid issue/due dates, at least one line for a manual
/// create, and per-line shape. Cross-field totals reconciliation runs in the service (SDD-INV-001 §2.8).
/// </summary>
public sealed class CreateInvoiceRequestValidator : AbstractValidator<CreateInvoiceRequest>
{
    /// <summary>Creates a new <see cref="CreateInvoiceRequestValidator"/>.</summary>
    /// <param name="countryStrategy">The country strategy owning the legal tax-rate set (SDD-CTRY-001).</param>
    public CreateInvoiceRequestValidator(ICountryStrategy countryStrategy)
    {
        ArgumentNullException.ThrowIfNull(countryStrategy);

        RuleFor(request => request.DocumentType)
            .IsInEnum()
            .WithErrorCode(InvoiceErrorCodes.INVALID_INVOICE_DOCUMENT_TYPE);

        RuleFor(request => request.CounterpartyId)
            .NotEqual(Guid.Empty)
            .WithErrorCode(InvoiceErrorCodes.INVOICE_COUNTERPARTY_REQUIRED);

        RuleFor(request => request.CurrencyCode)
            .Matches("^[A-Z]{3}$")
            .WithErrorCode(InvoiceErrorCodes.INVALID_INVOICE_CURRENCY);

        RuleFor(request => request.IssueDate)
            .Must(date => date != default)
            .WithErrorCode(InvoiceErrorCodes.INVALID_INVOICE_DATE);

        RuleFor(request => request.DueDate)
            .GreaterThanOrEqualTo(request => request.IssueDate)
            .WithErrorCode(InvoiceErrorCodes.INVALID_INVOICE_DUE_DATE);

        RuleFor(request => request.Lines)
            .Must(lines => lines is { Count: >= 1 })
            .WithErrorCode(InvoiceErrorCodes.INVOICE_LINES_REQUIRED);

        RuleForEach(request => request.Lines)
            .SetValidator(new InvoiceLineRequestValidator(countryStrategy));
    }
}
