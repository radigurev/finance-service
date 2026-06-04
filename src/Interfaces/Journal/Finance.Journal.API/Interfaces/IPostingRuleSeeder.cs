namespace Finance.Journal.API.Interfaces;

/// <summary>
/// Seeds the country's default posting-rule templates from <c>ICountryStrategy.GetDefaultPostingRules()</c>
/// into the posting-rule store (SDD-FIN-006 §2.2), mirroring the ISO 4217 currency seeder (SDD-NOM-001
/// §2.5). The seed is an idempotent, non-destructive upsert keyed by <c>RuleKey</c>: an existing rule is
/// NEVER overwritten (administrator edits are preserved), and a structurally unbalanceable template is
/// skipped and logged. Account selectors are stored as codes and resolved at apply time (resolved
/// decision: SDD-FIN-006 §7).
/// </summary>
public interface IPostingRuleSeeder
{
    /// <summary>
    /// Inserts every default template whose <c>RuleKey</c> is not yet present, leaving existing rows
    /// untouched and skipping structurally unbalanceable templates (SDD-FIN-006 §2.2).
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The number of posting rules inserted.</returns>
    Task<int> SeedAsync(CancellationToken cancellationToken);
}
