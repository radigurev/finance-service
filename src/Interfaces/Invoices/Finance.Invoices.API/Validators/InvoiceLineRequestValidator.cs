using Finance.Common.ErrorCodes;
using Finance.Country.Abstractions;
using Finance.ServiceModel.Invoices;
using FluentValidation;

namespace Finance.Invoices.API.Validators;

/// <summary>
/// FluentValidation shape rules for a single <see cref="InvoiceLineRequest"/> (SDD-INV-001 §3.1): a strictly
/// positive quantity, a non-negative unit price, and a tax rate the country recognizes (validated through
/// <see cref="ICountryStrategy.IsValidTaxRate"/> so the core never hard-codes a VAT rate).
/// </summary>
public sealed class InvoiceLineRequestValidator : AbstractValidator<InvoiceLineRequest>
{
    /// <summary>Creates a new <see cref="InvoiceLineRequestValidator"/>.</summary>
    /// <param name="countryStrategy">The country strategy owning the legal tax-rate set (SDD-CTRY-001).</param>
    public InvoiceLineRequestValidator(ICountryStrategy countryStrategy)
    {
        ArgumentNullException.ThrowIfNull(countryStrategy);

        RuleFor(line => line.Quantity)
            .GreaterThan(0m)
            .WithErrorCode(InvoiceErrorCodes.INVALID_INVOICE_LINE);

        RuleFor(line => line.UnitPrice)
            .GreaterThanOrEqualTo(0m)
            .WithErrorCode(InvoiceErrorCodes.INVALID_INVOICE_LINE);

        RuleFor(line => line.TaxRate)
            .Must(countryStrategy.IsValidTaxRate)
            .WithErrorCode(InvoiceErrorCodes.INVALID_INVOICE_TAX_RATE);
    }
}
