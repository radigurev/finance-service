using Finance.Common.ErrorCodes;
using Finance.Infrastructure.Web.ErrorMapping;
using Microsoft.AspNetCore.Http;

namespace Finance.Invoices.API.ErrorMapping;

/// <summary>
/// Invoice-domain extension of <see cref="DefaultErrorCodeToStatusMap"/> (SDD-INV-001 §4). The default
/// suffix/pattern rules do not classify the Invoice state-conflict codes (<c>INVOICE_NOT_DRAFT</c>,
/// <c>INVOICE_NOT_CONFIRMED</c>, <c>INVOICE_POSTED_IMMUTABLE</c>, <c>INVALID_INVOICE_STATE_TRANSITION</c>,
/// <c>INVOICE_PERIOD_CLOSED</c>, <c>INVOICE_DUPLICATE_DOCUMENT_NUMBER</c>, <c>INVOICE_HAS_SETTLEMENTS</c>) as
/// 409, so this map adds them and delegates every other code to the default map (where <c>*_NOT_FOUND</c> →
/// 404, <c>CONCURRENT_*</c> → 409, and the remaining validation codes → 400).
/// </summary>
public sealed class InvoiceErrorCodeToStatusMap : IErrorCodeToStatusMap
{
    private static readonly IReadOnlySet<string> ConflictCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        InvoiceErrorCodes.INVOICE_NOT_DRAFT,
        InvoiceErrorCodes.INVOICE_NOT_CONFIRMED,
        InvoiceErrorCodes.INVOICE_POSTED_IMMUTABLE,
        InvoiceErrorCodes.INVALID_INVOICE_STATE_TRANSITION,
        InvoiceErrorCodes.INVOICE_PERIOD_CLOSED,
        InvoiceErrorCodes.INVOICE_DUPLICATE_DOCUMENT_NUMBER,
        InvoiceErrorCodes.INVOICE_HAS_SETTLEMENTS
    };

    private readonly DefaultErrorCodeToStatusMap _default = new();

    /// <inheritdoc />
    public int MapToStatus(string errorCode)
    {
        if (string.IsNullOrEmpty(errorCode))
        {
            return _default.MapToStatus(errorCode);
        }

        if (ConflictCodes.Contains(errorCode))
        {
            return StatusCodes.Status409Conflict;
        }

        return _default.MapToStatus(errorCode);
    }
}
