using Finance.Common.ErrorCodes;
using Finance.Country.Abstractions;
using Finance.Journal.API.Interfaces;
using Finance.Journal.DBModel;
using Finance.Journal.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Finance.Journal.API.Services;

/// <summary>
/// Default <see cref="IPostingRuleSeeder"/> that upserts the country strategy's default posting-rule
/// templates (SDD-FIN-006 §2.2, SDD-CTRY-001). Existing rules (matched by <c>RuleKey</c>) are skipped —
/// never overwritten — and structurally unbalanceable templates are skipped and logged, so the seed is
/// safe to run on every startup and never crashes it. Account selectors are persisted as codes and
/// resolved at apply time (SDD-FIN-006 §7).
/// </summary>
public sealed class PostingRuleSeeder : IPostingRuleSeeder
{
    private readonly JournalDbContext _db;
    private readonly ICountryStrategy _countryStrategy;
    private readonly ILogger<PostingRuleSeeder> _logger;

    /// <summary>Creates a new <see cref="PostingRuleSeeder"/>.</summary>
    /// <param name="db">The journal database context.</param>
    /// <param name="countryStrategy">The country strategy supplying the default templates.</param>
    /// <param name="logger">Logger used to record the seed outcome and skipped templates.</param>
    public PostingRuleSeeder(
        JournalDbContext db,
        ICountryStrategy countryStrategy,
        ILogger<PostingRuleSeeder> logger)
    {
        _db = db;
        _countryStrategy = countryStrategy;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> SeedAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PostingRuleTemplate> templates = _countryStrategy.GetDefaultPostingRules();
        if (templates.Count == 0)
        {
            _logger.LogInformation("Posting-rule seed skipped; country strategy supplied no templates.");
            return 0;
        }

        HashSet<string> existingKeys = await LoadExistingKeysAsync(cancellationToken).ConfigureAwait(false);

        List<PostingRule> toInsert = BuildSeedRules(templates, existingKeys);
        if (toInsert.Count == 0)
        {
            _logger.LogInformation("Posting-rule seed inserted no new rules; all templates already present or skipped.");
            return 0;
        }

        _db.PostingRules.AddRange(toInsert);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Posting-rule seed inserted {Inserted} new posting rules.", toInsert.Count);
        return toInsert.Count;
    }

    private async Task<HashSet<string>> LoadExistingKeysAsync(CancellationToken cancellationToken)
    {
        List<string> keys = await _db.PostingRules
            .AsNoTracking()
            .Select(rule => rule.RuleKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new HashSet<string>(keys, StringComparer.Ordinal);
    }

    private List<PostingRule> BuildSeedRules(
        IReadOnlyList<PostingRuleTemplate> templates,
        HashSet<string> existingKeys)
    {
        List<PostingRule> rules = [];
        foreach (PostingRuleTemplate template in templates)
        {
            if (existingKeys.Contains(template.RuleKey))
            {
                continue;
            }

            if (!IsBalanceable(template))
            {
                _logger.LogWarning(
                    "Posting-rule seed skipped template {RuleKey}: {Code} (missing a debit or credit line).",
                    template.RuleKey,
                    PostingErrorCodes.POSTING_RULE_UNBALANCED);
                continue;
            }

            rules.Add(MapTemplate(template));
            existingKeys.Add(template.RuleKey);
        }

        return rules;
    }

    private static bool IsBalanceable(PostingRuleTemplate template)
    {
        bool hasDebit = template.Lines.Any(line => line.DebitOrCredit == PostingDebitOrCredit.Debit);
        bool hasCredit = template.Lines.Any(line => line.DebitOrCredit == PostingDebitOrCredit.Credit);
        return hasDebit && hasCredit;
    }

    private static PostingRule MapTemplate(PostingRuleTemplate template)
    {
        PostingRule rule = new()
        {
            RuleKey = template.RuleKey,
            Description = template.Description,
            CountryCode = template.CountryCode,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        int lineNumber = 1;
        foreach (PostingRuleLineTemplate line in template.Lines)
        {
            rule.Lines.Add(new PostingRuleLine
            {
                LineNumber = lineNumber++,
                AccountSelector = line.AccountSelector,
                DebitOrCredit = line.DebitOrCredit,
                AmountSource = line.AmountSource
            });
        }

        return rule;
    }
}
