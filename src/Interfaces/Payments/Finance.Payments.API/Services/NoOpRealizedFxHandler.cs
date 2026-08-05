using Finance.Common.Results;
using Finance.Payments.API.Interfaces;
using Microsoft.Extensions.Logging;

namespace Finance.Payments.API.Services;

/// <summary>
/// The v1 registered <see cref="IRealizedFxHandler"/>: an inert no-op that always succeeds
/// (SDD-PAY-002 §2.9). It posts nothing, creates and mutates no journal entry, and changes no GL or
/// trial-balance figure — until SDD-FIN-005 lands, the computed difference stored on the allocation row is
/// INFORMATIONAL ONLY.
/// <para>It exists so allocation is wired end-to-end through the seam rather than shipping the only SDD-FIN-005
/// extension point unconnected, mirroring <c>AlwaysOpenPostingPeriodGuard</c> in SDD-FIN-002 §2.7. A non-zero
/// difference is logged at debug through a structured template so the dormant figure is observable without
/// noise on the common (rates equal) path.</para>
/// </summary>
public sealed class NoOpRealizedFxHandler : IRealizedFxHandler
{
    private readonly ILogger<NoOpRealizedFxHandler> _logger;

    /// <summary>Creates a new <see cref="NoOpRealizedFxHandler"/>.</summary>
    /// <param name="logger">The structured logger used for the dormant-difference trace.</param>
    public NoOpRealizedFxHandler(ILogger<NoOpRealizedFxHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<Result> HandleAsync(RealizedFxContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.RealizedFxDifference != 0m)
        {
            _logger.LogDebug(
                "Realized FX difference computed for payment {PaymentId} against invoice {InvoiceId}: "
                + "{RealizedFxDifference} {BaseCurrencyCode}. Posting is deferred to SDD-FIN-005; no GL effect.",
                context.PaymentId,
                context.InvoiceId,
                context.RealizedFxDifference,
                context.BaseCurrencyCode);
        }

        return Task.FromResult(Result.Success());
    }
}
