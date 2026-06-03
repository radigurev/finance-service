using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Journal.API.Tests.Builders;
using Finance.Journal.API.Tests.Fixtures;
using Finance.ServiceModel.Journal;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the trial-balance aggregation of
/// <see cref="Finance.Journal.API.Services.GeneralLedgerService"/> (SDD-FIN-003 §6.1, §6.3): the
/// Σdebit==Σcredit balanced invariant, per-account net column placement by sign, exclusion of <c>Draft</c>
/// entries, natural netting of a reversed original plus its reversal, base-currency-only summation, the
/// inclusive date-window boundaries, deterministic ordering by account code, account code/name enrichment,
/// graceful degradation when enrichment is unavailable, and the date-range / as-of-date validation. Runs
/// fully offline against a SQLite in-memory <see cref="Finance.Journal.DBModel.JournalDbContext"/> seeded
/// with prebuilt <c>Posted</c> / <c>Draft</c> / <c>Reversed</c> rows.
/// </summary>
[TestFixture]
[Category("SDD-FIN-003")]
public sealed class TrialBalanceServiceTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

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

    /// <summary>Multiple balanced posted entries roll up to equal grand totals with Balanced == true (§2.2, §6.1).</summary>
    [Test]
    public async Task TrialBalance_MultipleBalancedEntries_GrandTotalsMatch_BalancedTrue()
    {
        // Arrange — three balanced entries spanning four accounts.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithDebit(10, 100.00m).WithCredit(20, 100.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithDebit(30, 250.50m).WithCredit(40, 250.50m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithDebit(10, 75.25m).WithCredit(30, 75.25m).Build());

        // Act
        Result<TrialBalanceDto> result =
            await _harness.Service.GetTrialBalanceAsync(AsOf, null, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.GrandTotalDebit, Is.EqualTo(result.Value.GrandTotalCredit));
            Assert.That(result.Value.Balanced, Is.True);
        });
    }

    /// <summary>An account whose debits exceed credits lands in the debit column (§2.2, §6.1).</summary>
    [Test]
    public async Task TrialBalance_NetDebitAccount_PlacedInDebitColumn()
    {
        // Arrange — account 10 nets +60.00 debit (100 debit, 40 credit).
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithDebit(10, 100.00m).WithCredit(20, 100.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithCredit(10, 40.00m).WithDebit(20, 40.00m).Build());

        // Act
        Result<TrialBalanceDto> result =
            await _harness.Service.GetTrialBalanceAsync(AsOf, null, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        TrialBalanceRowDto row = result.Value!.Rows.Single(candidate => candidate.AccountId == 10);
        Assert.Multiple(() =>
        {
            Assert.That(row.DebitBalance, Is.EqualTo(60.00m));
            Assert.That(row.CreditBalance, Is.EqualTo(0m));
        });
    }

    /// <summary>An account whose credits exceed debits lands in the credit column (§2.2, §6.1).</summary>
    [Test]
    public async Task TrialBalance_NetCreditAccount_PlacedInCreditColumn()
    {
        // Arrange — account 20 nets -100.00 (credit-heavy).
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithDebit(10, 100.00m).WithCredit(20, 100.00m).Build());

        // Act
        Result<TrialBalanceDto> result =
            await _harness.Service.GetTrialBalanceAsync(AsOf, null, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        TrialBalanceRowDto row = result.Value!.Rows.Single(candidate => candidate.AccountId == 20);
        Assert.Multiple(() =>
        {
            Assert.That(row.CreditBalance, Is.EqualTo(100.00m));
            Assert.That(row.DebitBalance, Is.EqualTo(0m));
        });
    }

    /// <summary>Draft entries contribute to no total, row, or grand total (§2.1, §2.8, §6.1).</summary>
    [Test]
    public async Task TrialBalance_ExcludesDraftEntries_FromAllTotals()
    {
        // Arrange — one posted entry plus a large draft against a unique account.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithDebit(10, 100.00m).WithCredit(20, 100.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).AsDraft()
                .WithDebit(99, 5000.00m).WithCredit(98, 5000.00m).Build());

        // Act
        Result<TrialBalanceDto> result =
            await _harness.Service.GetTrialBalanceAsync(AsOf, null, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Rows.Any(row => row.AccountId == 99), Is.False);
            Assert.That(result.Value.Rows.Any(row => row.AccountId == 98), Is.False);
            Assert.That(result.Value.GrandTotalDebit, Is.EqualTo(100.00m));
        });
    }

    /// <summary>
    /// SDD-FIN-003 §2.1/§2.8: a <c>Reversed</c> original's lines AND its sign-flipped <c>Posted</c> reversal
    /// are both aggregated (the inclusion predicate is <c>Status ∈ { Posted, Reversed }</c>), so the account
    /// nets to zero with no special-casing. The reversed original keeps its lines on the books (SDD-FIN-002
    /// §2.6 does not mutate them); the reversal offsets it. Verifies <c>GeneralLedgerService.LedgerLines()</c>
    /// includes the reversed original rather than dropping it.
    /// </summary>
    [Test]
    public async Task TrialBalance_ReversedEntryAndReversal_NetToZero_NoSpecialCasing()
    {
        // Arrange — original (Reversed status) debits account 10; the reversal (Posted) credits it back.
        Guid originalId = Guid.NewGuid();
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(originalId).AsReversed()
                .WithDebit(10, 100.00m).WithCredit(20, 100.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).Reverses(originalId)
                .WithCredit(10, 100.00m).WithDebit(20, 100.00m).Build());

        // Act
        Result<TrialBalanceDto> result =
            await _harness.Service.GetTrialBalanceAsync(AsOf, null, CancellationToken.None);

        // Assert — account 10 appears (it had activity) but its net is zero, and the balance still balances.
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        TrialBalanceRowDto row = result.Value!.Rows.Single(candidate => candidate.AccountId == 10);
        Assert.Multiple(() =>
        {
            Assert.That(row.DebitBalance, Is.EqualTo(0m));
            Assert.That(row.CreditBalance, Is.EqualTo(0m));
            Assert.That(row.TotalDebit, Is.EqualTo(100.00m));
            Assert.That(row.TotalCredit, Is.EqualTo(100.00m));
            Assert.That(result.Value.Balanced, Is.True);
        });
    }

    /// <summary>
    /// The "nets to zero with no special-casing" mechanism the Phase-2 implementation CAN demonstrate: two
    /// independent <c>Posted</c> entries with opposite signs against the same account sum to a zero net,
    /// proving the aggregation special-cases nothing and that offsetting activity collapses naturally
    /// (SDD-FIN-003 §2.1, §2.8). Complements the ignored spec-truth reversal test above.
    /// </summary>
    [Test]
    public async Task TrialBalance_TwoOffsettingPostedEntries_AccountNetsToZero_NoSpecialCasing()
    {
        // Arrange — both entries Posted: one debits account 10, one credits it back by the same amount.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithDebit(10, 100.00m).WithCredit(20, 100.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithCredit(10, 100.00m).WithDebit(20, 100.00m).Build());

        // Act
        Result<TrialBalanceDto> result =
            await _harness.Service.GetTrialBalanceAsync(AsOf, null, CancellationToken.None);

        // Assert — account 10 had activity but nets to zero in both columns; the balance balances.
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        TrialBalanceRowDto row = result.Value!.Rows.Single(candidate => candidate.AccountId == 10);
        Assert.Multiple(() =>
        {
            Assert.That(row.TotalDebit, Is.EqualTo(100.00m));
            Assert.That(row.TotalCredit, Is.EqualTo(100.00m));
            Assert.That(row.DebitBalance, Is.EqualTo(0m));
            Assert.That(row.CreditBalance, Is.EqualTo(0m));
            Assert.That(result.Value.Balanced, Is.True);
        });
    }

    /// <summary>A multi-currency posted entry rolls up in base currency and stays balanced (§2.8, §6.1).</summary>
    [Test]
    public async Task TrialBalance_MultiCurrencyEntry_RollsUpInBaseCurrency_StaysBalanced()
    {
        // Arrange — an EUR debit and a BGN credit, both balanced to 195.58 in base currency.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithDebit(10, 195.58m, currencyCode: "EUR", transactionalAmount: 100.00m)
                .WithCredit(20, 195.58m, currencyCode: "BGN", transactionalAmount: 195.58m)
                .Build());

        // Act
        Result<TrialBalanceDto> result =
            await _harness.Service.GetTrialBalanceAsync(AsOf, null, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        TrialBalanceRowDto debitRow = result.Value!.Rows.Single(candidate => candidate.AccountId == 10);
        Assert.Multiple(() =>
        {
            Assert.That(debitRow.TotalDebit, Is.EqualTo(195.58m));
            Assert.That(result.Value.GrandTotalDebit, Is.EqualTo(result.Value.GrandTotalCredit));
            Assert.That(result.Value.Balanced, Is.True);
        });
    }

    /// <summary>An entry dated after asOfDate is excluded; the upper bound is inclusive (§2.2, §2.8, §6.1).</summary>
    [Test]
    public async Task TrialBalance_AsOfDateUpperBoundInclusive_ExcludesLaterEntries()
    {
        // Arrange — one entry exactly on the as-of boundary, one the day after.
        DateTimeOffset asOf = new(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(asOf)
                .WithDebit(10, 100.00m).WithCredit(20, 100.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithEntryDate(asOf.AddDays(1))
                .WithDebit(10, 500.00m).WithCredit(20, 500.00m).Build());

        // Act
        Result<TrialBalanceDto> result =
            await _harness.Service.GetTrialBalanceAsync(asOf, null, CancellationToken.None);

        // Assert — only the boundary entry contributes.
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        TrialBalanceRowDto row = result.Value!.Rows.Single(candidate => candidate.AccountId == 10);
        Assert.That(row.TotalDebit, Is.EqualTo(100.00m));
    }

    /// <summary>An entry dated before fromDate is excluded; the lower bound is inclusive (§2.2, §2.8, §6.1).</summary>
    [Test]
    public async Task TrialBalance_FromDateLowerBoundInclusive_ExcludesEarlierEntries()
    {
        // Arrange — one entry on the from boundary, one the day before.
        DateTimeOffset from = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid()).WithEntryDate(from)
                .WithDebit(10, 100.00m).WithCredit(20, 100.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithEntryDate(from.AddDays(-1))
                .WithDebit(10, 500.00m).WithCredit(20, 500.00m).Build());

        // Act
        Result<TrialBalanceDto> result =
            await _harness.Service.GetTrialBalanceAsync(AsOf, from, CancellationToken.None);

        // Assert — only the boundary entry contributes.
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        TrialBalanceRowDto row = result.Value!.Rows.Single(candidate => candidate.AccountId == 10);
        Assert.That(row.TotalDebit, Is.EqualTo(100.00m));
    }

    /// <summary>With fromDate omitted, the balance is cumulative from the beginning up to asOfDate (§2.2, §6.1).</summary>
    [Test]
    public async Task TrialBalance_FromDateOmitted_AggregatesFromBeginningToAsOf()
    {
        // Arrange — two entries years apart, both before the as-of date.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithEntryDate(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero))
                .WithDebit(10, 100.00m).WithCredit(20, 100.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithEntryDate(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
                .WithDebit(10, 250.00m).WithCredit(20, 250.00m).Build());

        // Act
        Result<TrialBalanceDto> result =
            await _harness.Service.GetTrialBalanceAsync(AsOf, null, CancellationToken.None);

        // Assert — both entries sum into account 10.
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        TrialBalanceRowDto row = result.Value!.Rows.Single(candidate => candidate.AccountId == 10);
        Assert.That(row.TotalDebit, Is.EqualTo(350.00m));
    }

    /// <summary>Rows are ordered by account code ascending so repeated calls are stable (§2.2, §6.1).</summary>
    [Test]
    public async Task TrialBalance_OrdersRowsByAccountCodeAscending_Deterministic()
    {
        // Arrange — register codes out of account-id order, so id-order != code-order.
        _harness.ReferenceData.RegisterAccountReference(10, "601", "Materials");
        _harness.ReferenceData.RegisterAccountReference(20, "101", "Capital");
        _harness.ReferenceData.RegisterAccountReference(30, "411", "Receivables");
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithDebit(10, 100.00m).WithCredit(20, 100.00m).Build(),
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithDebit(30, 50.00m).WithCredit(20, 50.00m).Build());

        // Act
        Result<TrialBalanceDto> result =
            await _harness.Service.GetTrialBalanceAsync(AsOf, null, CancellationToken.None);

        // Assert — codes ascend: 101, 411, 601.
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        IReadOnlyList<string?> codes = result.Value!.Rows.Select(row => row.AccountCode).ToList();
        Assert.That(codes, Is.EqualTo(new[] { "101", "411", "601" }));
    }

    /// <summary>Only base-currency amounts are summed; deliberately divergent transactional amounts are ignored (§2.1, §6.1).</summary>
    [Test]
    public async Task TrialBalance_OnlyBaseAmountsSummed_TransactionalAmountsIgnored()
    {
        // Arrange — base debit 100.00 but a wildly different transactional amount of 9999.99.
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithDebit(10, 100.00m, currencyCode: "USD", transactionalAmount: 9999.99m)
                .WithCredit(20, 100.00m, currencyCode: "USD", transactionalAmount: 9999.99m)
                .Build());

        // Act
        Result<TrialBalanceDto> result =
            await _harness.Service.GetTrialBalanceAsync(AsOf, null, CancellationToken.None);

        // Assert — totals reflect the base amount only.
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        TrialBalanceRowDto row = result.Value!.Rows.Single(candidate => candidate.AccountId == 10);
        Assert.That(row.TotalDebit, Is.EqualTo(100.00m));
    }

    // ---- Enrichment & validation (SDD-FIN-003 §6.3) ----

    /// <summary>Account code/name are populated from the reference reader (§2.5, §6.3).</summary>
    [Test]
    public async Task Enrichment_PopulatesAccountCodeAndName_FromReferenceDataReader()
    {
        // Arrange
        _harness.ReferenceData.RegisterAccountReference(10, "601", "Materials expense");
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithDebit(10, 100.00m).WithCredit(20, 100.00m).Build());

        // Act
        Result<TrialBalanceDto> result =
            await _harness.Service.GetTrialBalanceAsync(AsOf, null, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        TrialBalanceRowDto row = result.Value!.Rows.Single(candidate => candidate.AccountId == 10);
        Assert.Multiple(() =>
        {
            Assert.That(row.AccountCode, Is.EqualTo("601"));
            Assert.That(row.AccountName, Is.EqualTo("Materials expense"));
        });
    }

    /// <summary>When enrichment is unavailable, numeric balances still return with null code/name (§2.5, §6.3).</summary>
    [Test]
    public async Task Enrichment_ReaderUnreachable_ReturnsBalancesWithNullCodeName_NoFailure()
    {
        // Arrange — no references registered, so the fake reader resolves nothing (degraded read).
        await _harness.Seed(
            PostedEntrySeedBuilder.Create().WithId(Guid.NewGuid())
                .WithDebit(10, 100.00m).WithCredit(20, 100.00m).Build());

        // Act
        Result<TrialBalanceDto> result =
            await _harness.Service.GetTrialBalanceAsync(AsOf, null, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        TrialBalanceRowDto row = result.Value!.Rows.Single(candidate => candidate.AccountId == 10);
        Assert.Multiple(() =>
        {
            Assert.That(row.AccountCode, Is.Null);
            Assert.That(row.AccountName, Is.Null);
            Assert.That(row.TotalDebit, Is.EqualTo(100.00m));
        });
    }

    /// <summary>fromDate after asOfDate is rejected with INVALID_DATE_RANGE before any query runs (§4, §6.3).</summary>
    [Test]
    public async Task Validate_FromDateAfterAsOfDate_ReturnsInvalidDateRange()
    {
        // Arrange
        DateTimeOffset from = AsOf.AddDays(1);

        // Act
        Result<TrialBalanceDto> result =
            await _harness.Service.GetTrialBalanceAsync(AsOf, from, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.INVALID_DATE_RANGE));
        });
    }
}
