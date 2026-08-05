using Finance.Common.ErrorCodes;
using Finance.Infrastructure.Web.ErrorMapping;
using Microsoft.AspNetCore.Http;

namespace Finance.Payments.API.ErrorMapping;

/// <summary>
/// Payment-domain extension of <see cref="DefaultErrorCodeToStatusMap"/> (SDD-PAY-001 §4, SDD-PAY-002 §4). The
/// default suffix/pattern rules do not classify the Payment state, period, numbering, and allocation conflict
/// codes as 409, so they would silently fall through to 400 — this map names them and delegates every other code
/// to a private default map (where <c>*_NOT_FOUND</c> → 404, <c>*_INACTIVE</c> → 409, <c>*DUPLICATE*</c> → 409,
/// <c>CONCURRENT_*</c> → 409, and the remaining validation codes → 400).
/// <para><b>This map is ONE class shared by both payment specs and is registered exactly once.</b> It carries
/// SDD-PAY-001 §4's EIGHT lifecycle conflicts plus SDD-PAY-002 §4's EIGHT allocation conflicts — <b>SIXTEEN</b>
/// explicit entries in total, the count both specs pin. It MUST NOT be rebuilt from SDD-PAY-001 §4 alone, or the
/// allocation codes would answer 400 where SDD-PAY-002 §4 documents 409, a client-visible contract break.</para>
/// <para>Deliberately ABSENT (they resolve correctly through the default map, and adding them would be
/// redundant): <c>PAYMENT_ALLOCATION_DUPLICATE</c> (the <c>DUPLICATE</c> pattern → 409), the three
/// <c>*_NOT_FOUND</c> codes → 404, and <c>CONCURRENT_MODIFICATION</c> (the <c>CONCURRENT_</c> prefix → 409).
/// SDD-PAY-003 adds nothing: all of its codes are 400 validation codes.</para>
/// </summary>
public sealed class PaymentErrorCodeToStatusMap : IErrorCodeToStatusMap
{
    private static readonly IReadOnlySet<string> ConflictCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        PaymentErrorCodes.PAYMENT_NOT_DRAFT,
        PaymentErrorCodes.PAYMENT_NOT_CONFIRMED,
        PaymentErrorCodes.PAYMENT_POSTING_PENDING,
        PaymentErrorCodes.PAYMENT_POSTED_IMMUTABLE,
        PaymentErrorCodes.INVALID_PAYMENT_STATE_TRANSITION,
        PaymentErrorCodes.PAYMENT_PERIOD_CLOSED,
        PaymentErrorCodes.PAYMENT_HAS_ALLOCATIONS,
        PaymentErrorCodes.PAYMENT_DATE_YEAR_MISMATCH,
        PaymentErrorCodes.PAYMENT_NOT_ALLOCATABLE,
        PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_NOT_ELIGIBLE,
        PaymentErrorCodes.PAYMENT_ALLOCATION_EXCEEDS_PAYMENT,
        PaymentErrorCodes.PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING,
        PaymentErrorCodes.PAYMENT_ALLOCATION_DIRECTION_MISMATCH,
        PaymentErrorCodes.PAYMENT_ALLOCATION_COUNTERPARTY_MISMATCH,
        PaymentErrorCodes.PAYMENT_ALLOCATION_CURRENCY_MISMATCH,
        PaymentErrorCodes.PAYMENT_ALLOCATION_CONTROL_ACCOUNT_MISMATCH
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
