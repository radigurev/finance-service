using Finance.Common.Results;
using Finance.ServiceModel.Journal;
using Finance.ServiceModel.Posting;

namespace Finance.Journal.API.Interfaces;

/// <summary>
/// The Posting Engine (SDD-FIN-006 §2.3): turns a named posting rule plus an amount context into a
/// balanced journal entry by DELEGATING materialization, numbering, audit, posting, and the outbox to the
/// existing <see cref="IJournalEntryService"/>. It reimplements none of that and emits no new event. Every
/// method returns a <see cref="Result"/> / <see cref="Result{T}"/> (SDD-INFRA-009).
/// </summary>
public interface IPostingEngine
{
    /// <summary>
    /// Resolves the active rule by <see cref="ApplyPostingRuleRequest.RuleKey"/>, materializes balanced
    /// debit/credit lines via an enum-driven <c>AmountSource</c> mapping, runs a defensive early balance
    /// check (<c>POSTING_RULE_UNBALANCED</c> before any draft is created), then delegates to
    /// <see cref="IJournalEntryService.CreateDraftAsync"/> (and <c>PostAsync</c> when
    /// <see cref="ApplyPostingRuleRequest.PostImmediately"/> is <c>true</c>). A JE-path failure propagates
    /// as the result (SDD-FIN-006 §2.3).
    /// </summary>
    /// <param name="request">The apply request carrying the rule key, amounts, currency, date, and flags.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the created (draft or posted) entry, or a domain failure.</returns>
    Task<Result<JournalEntryDto>> ApplyAsync(ApplyPostingRuleRequest request, CancellationToken cancellationToken);
}
