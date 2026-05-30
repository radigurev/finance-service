namespace Finance.Nomenclature.API.Interfaces;

/// <summary>
/// Seeds the bundled ISO 4217 currency list into the database (SDD-NOM-001 §2.5). The seeder is an
/// idempotent upsert that NEVER overwrites an existing currency row and NEVER removes a currency, so
/// re-running it on every startup is safe. It only inserts currencies whose ISO code is not yet present.
/// </summary>
public interface ICurrencySeeder
{
    /// <summary>
    /// Inserts every bundled ISO 4217 currency that is not already present, leaving existing rows
    /// untouched (SDD-NOM-001 §2.5).
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The number of currencies inserted.</returns>
    Task<int> SeedAsync(CancellationToken cancellationToken);
}
