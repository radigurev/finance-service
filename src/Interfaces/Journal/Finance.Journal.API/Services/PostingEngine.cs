using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Country.Abstractions;
using Finance.Journal.API.Interfaces;
using Finance.Journal.DBModel;
using Finance.Journal.DBModel.Models;
using Finance.ServiceModel.Journal;
using Finance.ServiceModel.Posting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Finance.Journal.API.Services;

/// <summary>
/// Default <see cref="IPostingEngine"/> (SDD-FIN-006 §2.3). Resolves an active posting rule, materializes
/// balanced lines via an enum-driven <c>AmountSource</c> mapping, performs a defensive early balance
/// check, then DELEGATES to <see cref="IJournalEntryService"/> for materialization, numbering, audit,
/// posting, and the outbox. It reimplements none of that and emits no new event (SDD-FIN-006 §1, §2.6).
/// </summary>
public sealed class PostingEngine : IPostingEngine
{
    private readonly JournalDbContext _db;
    private readonly IReferenceDataReader _referenceData;
    private readonly IJournalEntryService _journalEntries;
    private readonly ICountryStrategy _countryStrategy;
    private readonly ILogger<PostingEngine> _logger;

    /// <summary>Creates a new <see cref="PostingEngine"/>.</summary>
    /// <param name="db">The journal database context (rule resolution).</param>
    /// <param name="referenceData">The seam resolving account selector codes to postable ids.</param>
    /// <param name="journalEntries">The journal-entry service the engine delegates posting to.</param>
    /// <param name="countryStrategy">The country strategy supplying the base currency code.</param>
    /// <param name="logger">Structured logger for apply diagnostics.</param>
    public PostingEngine(
        JournalDbContext db,
        IReferenceDataReader referenceData,
        IJournalEntryService journalEntries,
        ICountryStrategy countryStrategy,
        ILogger<PostingEngine> logger)
    {
        _db = db;
        _referenceData = referenceData;
        _journalEntries = journalEntries;
        _countryStrategy = countryStrategy;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<JournalEntryDto>> ApplyAsync(
        ApplyPostingRuleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        PostingRule? rule = await ResolveActiveRuleAsync(request.RuleKey, cancellationToken).ConfigureAwait(false);
        if (rule is null)
        {
            return Result<JournalEntryDto>.Failure(
                PostingErrorCodes.POSTING_RULE_NOT_FOUND,
                $"No active posting rule was found for key '{request.RuleKey}'.");
        }

        Result<List<JournalEntryLineRequest>> materialized =
            await MaterializeLinesAsync(rule, request, cancellationToken).ConfigureAwait(false);
        if (!materialized.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(materialized.ErrorCode!, materialized.Detail);
        }

        Result balanceCheck = CheckBalanced(materialized.Value!);
        if (!balanceCheck.IsSuccess)
        {
            return Result<JournalEntryDto>.Failure(balanceCheck.ErrorCode!, balanceCheck.Detail);
        }

        return await DelegateToJournalAsync(rule, request, materialized.Value!, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<PostingRule?> ResolveActiveRuleAsync(string ruleKey, CancellationToken cancellationToken)
    {
        return await _db.PostingRules
            .AsNoTracking()
            .Include(rule => rule.Lines)
            .FirstOrDefaultAsync(rule => rule.RuleKey == ruleKey && rule.IsActive, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<List<JournalEntryLineRequest>>> MaterializeLinesAsync(
        PostingRule rule,
        ApplyPostingRuleRequest request,
        CancellationToken cancellationToken)
    {
        List<JournalEntryLineRequest> lines = [];
        foreach (PostingRuleLine line in rule.Lines.OrderBy(line => line.LineNumber))
        {
            Result<decimal> amount = ResolveAmount(line, request.Amounts);
            if (!amount.IsSuccess)
            {
                return Result<List<JournalEntryLineRequest>>.Failure(amount.ErrorCode!, amount.Detail);
            }

            Result<int> accountId =
                await ResolveAccountAsync(line, request.AccountOverrides, cancellationToken).ConfigureAwait(false);
            if (!accountId.IsSuccess)
            {
                return Result<List<JournalEntryLineRequest>>.Failure(accountId.ErrorCode!, accountId.Detail);
            }

            lines.Add(BuildLine(line, accountId.Value, amount.Value, request.CurrencyCode));
        }

        return Result<List<JournalEntryLineRequest>>.Success(lines);
    }

    private static Result<decimal> ResolveAmount(
        PostingRuleLine line,
        IReadOnlyDictionary<PostingAmountSource, decimal> amounts)
    {
        if (amounts.TryGetValue(line.AmountSource, out decimal value))
        {
            return Result<decimal>.Success(value);
        }

        return Result<decimal>.Failure(
            PostingErrorCodes.MISSING_POSTING_AMOUNT,
            $"The apply context is missing an amount for source '{line.AmountSource}'.");
    }

    private async Task<Result<int>> ResolveAccountAsync(
        PostingRuleLine line,
        IReadOnlyDictionary<string, string>? overrides,
        CancellationToken cancellationToken)
    {
        string code = line.AccountSelector;
        if (overrides is not null && overrides.TryGetValue(line.AccountSelector, out string? overrideCode))
        {
            code = overrideCode;
        }

        int? accountId =
            await _referenceData.ResolveAccountIdByCodeAsync(code, cancellationToken).ConfigureAwait(false);
        if (accountId is null)
        {
            return Result<int>.Failure(
                PostingErrorCodes.POSTING_RULE_ACCOUNT_NOT_FOUND,
                $"Account selector code '{code}' resolved to no postable account.");
        }

        return Result<int>.Success(accountId.Value);
    }

    private static JournalEntryLineRequest BuildLine(
        PostingRuleLine line,
        int accountId,
        decimal amount,
        string currencyCode)
    {
        bool isDebit = line.DebitOrCredit == PostingDebitOrCredit.Debit;
        return new JournalEntryLineRequest
        {
            AccountId = accountId,
            DebitAmount = isDebit ? amount : 0m,
            CreditAmount = isDebit ? 0m : amount,
            CurrencyCode = currencyCode,
            ExchangeRate = 1.000000m,
            BaseDebitAmount = isDebit ? amount : 0m,
            BaseCreditAmount = isDebit ? 0m : amount
        };
    }

    private static Result CheckBalanced(IReadOnlyList<JournalEntryLineRequest> lines)
    {
        decimal debits = lines.Sum(line => line.BaseDebitAmount);
        decimal credits = lines.Sum(line => line.BaseCreditAmount);

        if (decimal.Round(debits, 2) != decimal.Round(credits, 2))
        {
            return Result.Failure(
                PostingErrorCodes.POSTING_RULE_UNBALANCED,
                $"Materialized lines do not net to zero (debits {debits}, credits {credits}).");
        }

        return Result.Success();
    }

    private async Task<Result<JournalEntryDto>> DelegateToJournalAsync(
        PostingRule rule,
        ApplyPostingRuleRequest request,
        IReadOnlyList<JournalEntryLineRequest> lines,
        CancellationToken cancellationToken)
    {
        CreateJournalEntryRequest createRequest = new()
        {
            EntryDate = request.EntryDate,
            Description = BuildDescription(rule, request),
            Lines = lines
        };

        Result<JournalEntryDto> draft = await _journalEntries
            .CreateDraftAsync(createRequest, _countryStrategy.BaseCurrencyCode, cancellationToken)
            .ConfigureAwait(false);
        if (!draft.IsSuccess || !request.PostImmediately)
        {
            return draft;
        }

        _logger.LogInformation(
            "Posting rule {RuleKey} produced draft {EntryId}; posting immediately.",
            rule.RuleKey,
            draft.Value!.Id);

        PostJournalEntryRequest postRequest = new() { RowVersion = draft.Value!.RowVersion };
        return await _journalEntries.PostAsync(draft.Value!.Id, postRequest, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildDescription(PostingRule rule, ApplyPostingRuleRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            return request.Description;
        }

        return $"{rule.RuleKey}: {rule.Description}";
    }
}
