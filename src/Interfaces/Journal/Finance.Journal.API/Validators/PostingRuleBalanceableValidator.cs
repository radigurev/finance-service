using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.Country.Abstractions;
using Finance.ServiceModel.Posting;

namespace Finance.Journal.API.Validators;

/// <summary>
/// Cross-aggregate validator ensuring a posting rule is structurally balanceable on create
/// (SDD-FIN-006 §3.2): its lines MUST include at least one <c>Debit</c> AND at least one <c>Credit</c>
/// line. A structurally unbalanceable rule yields <c>POSTING_RULE_UNBALANCED</c>. The per-context numeric
/// balance is the Posting Engine's concern at apply time (SDD-FIN-006 §2.4).
/// </summary>
public sealed class PostingRuleBalanceableValidator : IChainValidator<CreatePostingRuleRequest>
{
    /// <inheritdoc />
    public Task<ChainValidationResult> ValidateAsync(CreatePostingRuleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(PostingRuleStructure.ValidateBalanceable(request.Lines));
    }
}

/// <summary>
/// Shared structural-balance check reused by the create chain and the update path (SDD-FIN-006 §3.2).
/// </summary>
public static class PostingRuleStructure
{
    /// <summary>
    /// Verifies the supplied lines include at least one debit and one credit line.
    /// </summary>
    /// <param name="lines">The posting-rule lines to inspect.</param>
    /// <returns>A success result, or a <c>POSTING_RULE_UNBALANCED</c> failure.</returns>
    public static ChainValidationResult ValidateBalanceable(IReadOnlyList<CreatePostingRuleLineRequest> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        bool hasDebit = lines.Any(line => line.DebitOrCredit == PostingDebitOrCredit.Debit);
        bool hasCredit = lines.Any(line => line.DebitOrCredit == PostingDebitOrCredit.Credit);

        if (!hasDebit || !hasCredit)
        {
            return ChainValidationResult.Failure(
                PostingErrorCodes.POSTING_RULE_UNBALANCED,
                "A posting rule must include at least one debit line and one credit line.");
        }

        return ChainValidationResult.Success();
    }
}
