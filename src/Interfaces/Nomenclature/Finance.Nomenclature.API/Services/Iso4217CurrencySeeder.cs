using Finance.Nomenclature.API.Interfaces;
using Finance.Nomenclature.API.Seeding;
using Finance.Nomenclature.DBModel;
using Finance.Nomenclature.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Finance.Nomenclature.API.Services;

/// <summary>
/// Default <see cref="ICurrencySeeder"/> that upserts the bundled ISO 4217 currency list
/// (SDD-NOM-001 §2.5). Existing rows (matched by <c>IsoCode</c>) are skipped — never overwritten — and
/// currencies are never removed, so the seed is safe to run on every startup.
/// </summary>
public sealed class Iso4217CurrencySeeder : ICurrencySeeder
{
    private readonly NomenclatureDbContext _db;
    private readonly ILogger<Iso4217CurrencySeeder> _logger;

    /// <summary>Creates a new <see cref="Iso4217CurrencySeeder"/>.</summary>
    /// <param name="db">The nomenclature database context.</param>
    /// <param name="logger">Logger used to record the seed outcome.</param>
    public Iso4217CurrencySeeder(NomenclatureDbContext db, ILogger<Iso4217CurrencySeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> SeedAsync(CancellationToken cancellationToken)
    {
        HashSet<string> existingCodes = await LoadExistingCodesAsync(cancellationToken).ConfigureAwait(false);

        List<Currency> toInsert = BuildMissingCurrencies(existingCodes);
        if (toInsert.Count == 0)
        {
            _logger.LogInformation("ISO 4217 currency seed skipped; all {Total} currencies already present.",
                Iso4217CurrencyList.All.Count);
            return 0;
        }

        _db.Currencies.AddRange(toInsert);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("ISO 4217 currency seed inserted {Inserted} new currencies.", toInsert.Count);
        return toInsert.Count;
    }

    private async Task<HashSet<string>> LoadExistingCodesAsync(CancellationToken cancellationToken)
    {
        List<string> codes = await _db.Currencies
            .AsNoTracking()
            .Select(c => c.IsoCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new HashSet<string>(codes, StringComparer.Ordinal);
    }

    private static List<Currency> BuildMissingCurrencies(HashSet<string> existingCodes)
    {
        List<Currency> currencies = [];
        foreach (Iso4217Currency definition in Iso4217CurrencyList.All)
        {
            if (existingCodes.Contains(definition.IsoCode))
            {
                continue;
            }

            currencies.Add(new Currency
            {
                IsoCode = definition.IsoCode,
                Name = definition.Name,
                Symbol = definition.Symbol,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        return currencies;
    }
}
