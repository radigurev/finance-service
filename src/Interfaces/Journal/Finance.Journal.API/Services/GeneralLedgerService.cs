using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.GenericFiltering;
using Finance.GenericFiltering.Models;
using Finance.Journal.API.Interfaces;
using Finance.Journal.DBModel;
using Finance.Journal.DBModel.Models;
using Finance.ServiceModel.Journal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Finance.Journal.API.Services;

/// <summary>
/// Default <see cref="IGeneralLedgerService"/>: a read-only aggregation over journal-entry lines that have
/// been posted to the ledger (SDD-FIN-003). "On the books" means the owning entry's status is
/// <c>Posted</c> OR <c>Reversed</c>: per SDD-FIN-002 §2.6 a reversed original keeps its posted lines intact
/// and is offset by a separate <c>Posted</c> reversal entry, so including both statuses lets a reversal net
/// to zero with no special-casing. <c>Draft</c> lines are always excluded. Sums use the base-currency columns
/// only. Results are never cached (SDD-INFRA-004). Account code/name
/// are enriched via the existing <see cref="IReferenceDataReader"/> seam; an unreachable enrichment read
/// degrades to null code/name without failing the query (SDD-FIN-003 §2.5).
/// <para><b>Empty-vs-404 decision (SDD-FIN-003 §2.4, §7):</b> this implementation takes the spec's PREFERRED
/// default — an account with no posted activity returns a well-formed empty ledger with zero balances and a
/// <c>200</c>; no account-existence pre-check against the reference seam is performed, so <c>ACCOUNT_NOT_FOUND</c>
/// is never returned.</para>
/// <para><b>Account-ledger ordering (SDD-FIN-003 §2.3):</b> the in-window line list is ordered by
/// <c>EntryDate</c> ascending then the line primary key. Because <c>EntryDate</c> lives on the parent
/// <see cref="JournalEntry"/> (not on the <see cref="JournalEntryLine"/> opted into the SDD-INFRA-005 filter
/// attributes), the ordering and paging are composed explicitly here while still enforcing the SDD-INFRA-005
/// page-size cap (<see cref="QueryableFilterExtensions.MaxPageSize"/>) with <c>PAGE_SIZE_TOO_LARGE</c>.</para>
/// </summary>
public sealed class GeneralLedgerService : IGeneralLedgerService
{
    private readonly JournalDbContext _db;
    private readonly IReferenceDataReader _referenceData;
    private readonly ILogger<GeneralLedgerService> _logger;

