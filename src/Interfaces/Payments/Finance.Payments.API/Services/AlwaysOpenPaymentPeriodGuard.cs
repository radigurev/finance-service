using Finance.Common.Results;
using Finance.Payments.API.Interfaces;

namespace Finance.Payments.API.Services;

/// <summary>
/// An <see cref="IPaymentPeriodGuard"/> that treats every period as open.
/// <para><b>TEST-ONLY. This type MUST NEVER be registered in production DI</b> (SDD-PAY-001 §2.9). The
/// production registration is <c>GatewayPaymentPeriodGuard</c>, which performs the real period-status lookup
/// from day one and fails closed — this service deliberately does NOT repeat the Invoices service's
/// always-open production registration. The stub exists so unit tests can exercise the confirm and reverse
/// paths without a Periods service, mirroring the Journal service's <c>AlwaysOpenPostingPeriodGuard</c>
/// fallback.</para>
/// </summary>
public sealed class AlwaysOpenPaymentPeriodGuard : IPaymentPeriodGuard
{
    /// <inheritdoc />
    public Task<Result> EnsureOpenAsync(DateTimeOffset paymentDate, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success());
    }
}
