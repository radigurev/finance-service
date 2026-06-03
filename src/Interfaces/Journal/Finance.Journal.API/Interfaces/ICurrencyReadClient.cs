using Finance.ServiceModel.Nomenclature;
using Refit;

namespace Finance.Journal.API.Interfaces;

/// <summary>
/// Refit contract for the Currencies read endpoint consumed through the Finance Gateway
/// (SDD-FIN-001 §2.7, resolved §7). The Journal service owns only <c>finance_journal</c> and MUST NOT
/// cross-database-join into <c>finance_nomenclature</c>; currency validity is asserted at draft/post time
/// via a synchronous read of <c>GET /api/v1/currencies/{isoCode}</c>. Registered with the standard handler
/// chain (<c>CorrelationIdDelegatingHandler</c> → bearer forwarding → resilience).
/// </summary>
public interface ICurrencyReadClient
{
    /// <summary>Reads a single currency by its ISO 4217 alphabetic code.</summary>
    /// <param name="isoCode">The ISO 4217 alphabetic code.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The currency when found; otherwise an API error captured by the caller.</returns>
    [Get("/api/v1/currencies/{isoCode}")]
    Task<CurrencyDto> GetCurrencyAsync(string isoCode, CancellationToken cancellationToken);
}
