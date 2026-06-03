using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.GenericFiltering;
using Finance.GenericFiltering.Models;
using Finance.Journal.API.Tests.Builders;
using Finance.Journal.API.Tests.Fixtures;
using Finance.ServiceModel.Journal;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the single-account ledger of
/// <see cref="Finance.Journal.API.Services.GeneralLedgerService"/> (SDD-FIN-003 §6.2, §6.3): the opening
/// balance (net of posted lines strictly before <c>fromDate</c>), the running balance accumulating in
/// ledger order, the closing balance, the empty-ledger 200 (not 404) for an account with no postings, the
/// exclusion of <c>Draft</c> lines, the inclusive date-window boundaries, deterministic ordering by entry
/// date then PK, the SDD-INFRA-005 page-size cap, the offsetting reversal line, and the non-positive
/// account-id rejection. Runs fully offline against a SQLite in-memory
/// <see cref="Finance.Journal.DBModel.JournalDbContext"/>.
/// </summary>
[TestFixture]
[Category("SDD-FIN-003")]
public sealed class AccountLedgerServiceTests
{
    private const int Account = 10;
    private const int Counterparty = 20;

    private static readonly DateTimeOffset From = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);

    private SqliteJournalDbContextScope _scope = null!;
    private GeneralLedgerServiceTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed GL harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteJournalDbContextFactory.Create();
        _harness = GeneralLedgerServiceTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    /// <summary>Opening balance sums net debits − credits of posted lines strictly before fromDate (§2.3, §6.2).</summary>
    [Test]
    public async Task AccountLedger_OpeningBalance_SumsPostedLinesStrictlyBeforeFromDate()
    {
        // Arrange — two prior entries net +70.00 before the window; one in-window entry.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithEntryDate(From.AddDays(-10))
                .WithDebit(Account, 100.00m).WithCredit(Counterparty, 100.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithEntryDate(From.AddDays(-5))
                .WithCredit(Account, 30.00m).WithDebit(Counterparty, 30.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithEntryDate(From.AddDays(2))
                .WithDebit(Account, 10.00m).WithCredit(Counterparty, 10.00m).Build());

        // Act
        Result<AccountLedgerDto> result = await _harness.Service.GetAccountLedgerAsync(
            Account, From, To, NewRequest(), CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.That(result.Value!.OpeningBalance, Is.EqualTo(70.00m));
    }

    /// <summary>With fromDate omitted, the opening balance is zero (§2.3, §6.2).</summary>
    [Test]
    public async Task AccountLedger_FromDateOmitted_OpeningBalanceIsZero()
    {
        // Arrange — a prior entry exists, but with no fromDate it must not seed the opening balance.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithEntryDate(From.AddDays(-10))
                .WithDebit(Account, 100.00m).WithCredit(Counterparty, 100.00m).Build());

        // Act
        Result<AccountLedgerDto> result = await _harness.Service.GetAccountLedgerAsync(
            Account, null, To, NewRequest(), CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.That(result.Value!.OpeningBalance, Is.EqualTo(0m));
    }

    /// <summary>The running balance accumulates debit − credit from the opening balance in ledger order (§2.3, §6.2).</summary>
    [Test]
    public async Task AccountLedger_RunningBalance_AccumulatesDebitMinusCreditInLedgerOrder()
    {
        // Arrange — opening 50.00 (prior), then +100, -40, +10 across in-window lines.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithEntryDate(From.AddDays(-1))
                .WithDebit(Account, 50.00m).WithCredit(Counterparty, 50.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(From.AddDays(1))
                .WithDebit(Account, 100.00m).WithCredit(Counterparty, 100.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(From.AddDays(2))
                .WithCredit(Account, 40.00m).WithDebit(Counterparty, 40.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(From.AddDays(3))
                .WithDebit(Account, 10.00m).WithCredit(Counterparty, 10.00m).Build());

        // Act
        Result<AccountLedgerDto> result = await _harness.Service.GetAccountLedgerAsync(
            Account, From, To, NewRequest(), CancellationToken.None);

        // Assert — running balances: 50+100=150, 150-40=110, 110+10=120.
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        IReadOnlyList<AccountLedgerLineDto> lines = result.Value!.Lines.Items;
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.OpeningBalance, Is.EqualTo(50.00m));
            Assert.That(lines[0].RunningBalance, Is.EqualTo(150.00m));
            Assert.That(lines[1].RunningBalance, Is.EqualTo(110.00m));
            Assert.That(lines[2].RunningBalance, Is.EqualTo(120.00m));
        });
    }

    /// <summary>Closing balance equals opening plus the net of every in-range line (§2.3, §6.2).</summary>
    [Test]
    public async Task AccountLedger_ClosingBalance_EqualsOpeningPlusInRangeNet()
    {
        // Arrange — opening 50.00, in-range net = +100 - 40 = +60 → closing 110.00.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithEntryDate(From.AddDays(-1))
                .WithDebit(Account, 50.00m).WithCredit(Counterparty, 50.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(From.AddDays(1))
                .WithDebit(Account, 100.00m).WithCredit(Counterparty, 100.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(From.AddDays(2))
                .WithCredit(Account, 40.00m).WithDebit(Counterparty, 40.00m).Build());

        // Act
        Result<AccountLedgerDto> result = await _harness.Service.GetAccountLedgerAsync(
            Account, From, To, NewRequest(), CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.OpeningBalance, Is.EqualTo(50.00m));
            Assert.That(result.Value.ClosingBalance, Is.EqualTo(110.00m));
        });
    }

    /// <summary>An account with no postings returns an empty ledger with zero balances and a success result, not a not-found (§2.4, §6.2).</summary>
    [Test]
    public async Task AccountLedger_NoPostings_ReturnsEmptyLedger_ZeroBalances_NotFound()
    {
        // Arrange — seed activity only against unrelated accounts.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithDebit(777, 100.00m).WithCredit(888, 100.00m).Build());

        // Act
        Result<AccountLedgerDto> result = await _harness.Service.GetAccountLedgerAsync(
            Account, From, To, NewRequest(), CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.OpeningBalance, Is.EqualTo(0m));
            Assert.That(result.Value.ClosingBalance, Is.EqualTo(0m));
            Assert.That(result.Value.Lines.Items, Is.Empty);
            Assert.That(result.Value.Lines.TotalCount, Is.Zero);
        });
    }

    /// <summary>Draft lines contribute to neither the opening balance, the in-range lines, nor the closing balance (§2.1, §2.8, §6.2).</summary>
    [Test]
    public async Task AccountLedger_ExcludesDraftLines()
    {
        // Arrange — a draft before the window and a draft inside the window, plus one posted in-window line.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).AsDraft()
                .WithEntryDate(From.AddDays(-5))
                .WithDebit(Account, 999.00m).WithCredit(Counterparty, 999.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).AsDraft()
                .WithEntryDate(From.AddDays(3))
                .WithDebit(Account, 555.00m).WithCredit(Counterparty, 555.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(From.AddDays(2))
                .WithDebit(Account, 20.00m).WithCredit(Counterparty, 20.00m).Build());

        // Act
        Result<AccountLedgerDto> result = await _harness.Service.GetAccountLedgerAsync(
            Account, From, To, NewRequest(), CancellationToken.None);

        // Assert — only the single posted line of 20.00 is visible; drafts contribute nothing.
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.OpeningBalance, Is.EqualTo(0m));
            Assert.That(result.Value.Lines.Items, Has.Count.EqualTo(1));
            Assert.That(result.Value.ClosingBalance, Is.EqualTo(20.00m));
        });
    }

    /// <summary>An entry on fromDate is included in the line list but excluded from the opening balance (§2.3, §2.8, §6.2).</summary>
    [Test]
    public async Task AccountLedger_EntryOnFromDate_Included_NotInOpeningBalance()
    {
        // Arrange — one entry exactly on the from boundary.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(From)
                .WithDebit(Account, 100.00m).WithCredit(Counterparty, 100.00m).Build());

        // Act
        Result<AccountLedgerDto> result = await _harness.Service.GetAccountLedgerAsync(
            Account, From, To, NewRequest(), CancellationToken.None);

        // Assert — included in the page, opening stays zero.
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.OpeningBalance, Is.EqualTo(0m));
            Assert.That(result.Value.Lines.Items, Has.Count.EqualTo(1));
            Assert.That(result.Value.Lines.Items[0].Debit, Is.EqualTo(100.00m));
        });
    }

    /// <summary>An entry on toDate is included; the upper bound is inclusive (§2.3, §2.8, §6.2).</summary>
    [Test]
    public async Task AccountLedger_EntryOnToDate_Included()
    {
        // Arrange — one entry exactly on the to boundary, one the day after.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(To)
                .WithDebit(Account, 100.00m).WithCredit(Counterparty, 100.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(To.AddDays(1))
                .WithDebit(Account, 500.00m).WithCredit(Counterparty, 500.00m).Build());

        // Act
        Result<AccountLedgerDto> result = await _harness.Service.GetAccountLedgerAsync(
            Account, From, To, NewRequest(), CancellationToken.None);

        // Assert — only the boundary entry is in range.
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Lines.Items, Has.Count.EqualTo(1));
            Assert.That(result.Value.ClosingBalance, Is.EqualTo(100.00m));
        });
    }

    /// <summary>Lines are ordered by entry date ascending then PK so the running balance reads chronologically (§2.3, §6.2).</summary>
    [Test]
    public async Task AccountLedger_LinesOrderedByEntryDateThenPk_Deterministic()
    {
        // Arrange — seed entries out of date order so ordering is observable.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(From.AddDays(5))
                .WithEntryNumber("JE-2026-000003")
                .WithDebit(Account, 30.00m).WithCredit(Counterparty, 30.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(From.AddDays(1))
                .WithEntryNumber("JE-2026-000001")
                .WithDebit(Account, 10.00m).WithCredit(Counterparty, 10.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(From.AddDays(3))
                .WithEntryNumber("JE-2026-000002")
                .WithDebit(Account, 20.00m).WithCredit(Counterparty, 20.00m).Build());

        // Act
        Result<AccountLedgerDto> result = await _harness.Service.GetAccountLedgerAsync(
            Account, From, To, NewRequest(), CancellationToken.None);

        // Assert — chronological order by entry date.
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        IReadOnlyList<DateTimeOffset> dates = result.Value!.Lines.Items.Select(line => line.EntryDate).ToList();
        Assert.That(dates, Is.Ordered.Ascending);
    }

    /// <summary>A page size above the SDD-INFRA-005 cap of 200 is rejected with PAGE_SIZE_TOO_LARGE (§2.3, §4, §6.2).</summary>
    [Test]
    public async Task AccountLedger_RespectsPageSizeCap_200()
    {
        // Arrange
        FilterRequest oversized = new() { Page = 1, PageSize = QueryableFilterExtensions.MaxPageSize + 1 };

        // Act
        Result<AccountLedgerDto> result = await _harness.Service.GetAccountLedgerAsync(
            Account, From, To, oversized, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(FilterErrorCodes.PAGE_SIZE_TOO_LARGE));
        });
    }

    /// <summary>
    /// SDD-FIN-003 §2.8: when an entry is reversed, BOTH the <c>Reversed</c> original's line and the
    /// sign-flipped <c>Posted</c> reversal line appear as separate offsetting lines in the account ledger, so
    /// the running balance returns to the opening balance. The inclusion predicate is
    /// <c>Status ∈ { Posted, Reversed }</c>, so <c>GeneralLedgerService.LedgerLines()</c> keeps the reversed
    /// original rather than dropping it (SDD-FIN-002 §2.6 leaves its lines on the books).
    /// </summary>
    [Test]
    public async Task AccountLedger_ReversalLine_AppearsAsOffsettingLine()
    {
        // Arrange — an original debit and its sign-flipped reversal credit, both in-window.
        Guid originalId = Guid.NewGuid();
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(originalId).AsReversed()
                .WithEntryDate(From.AddDays(1))
                .WithDebit(Account, 100.00m).WithCredit(Counterparty, 100.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).Reverses(originalId)
                .WithEntryDate(From.AddDays(2))
                .WithCredit(Account, 100.00m).WithDebit(Counterparty, 100.00m).Build());

        // Act
        Result<AccountLedgerDto> result = await _harness.Service.GetAccountLedgerAsync(
            Account, From, To, NewRequest(), CancellationToken.None);

        // Assert — both lines visible, net to zero, running balance returns to opening.
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        IReadOnlyList<AccountLedgerLineDto> lines = result.Value!.Lines.Items;
        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Count.EqualTo(2));
            Assert.That(lines[0].Debit, Is.EqualTo(100.00m));
            Assert.That(lines[1].Credit, Is.EqualTo(100.00m));
            Assert.That(result.Value.ClosingBalance, Is.EqualTo(0m));
        });
    }

    /// <summary>
    /// The offsetting-line mechanism the Phase-2 implementation CAN demonstrate: two independent
    /// <c>Posted</c> entries — one debit, one offsetting credit — appear as two separate ledger lines whose
    /// running balance returns to the opening balance, proving no special-casing of offsetting activity
    /// (SDD-FIN-003 §2.3, §2.8). Complements the ignored spec-truth reversal test above.
    /// </summary>
    [Test]
    public async Task AccountLedger_TwoOffsettingPostedEntries_AppearAsSeparateLines_ClosingReturnsToOpening()
    {
        // Arrange — both entries Posted and in-window: a debit then an offsetting credit.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithEntryDate(From.AddDays(1))
                .WithDebit(Account, 100.00m).WithCredit(Counterparty, 100.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithEntryDate(From.AddDays(2))
                .WithCredit(Account, 100.00m).WithDebit(Counterparty, 100.00m).Build());

        // Act
        Result<AccountLedgerDto> result = await _harness.Service.GetAccountLedgerAsync(
            Account, From, To, NewRequest(), CancellationToken.None);

        // Assert — two lines, the second offsets the first, closing returns to the zero opening.
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        IReadOnlyList<AccountLedgerLineDto> lines = result.Value!.Lines.Items;
        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Count.EqualTo(2));
            Assert.That(lines[0].Debit, Is.EqualTo(100.00m));
            Assert.That(lines[1].Credit, Is.EqualTo(100.00m));
            Assert.That(result.Value.ClosingBalance, Is.EqualTo(0m));
        });
    }

    /// <summary>A non-positive account id is rejected with INVALID_ACCOUNT_ID before any query (§4, §6.3).</summary>
    [Test]
    public async Task Validate_NonPositiveAccountId_ReturnsInvalidAccountId()
    {
        // Arrange & Act
        Result<AccountLedgerDto> result = await _harness.Service.GetAccountLedgerAsync(
            0, From, To, NewRequest(), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.INVALID_ACCOUNT_ID));
        });
    }

    /// <summary>fromDate after toDate is rejected with INVALID_DATE_RANGE before any query (§4, §6.3).</summary>
    [Test]
    public async Task Validate_FromDateAfterToDate_ReturnsInvalidDateRange()
    {
        // Arrange — from is one day after to.
        DateTimeOffset from = To.AddDays(1);

        // Act
        Result<AccountLedgerDto> result = await _harness.Service.GetAccountLedgerAsync(
            Account, from, To, NewRequest(), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.INVALID_DATE_RANGE));
        });
    }

    /// <summary>
    /// SDD-FIN-003 §2.3: <c>RunningBalance</c> is continuous ACROSS pages — the second page's first line
    /// carries the cumulative balance from the opening balance through every skipped earlier line, not a
    /// per-page reset. Verifies the <c>runningBefore = opening + Σ(skipped net)</c> composition in
    /// <c>GeneralLedgerService.BuildAccountLedgerAsync</c>.
    /// </summary>
    [Test]
    public async Task AccountLedger_SecondPage_RunningBalanceContinuesFromOpeningThroughSkippedLines()
    {
        // Arrange — opening +50.00 (prior), then in-window +100, -40, +10; page size 2 ⇒ page 2 holds only the +10 line.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithEntryDate(From.AddDays(-1))
                .WithDebit(Account, 50.00m).WithCredit(Counterparty, 50.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(From.AddDays(1))
                .WithDebit(Account, 100.00m).WithCredit(Counterparty, 100.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(From.AddDays(2))
                .WithCredit(Account, 40.00m).WithDebit(Counterparty, 40.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(From.AddDays(3))
                .WithDebit(Account, 10.00m).WithCredit(Counterparty, 10.00m).Build());
        FilterRequest secondPage = new() { Page = 2, PageSize = 2 };

        // Act
        Result<AccountLedgerDto> result = await _harness.Service.GetAccountLedgerAsync(
            Account, From, To, secondPage, CancellationToken.None);

        // Assert — single line on page 2; its running balance is 50+100-40+10 = 120; closing is also 120.
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        IReadOnlyList<AccountLedgerLineDto> lines = result.Value!.Lines.Items;
        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Count.EqualTo(1));
            Assert.That(result.Value.Lines.TotalCount, Is.EqualTo(3));
            Assert.That(lines[0].Debit, Is.EqualTo(10.00m));
            Assert.That(lines[0].RunningBalance, Is.EqualTo(120.00m));
            Assert.That(result.Value.OpeningBalance, Is.EqualTo(50.00m));
            Assert.That(result.Value.ClosingBalance, Is.EqualTo(120.00m));
        });
    }

    private static FilterRequest NewRequest()
    {
        return new FilterRequest { Page = 1, PageSize = 50 };
    }
}