    /// <summary>Creates a new <see cref="GeneralLedgerService"/>.</summary>
    /// <param name="db">The journal database context (read-only use).</param>
    /// <param name="referenceData">The existing reference-data seam used for account code/name enrichment.</param>
    /// <param name="logger">Structured logger for read-path diagnostics.</param>
    public GeneralLedgerService(
        JournalDbContext db,
        IReferenceDataReader referenceData,
        ILogger<GeneralLedgerService> logger)
    {
        _db = db;
        _referenceData = referenceData;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<TrialBalanceDto>> GetTrialBalanceAsync(
        DateTimeOffset asOfDate,
        DateTimeOffset? fromDate,
        CancellationToken cancellationToken)
    {
        if (fromDate.HasValue && fromDate.Value > asOfDate)
        {
            return Result<TrialBalanceDto>.Failure(
                JournalErrorCodes.INVALID_DATE_RANGE,
                "fromDate must be on or before asOfDate.");
        }

        IReadOnlyList<AccountAggregate> aggregates =
            await AggregatePostedLinesAsync(fromDate, asOfDate, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<int, AccountReference> references =
            await EnrichAsync(aggregates.Select(aggregate => aggregate.AccountId).ToArray(), cancellationToken)
                .ConfigureAwait(false);

        _logger.LogInformation(
            "Trial balance computed over {AccountCount} accounts as of {AsOfDate}.",
            aggregates.Count,
            asOfDate);

        return Result<TrialBalanceDto>.Success(BuildTrialBalance(asOfDate, fromDate, aggregates, references));
    }

    /// <inheritdoc />
    public async Task<Result<AccountLedgerDto>> GetAccountLedgerAsync(
        int accountId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        FilterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (accountId <= 0)
        {
            return Result<AccountLedgerDto>.Failure(
                JournalErrorCodes.INVALID_ACCOUNT_ID, "accountId must be a positive integer.");
        }

        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
        {
            return Result<AccountLedgerDto>.Failure(
                JournalErrorCodes.INVALID_DATE_RANGE, "fromDate must be on or before toDate.");
        }

        if (request.PageSize > QueryableFilterExtensions.MaxPageSize)
        {
            return Result<AccountLedgerDto>.Failure(
                FilterErrorCodes.PAGE_SIZE_TOO_LARGE,
                $"The requested page size {request.PageSize} exceeds the maximum of {QueryableFilterExtensions.MaxPageSize}.");
        }

        return await BuildAccountLedgerAsync(accountId, fromDate, toDate, request, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<AccountAggregate>> AggregatePostedLinesAsync(
        DateTimeOffset? fromDate,
        DateTimeOffset asOfDate,
        CancellationToken cancellationToken)
    {
        IQueryable<JournalEntryLine> lines = LedgerLines()
            .Where(line => line.JournalEntry!.EntryDate <= asOfDate);

        if (fromDate.HasValue)
        {
            lines = lines.Where(line => line.JournalEntry!.EntryDate >= fromDate.Value);
        }

        List<AccountAggregate> aggregates = await lines
            .GroupBy(line => line.AccountId)
            .Select(group => new AccountAggregate
            {
                AccountId = group.Key,
                TotalDebit = group.Sum(line => line.BaseDebitAmount),
                TotalCredit = group.Sum(line => line.BaseCreditAmount)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return aggregates;
    }

    private async Task<Result<AccountLedgerDto>> BuildAccountLedgerAsync(
        int accountId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        FilterRequest request,
        CancellationToken cancellationToken)
    {
        decimal openingBalance = await ComputeOpeningBalanceAsync(accountId, fromDate, cancellationToken)
            .ConfigureAwait(false);

        IQueryable<JournalEntryLine> inWindow = OrderedInWindowLines(accountId, fromDate, toDate);
        int totalCount = await inWindow.CountAsync(cancellationToken).ConfigureAwait(false);
        decimal closingBalance = openingBalance
            + await SumNetAsync(inWindow, cancellationToken).ConfigureAwait(false);

        int page = request.Page < 1 ? 1 : request.Page;
        int skip = (page - 1) * request.PageSize;
        decimal runningBefore = openingBalance
            + await SumNetAsync(inWindow.Take(skip), cancellationToken).ConfigureAwait(false);

        List<LedgerLineProjection> pageLines = await inWindow
            .Skip(skip)
            .Take(request.PageSize)
            .Select(line => new LedgerLineProjection
            {
                LineId = line.Id,
                EntryNumber = line.JournalEntry!.EntryNumber!,
                EntryDate = line.JournalEntry!.EntryDate,
                EntryDescription = line.JournalEntry!.Description,
                LineDescription = line.Description,
                Debit = line.BaseDebitAmount,
                Credit = line.BaseCreditAmount
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<int, AccountReference> references =
            await EnrichAsync([accountId], cancellationToken).ConfigureAwait(false);

        return Result<AccountLedgerDto>.Success(ComposeLedger(
            accountId, fromDate, toDate, openingBalance, closingBalance,
            runningBefore, pageLines, totalCount, page, request.PageSize, references));
    }

    private Task<decimal> ComputeOpeningBalanceAsync(
        int accountId,
        DateTimeOffset? fromDate,
        CancellationToken cancellationToken)
    {
        if (!fromDate.HasValue)
        {
            return Task.FromResult(0m);
        }

        IQueryable<JournalEntryLine> before = LedgerLines()
            .Where(line => line.AccountId == accountId)
            .Where(line => line.JournalEntry!.EntryDate < fromDate.Value);

        return SumNetAsync(before, cancellationToken);
    }

    private IQueryable<JournalEntryLine> OrderedInWindowLines(
        int accountId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate)
    {
        IQueryable<JournalEntryLine> lines = LedgerLines()
            .Where(line => line.AccountId == accountId);

        if (fromDate.HasValue)
        {
            lines = lines.Where(line => line.JournalEntry!.EntryDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            lines = lines.Where(line => line.JournalEntry!.EntryDate <= toDate.Value);
        }

        return lines
            .OrderBy(line => line.JournalEntry!.EntryDate)
            .ThenBy(line => line.Id);
    }

    private static async Task<decimal> SumNetAsync(
        IQueryable<JournalEntryLine> lines,
        CancellationToken cancellationToken)
    {
        decimal debit = await lines.SumAsync(line => line.BaseDebitAmount, cancellationToken).ConfigureAwait(false);
        decimal credit = await lines.SumAsync(line => line.BaseCreditAmount, cancellationToken).ConfigureAwait(false);
        return debit - credit;
    }

    private IQueryable<JournalEntryLine> LedgerLines()
    {
        return _db.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.JournalEntry!.Status == JournalEntryStatus.Posted
                || line.JournalEntry!.Status == JournalEntryStatus.Reversed);
    }

    private async Task<IReadOnlyDictionary<int, AccountReference>> EnrichAsync(
        IReadOnlyCollection<int> accountIds,
        CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
        {
            return new Dictionary<int, AccountReference>();
        }

        return await _referenceData
            .GetAccountReferencesAsync(accountIds, cancellationToken)
            .ConfigureAwait(false);
    }

    private static TrialBalanceDto BuildTrialBalance(
        DateTimeOffset asOfDate,
        DateTimeOffset? fromDate,
        IReadOnlyList<AccountAggregate> aggregates,
        IReadOnlyDictionary<int, AccountReference> references)
    {
        List<TrialBalanceRowDto> rows = [.. aggregates.Select(aggregate => BuildRow(aggregate, references))];
        rows.Sort(CompareRows);

        decimal grandDebit = rows.Sum(row => row.DebitBalance);
        decimal grandCredit = rows.Sum(row => row.CreditBalance);

        return new TrialBalanceDto
        {
            AsOfDate = asOfDate,
            FromDate = fromDate,
            Rows = rows,
            GrandTotalDebit = grandDebit,
            GrandTotalCredit = grandCredit,
            Balanced = grandDebit == grandCredit
        };
    }

    private static TrialBalanceRowDto BuildRow(
        AccountAggregate aggregate,
        IReadOnlyDictionary<int, AccountReference> references)
    {
        decimal net = aggregate.TotalDebit - aggregate.TotalCredit;
        references.TryGetValue(aggregate.AccountId, out AccountReference? reference);

        return new TrialBalanceRowDto
        {
            AccountId = aggregate.AccountId,
            AccountCode = reference?.Code,
            AccountName = reference?.Name,
            TotalDebit = aggregate.TotalDebit,
            TotalCredit = aggregate.TotalCredit,
            DebitBalance = net >= 0m ? net : 0m,
            CreditBalance = net < 0m ? -net : 0m
        };
    }

    private static int CompareRows(TrialBalanceRowDto left, TrialBalanceRowDto right)
    {
        if (left.AccountCode is not null && right.AccountCode is not null)
        {
            int byCode = string.CompareOrdinal(left.AccountCode, right.AccountCode);
            if (byCode != 0)
            {
                return byCode;
            }
        }
        else if (left.AccountCode is null && right.AccountCode is not null)
        {
            return 1;
        }
        else if (left.AccountCode is not null && right.AccountCode is null)
        {
            return -1;
        }

        return left.AccountId.CompareTo(right.AccountId);
    }

    private static AccountLedgerDto ComposeLedger(
        int accountId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        decimal openingBalance,
        decimal closingBalance,
        decimal runningBefore,
        IReadOnlyList<LedgerLineProjection> pageLines,
        int totalCount,
        int page,
        int pageSize,
        IReadOnlyDictionary<int, AccountReference> references)
    {
        List<AccountLedgerLineDto> items = new(pageLines.Count);
        decimal running = runningBefore;
        foreach (LedgerLineProjection line in pageLines)
        {
            running += line.Debit - line.Credit;
            items.Add(new AccountLedgerLineDto
            {
                LineId = line.LineId,
                EntryNumber = line.EntryNumber,
                EntryDate = line.EntryDate,
                Description = line.LineDescription ?? line.EntryDescription,
                Debit = line.Debit,
                Credit = line.Credit,
                RunningBalance = running
            });
        }

        references.TryGetValue(accountId, out AccountReference? reference);

        return new AccountLedgerDto
        {
            AccountId = accountId,
            AccountCode = reference?.Code,
            AccountName = reference?.Name,
            FromDate = fromDate,
            ToDate = toDate,
            OpeningBalance = openingBalance,
            ClosingBalance = closingBalance,
            Lines = new PagedResult<AccountLedgerLineDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            }
        };
    }
}
