using Finance.Common.Results;

namespace Finance.Payments.API.Interfaces;

/// <summary>
/// The DORMANT realized-FX seam (SDD-PAY-002 §2.9). The allocation service computes the signed base-currency
/// difference between the two documents' frozen rates, stores it on the allocation row, and hands it here — once
/// per allocation row, INSIDE the allocation transaction. A non-success result FAILS the whole allocation.
/// <para>The only registered implementation in v1 is <c>NoOpRealizedFxHandler</c>, which always succeeds, so
/// allocation works end-to-end while SDD-FIN-005 is unauthored. Posting the difference to a country FX
/// gain/loss account through the Posting Engine is DEFERRED to that spec, and this seam is its ONLY extension
/// point — no change to the allocation code, the stored column, or the events will be required.</para>
/// <para>The seam MUST be invoked even when the computed difference is <c>0.00</c>: the invocation is the
/// contract, not the value. This mirrors the way SDD-FIN-002 §2.7 shipped <c>IPostingPeriodGuard</c> with an
/// always-open default so posting worked before SDD-FIN-004 existed.</para>
/// </summary>
public interface IRealizedFxHandler
{
    /// <summary>
    /// Handles the realized-FX difference for a single allocation row inside the allocation transaction.
    /// </summary>
    /// <param name="context">The two frozen rates, the allocated amount, and the computed signed difference.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>
    /// A success result when the difference has been handled (always, for the v1 no-op); a failure result,
    /// which fails the whole allocation and writes nothing.
    /// </returns>
    Task<Result> HandleAsync(RealizedFxContext context, CancellationToken cancellationToken);
}
