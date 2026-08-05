using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Payments.API.Interfaces;
using Finance.ServiceModel.Accounts;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// Configurable <see cref="ISettlementAccountReader"/> for the Payments unit tests (SDD-PAY-001 §2.8). It reports
/// the settlement account as usable by default; <see cref="Outcome"/> switches it to the
/// <c>PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND</c> (missing, or unreachable and failing closed) and
/// <c>PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE</c> resolutions without needing a gateway.
/// </summary>
public sealed class FakeSettlementAccountReader : ISettlementAccountReader
{
    /// <summary>The three documented resolutions of the settlement-account check.</summary>
    public enum ReaderOutcome
    {
        /// <summary>The account exists and is active.</summary>
        Usable = 0,

        /// <summary>The account does not exist, or the reader is unreachable and fails closed.</summary>
        NotFound = 1,

        /// <summary>The account exists but is not active.</summary>
        Inactive = 2
    }

    /// <summary>The resolution every call returns. Defaults to <see cref="ReaderOutcome.Usable"/>.</summary>
    public ReaderOutcome Outcome { get; set; } = ReaderOutcome.Usable;

    /// <summary>The account identifiers the reader was asked about, in call order.</summary>
    public List<int> RequestedAccountIds { get; } = [];

    /// <inheritdoc />
    public Task<Result> EnsureUsableAsync(int settlementAccountId, CancellationToken cancellationToken)
    {
        RequestedAccountIds.Add(settlementAccountId);

        return Task.FromResult(Outcome switch
        {
            ReaderOutcome.NotFound => Result.Failure(
                PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND,
                "The settlement account does not exist."),
            ReaderOutcome.Inactive => Result.Failure(
                PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE,
                "The settlement account exists but is not active."),
            _ => Result.Success()
        });
    }

    /// <inheritdoc />
    public bool IsPostable(AccountDto account) => true;
}
