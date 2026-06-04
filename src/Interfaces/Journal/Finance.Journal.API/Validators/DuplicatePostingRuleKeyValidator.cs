using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.Journal.DBModel;
using Finance.ServiceModel.Posting;
using Microsoft.EntityFrameworkCore;

namespace Finance.Journal.API.Validators;

/// <summary>
/// Cross-aggregate validator ensuring a posting-rule <c>RuleKey</c> is unique within the store
/// (SDD-FIN-006 §3.2). A clash yields <c>DUPLICATE_POSTING_RULE_KEY</c>.
/// </summary>
public sealed class DuplicatePostingRuleKeyValidator : IChainValidator<CreatePostingRuleRequest>
{
    private readonly JournalDbContext _db;

    /// <summary>Creates a new <see cref="DuplicatePostingRuleKeyValidator"/>.</summary>
    /// <param name="db">The journal database context.</param>
    public DuplicatePostingRuleKeyValidator(JournalDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<ChainValidationResult> ValidateAsync(CreatePostingRuleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool exists = await _db.PostingRules
            .AsNoTracking()
            .AnyAsync(rule => rule.RuleKey == request.RuleKey, ct)
            .ConfigureAwait(false);

        if (exists)
        {
            return ChainValidationResult.Failure(
                PostingErrorCodes.DUPLICATE_POSTING_RULE_KEY,
                $"A posting rule with key '{request.RuleKey}' already exists.");
        }

        return ChainValidationResult.Success();
    }
}
