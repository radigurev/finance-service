using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Payments.API.Interfaces;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// Configurable <see cref="IPaymentPeriodGuard"/> for the Payments unit tests (SDD-PAY-001 §2.9). It allows every
/// period by default; setting <see cref="IsOpen"/> to <c>false</c> exercises the fail-closed
/// <c>PAYMENT_PERIOD_CLOSED</c> path that covers a closed period, a date with no period, and an unreachable
/// Periods service alike. Every requested date is recorded so a test can pin WHICH date the guard was asked about
/// — the SDD-PAY-001 §2.7 reverse pre-check must evaluate the LINKED entry's date, which equals
/// <c>PaymentDate</c> by construction.
/// </summary>
public sealed class FakePaymentPeriodGuard : IPaymentPeriodGuard
{
    /// <summary>Whether the guard reports the payment date's period as open. Defaults to <c>true</c>.</summary>
    public bool IsOpen { get; set; } = true;

    /// <summary>The dates the guard was asked about, in call order.</summary>
    public List<DateTimeOffset> RequestedDates { get; } = [];

    /// <inheritdoc />
    public Task<Result> EnsureOpenAsync(DateTimeOffset paymentDate, CancellationToken cancellationToken)
    {
        RequestedDates.Add(paymentDate);

        return Task.FromResult(IsOpen
            ? Result.Success()
            : Result.Failure(
                PaymentErrorCodes.PAYMENT_PERIOD_CLOSED,
                "The fiscal period for the payment date is closed."));
    }
}
