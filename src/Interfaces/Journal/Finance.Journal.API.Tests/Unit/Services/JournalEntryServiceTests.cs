using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Audit.Models;
using Finance.Journal.API.Auditing;
using Finance.Journal.API.Tests.Builders;
using Finance.Journal.API.Tests.Fixtures;
using Finance.Journal.DBModel.Models;
using Finance.ServiceModel.Events.Journal;
using Finance.ServiceModel.Journal;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="Finance.Journal.API.Services.JournalEntryService"/> covering the
/// Draft → Posted → Reversed lifecycle: create/update/delete draft, post (gapless number, stamps, audit,
/// outbox event, status history), reverse (sign-flipped linked entry, original → Reversed, event), the
/// workflow state machine, immutability of posted entries, period-guard rejection, and optimistic
/// concurrency (SDD-FIN-002 §6.1-§6.4). Runs fully offline against a SQLite in-memory
/// <see cref="Finance.Journal.DBModel.JournalDbContext"/> with the real validation surface and workflow
/// engine plus faked sequence, audit, publish, and period-guard dependencies.
/// </summary>
[TestFixture]
[Category("SDD-FIN-002")]
public sealed class JournalEntryServiceTests
{
    private SqliteJournalDbContextScope _scope = null!;
    private JournalEntryServiceTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteJournalDbContextFactory.Create();
        _harness = JournalEntryServiceTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    // ---- Create / list / get / validation (SDD-FIN-002 §6.4) ----

    /// <summary>Creating a valid balanced entry persists it in Draft with a NULL entry number (§2.3, §6.4).</summary>
    [Test]
    public async Task CreateDraft_ValidBalancedEntry_PersistsInDraft_WithNullEntryNumber()
    {
        // Arrange
        CreateJournalEntryRequest request = CreateJournalEntryRequestBuilder.Create().Build();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.CreateDraftAsync(
            request, JournalEntryServiceTestHarness.BaseCurrencyCode, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Status, Is.EqualTo(JournalEntryStatus.Draft));
            Assert.That(result.Value.EntryNumber, Is.Null);
            Assert.That(result.Value.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(result.Value.Lines, Has.Count.EqualTo(2));
        });
    }

    /// <summary>Creating a draft freezes the base currency supplied from configuration (§2.3, §6.4).</summary>
    [Test]
    public async Task CreateDraft_SetsBaseCurrencyFromConfiguration()
    {
        // Arrange
        CreateJournalEntryRequest request = CreateJournalEntryRequestBuilder.Create().Build();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.CreateDraftAsync(
            request, JournalEntryServiceTestHarness.BaseCurrencyCode, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.That(result.Value!.BaseCurrencyCode, Is.EqualTo(JournalEntryServiceTestHarness.BaseCurrencyCode));
    }

    /// <summary>Creating a draft records an audit Create entry with a null before-snapshot (§2.3, §6.4).</summary>
    [Test]
    public async Task CreateDraft_RecordsAuditCreate()
    {
        // Arrange
        CreateJournalEntryRequest request = CreateJournalEntryRequestBuilder.Create().Build();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.CreateDraftAsync(
            request, JournalEntryServiceTestHarness.BaseCurrencyCode, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        AuditEntry recorded = _harness.RecordedAudits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(recorded.Operation, Is.EqualTo(AuditOperation.Create));
            Assert.That(recorded.EventType, Is.EqualTo(JournalAuditEventTypes.JournalEntryCreated));
            Assert.That(recorded.BeforeJson, Is.Null);
            Assert.That(_harness.PublishedEvents, Is.Empty);
        });
    }

    /// <summary>Creating an unbalanced draft is rejected before persisting (§2.3; SDD-FIN-001 §2.3).</summary>
    [Test]
    public async Task CreateDraft_UnbalancedEntry_ReturnsUnbalancedEntry_AndPersistsNothing()
    {
        // Arrange
        CreateJournalEntryRequest request = CreateJournalEntryRequestBuilder.Create()
            .WithLines(
                JournalEntryLineRequestBuilder.Create().WithAccountId(1).AsDebit(100.00m).Build(),
                JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(90.00m).Build())
            .Build();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.CreateDraftAsync(
            request, JournalEntryServiceTestHarness.BaseCurrencyCode, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.UNBALANCED_ENTRY));
            Assert.That(_scope.Context.JournalEntries.Count(), Is.Zero);
        });
    }

    /// <summary>Get for a missing entry returns JOURNAL_ENTRY_NOT_FOUND (§2.9, §6.4).</summary>
    [Test]
    public async Task Get_ReturnsNotFound_WhenEntryDoesNotExist()
    {
        // Arrange & Act
        Result<JournalEntryDto> result = await _harness.Service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.JOURNAL_ENTRY_NOT_FOUND));
        });
    }

    /// <summary>Search returns a page ordered by EntryDate descending (§2.9, §6.4).</summary>
    [Test]
    public async Task Search_ReturnsPagedResultOrderedByEntryDateDescending()
    {
        // Arrange
        await CreateDraftAsync(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await CreateDraftAsync(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        await CreateDraftAsync(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        FilterRequest request = new() { Page = 1, PageSize = 50 };

        // Act
        Result<PagedResult<JournalEntryDto>> result =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        IReadOnlyList<JournalEntryDto> items = result.Value!.Items;
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items[0].EntryDate.Month, Is.EqualTo(3));
            Assert.That(items[1].EntryDate.Month, Is.EqualTo(2));
            Assert.That(items[2].EntryDate.Month, Is.EqualTo(1));
        });
    }

    /// <summary>Search reads transactional data live from the DB — it is never served from a cache (§2.9, §6.4).</summary>
    [Test]
    public async Task Search_DoesNotCacheTransactionalData()
    {
        // Arrange — create a draft, then verify a second search sees a freshly added second draft.
        await CreateDraftAsync(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        FilterRequest request = new() { Page = 1, PageSize = 50 };
        Result<PagedResult<JournalEntryDto>> first =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Act
        await CreateDraftAsync(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        Result<PagedResult<JournalEntryDto>> second =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(first.Value!.TotalCount, Is.EqualTo(1));
            Assert.That(second.Value!.TotalCount, Is.EqualTo(2));
        });
    }

    // ---- State machine & guards (SDD-FIN-002 §6.1) ----

    /// <summary>Posting a balanced draft transitions it to Posted (§2.4, §6.1).</summary>
    [Test]
    public async Task Post_DraftEntry_TransitionsToPosted()
    {
        // Arrange
        JournalEntry draft = await PersistDraftAsync();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.PostAsync(
            draft.Id, RowVersionRequest(draft), CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.That(result.Value!.Status, Is.EqualTo(JournalEntryStatus.Posted));
    }

    /// <summary>Posting an already-posted entry returns ENTRY_NOT_DRAFT (§2.4, §2.12, §6.1).</summary>
    [Test]
    public async Task Post_NonDraftEntry_ReturnsEntryNotDraft()
    {
        // Arrange
        JournalEntry draft = await PersistDraftAsync();
        Result<JournalEntryDto> firstPost = await _harness.Service.PostAsync(
            draft.Id, RowVersionRequest(draft), CancellationToken.None);
        JournalEntry posted = await ReloadAsync(draft.Id);

        // Act
        Result<JournalEntryDto> secondPost = await _harness.Service.PostAsync(
            posted.Id, RowVersionRequest(posted), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(firstPost.IsSuccess, Is.True, firstPost.ErrorCode);
            Assert.That(secondPost.IsSuccess, Is.False);
            Assert.That(secondPost.ErrorCode, Is.EqualTo(JournalErrorCodes.ENTRY_NOT_DRAFT));
        });
    }

    /// <summary>Posting an unbalanced draft returns UNBALANCED_ENTRY and burns no number (§2.2, §6.1).</summary>
    [Test]
    public async Task Post_UnbalancedDraft_ReturnsUnbalancedEntry_NoNumberAllocated()
    {
        // Arrange — persist a balanced draft, then corrupt the credit line so the post-time re-check fails.
        JournalEntry draft = await PersistDraftAsync();
        JournalEntry tracked = await _scope.Context.JournalEntries
            .Include(entry => entry.Lines)
            .SingleAsync(entry => entry.Id == draft.Id, CancellationToken.None);
        JournalEntryLine creditLine = tracked.Lines.Single(line => line.BaseCreditAmount > 0m);
        creditLine.BaseCreditAmount = 0m;
        creditLine.CreditAmount = 0m;
        creditLine.BaseDebitAmount = 50m;
        creditLine.DebitAmount = 50m;
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        _scope.Context.ChangeTracker.Clear();
        JournalEntry corrupted = await ReloadAsync(draft.Id);

        // Act
        Result<JournalEntryDto> result = await _harness.Service.PostAsync(
            corrupted.Id, RowVersionRequest(corrupted), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.UNBALANCED_ENTRY));
            _harness.SequenceMock.Verify(
                s => s.NextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    /// <summary>Posting into a closed period returns POSTING_PERIOD_CLOSED when the guard rejects (§2.7, §6.1).</summary>
    [Test]
    public async Task Post_ClosedPeriod_ReturnsPostingPeriodClosed_WhenGuardRejects()
    {
        // Arrange
        _harness.PeriodGuardMock
            .Setup(g => g.EnsurePostableAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(JournalErrorCodes.POSTING_PERIOD_CLOSED));
        JournalEntry draft = await PersistDraftAsync();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.PostAsync(
            draft.Id, RowVersionRequest(draft), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.POSTING_PERIOD_CLOSED));
        });
    }

    /// <summary>Posting succeeds with the default always-open period guard (§2.7, §6.1).</summary>
    [Test]
    public async Task Post_WithDefaultAlwaysOpenGuard_Succeeds()
    {
        // Arrange — the harness's default period guard returns success for every date.
        JournalEntry draft = await PersistDraftAsync();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.PostAsync(
            draft.Id, RowVersionRequest(draft), CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
    }

    /// <summary>Reversing a posted entry moves the original to Reversed (§2.6, §6.1).</summary>
    [Test]
    public async Task Reverse_PostedEntry_TransitionsOriginalToReversed()
    {
        // Arrange
        JournalEntry posted = await PostedEntryAsync();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.ReverseAsync(
            posted.Id, ReverseRequest(posted, "Correcting a keying error"), CancellationToken.None);
        JournalEntry original = await ReloadAsync(posted.Id);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(original.Status, Is.EqualTo(JournalEntryStatus.Reversed));
            Assert.That(result.Value!.Status, Is.EqualTo(JournalEntryStatus.Posted));
            Assert.That(result.Value.ReversesEntryId, Is.EqualTo(posted.Id));
        });
    }

    /// <summary>Reversing a draft returns INVALID_JOURNAL_STATE_TRANSITION (§2.6, §2.12, §6.1).</summary>
    [Test]
    public async Task Reverse_DraftEntry_ReturnsInvalidJournalStateTransition()
    {
        // Arrange
        JournalEntry draft = await PersistDraftAsync();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.ReverseAsync(
            draft.Id, ReverseRequest(draft, "Should not be allowed"), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.INVALID_JOURNAL_STATE_TRANSITION));
        });
    }

    /// <summary>Reversing an already-reversed entry returns INVALID_JOURNAL_STATE_TRANSITION (§2.12, §6.1).</summary>
    [Test]
    public async Task Reverse_AlreadyReversedEntry_ReturnsInvalidJournalStateTransition()
    {
        // Arrange
        JournalEntry posted = await PostedEntryAsync();
        await _harness.Service.ReverseAsync(
            posted.Id, ReverseRequest(posted, "First reversal"), CancellationToken.None);
        JournalEntry reversed = await ReloadAsync(posted.Id);

        // Act
        Result<JournalEntryDto> result = await _harness.Service.ReverseAsync(
            reversed.Id, ReverseRequest(reversed, "Second reversal"), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.INVALID_JOURNAL_STATE_TRANSITION));
        });
    }

    /// <summary>Reversing without a reason returns REVERSAL_REASON_REQUIRED before any side effect (§2.6, §6.1).</summary>
    [Test]
    public async Task Reverse_WithoutReason_ReturnsReversalReasonRequired()
    {
        // Arrange
        JournalEntry posted = await PostedEntryAsync();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.ReverseAsync(
            posted.Id, ReverseRequest(posted, "   "), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.REVERSAL_REASON_REQUIRED));
        });
    }

    /// <summary>Draft allows only Posted, Posted allows only Reversed, Reversed is terminal (§2.1, §6.1).</summary>
    [Test]
    public async Task Workflow_DraftAllowsOnlyPosted_PostedAllowsOnlyReversed_ReversedTerminal()
    {
        // Arrange & Act — drive the full legal chain, then attempt the illegal terminal move.
        JournalEntry posted = await PostedEntryAsync();
        Result<JournalEntryDto> reversal = await _harness.Service.ReverseAsync(
            posted.Id, ReverseRequest(posted, "Terminal-state probe"), CancellationToken.None);
        JournalEntry reversed = await ReloadAsync(posted.Id);
        Result<JournalEntryDto> reReverse = await _harness.Service.ReverseAsync(
            reversed.Id, ReverseRequest(reversed, "Illegal"), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(reversal.IsSuccess, Is.True, reversal.ErrorCode);
            Assert.That(reversed.Status, Is.EqualTo(JournalEntryStatus.Reversed));
            Assert.That(reReverse.IsSuccess, Is.False);
            Assert.That(reReverse.ErrorCode, Is.EqualTo(JournalErrorCodes.INVALID_JOURNAL_STATE_TRANSITION));
        });
    }

    /// <summary>Updating a posted entry returns CANNOT_EDIT_POSTED_ENTRY (§2.5, §2.8, §6.1).</summary>
    [Test]
    public async Task Update_PostedEntry_ReturnsCannotEditPostedEntry()
    {
        // Arrange
        JournalEntry posted = await PostedEntryAsync();
        UpdateJournalEntryRequest request = UpdateRequest(posted);

        // Act
        Result<JournalEntryDto> result = await _harness.Service.UpdateDraftAsync(
            posted.Id, request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.CANNOT_EDIT_POSTED_ENTRY));
        });
    }

    /// <summary>Deleting a posted entry returns CANNOT_EDIT_POSTED_ENTRY (§2.5, §2.8, §6.1).</summary>
    [Test]
    public async Task Delete_PostedEntry_ReturnsCannotEditPostedEntry()
    {
        // Arrange
        JournalEntry posted = await PostedEntryAsync();

        // Act
        Result result = await _harness.Service.DeleteDraftAsync(posted.Id, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.CANNOT_EDIT_POSTED_ENTRY));
        });
    }

    /// <summary>Deleting a draft removes the entry (§2.5, §6.1).</summary>
    [Test]
    public async Task Delete_DraftEntry_RemovesEntry()
    {
        // Arrange
        JournalEntry draft = await PersistDraftAsync();

        // Act
        Result result = await _harness.Service.DeleteDraftAsync(draft.Id, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.That(_scope.Context.JournalEntries.Count(), Is.Zero);
    }

    // ---- Posting side effects (SDD-FIN-002 §6.2) ----

    /// <summary>Posting assigns a gapless JE number from the sequence generator (§2.4, §6.2).</summary>
    [Test]
    public async Task Post_AssignsGaplessJeNumber_FromSequenceGenerator()
    {
        // Arrange
        JournalEntry draft = await PersistDraftAsync();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.PostAsync(
            draft.Id, RowVersionRequest(draft), CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.That(result.Value!.EntryNumber, Is.EqualTo("JE-2026-000001"));
        _harness.SequenceMock.Verify(
            s => s.NextAsync("JE", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Posting stamps PostedAt and PostedBy (§2.4, §6.2).</summary>
    [Test]
    public async Task Post_StampsPostedAtAndPostedBy()
    {
        // Arrange
        JournalEntry draft = await PersistDraftAsync();

        // Act
        await _harness.Service.PostAsync(draft.Id, RowVersionRequest(draft), CancellationToken.None);
        JournalEntry posted = await ReloadAsync(draft.Id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(posted.PostedAt, Is.Not.Null);
            Assert.That(posted.PostedBy, Is.EqualTo(StubCurrentUserAccessor.TestUserId));
        });
    }

    /// <summary>Posting records an audit StateChange before publishing the outbox event (§2.4, §6.2).</summary>
    [Test]
    public async Task Post_RecordsAuditStateChange_BeforeOutboxPublish()
    {
        // Arrange
        JournalEntry draft = await PersistDraftAsync();
        int auditCountBeforeCreate = _harness.RecordedAudits.Count;

        // Act
        await _harness.Service.PostAsync(draft.Id, RowVersionRequest(draft), CancellationToken.None);

        // Assert — the post audit row is the one recorded after the create row.
        AuditEntry postAudit = _harness.RecordedAudits[auditCountBeforeCreate];
        Assert.Multiple(() =>
        {
            Assert.That(postAudit.Operation, Is.EqualTo(AuditOperation.StateChange));
            Assert.That(postAudit.EventType, Is.EqualTo(JournalAuditEventTypes.JournalEntryPosted));
            Assert.That(postAudit.BeforeJson, Is.Not.Null);
            Assert.That(_harness.PublishedEvents.Single(), Is.TypeOf<JournalEntryPostedEvent>());
        });
    }

    /// <summary>Posting publishes JournalEntryPostedEvent with the correlation id and posted lines (§2.11, §6.2).</summary>
    [Test]
    public async Task Post_PublishesJournalEntryPostedEvent_WithCorrelationIdAndLines()
    {
        // Arrange
        JournalEntry draft = await PersistDraftAsync();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.PostAsync(
            draft.Id, RowVersionRequest(draft), CancellationToken.None);

        // Assert
        JournalEntryPostedEvent published = (JournalEntryPostedEvent)_harness.PublishedEvents.Single();
        Assert.Multiple(() =>
        {
            Assert.That(published.CorrelationId, Is.EqualTo(StubCorrelationIdAccessor.CorrelationId));
            Assert.That(published.MessageId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(published.JournalEntryId, Is.EqualTo(result.Value!.Id));
            Assert.That(published.EntryNumber, Is.EqualTo("JE-2026-000001"));
            Assert.That(published.Lines, Has.Count.EqualTo(2));
        });
        _harness.PublishMock.Verify(
            p => p.Publish(It.IsAny<JournalEntryPostedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Posting appends a Draft → Posted status-history row (§2.4, §6.2).</summary>
    [Test]
    public async Task Post_AppendsStatusHistoryRow_DraftToPosted()
    {
        // Arrange
        JournalEntry draft = await PersistDraftAsync();

        // Act
        await _harness.Service.PostAsync(draft.Id, RowVersionRequest(draft), CancellationToken.None);

        // Assert
        JournalEntryStatusHistory history = await _scope.Context.JournalEntryStatusHistory
            .Where(row => row.JournalEntryId == draft.Id)
            .SingleAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(history.FromStatus, Is.EqualTo(nameof(JournalEntryStatus.Draft)));
            Assert.That(history.ToStatus, Is.EqualTo(nameof(JournalEntryStatus.Posted)));
            Assert.That(history.CorrelationId, Is.EqualTo(StubCorrelationIdAccessor.CorrelationId));
        });
    }

    /// <summary>A guard failure publishes no event and burns no number (§2.2, §6.2).</summary>
    [Test]
    public async Task Post_DoesNotPublishEvent_WhenGuardFails()
    {
        // Arrange
        _harness.PeriodGuardMock
            .Setup(g => g.EnsurePostableAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(JournalErrorCodes.POSTING_PERIOD_CLOSED));
        JournalEntry draft = await PersistDraftAsync();

        // Act
        await _harness.Service.PostAsync(draft.Id, RowVersionRequest(draft), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(_harness.PublishedEvents, Is.Empty);
            _harness.SequenceMock.Verify(
                s => s.NextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    // ---- Reversal side effects (SDD-FIN-002 §6.3) ----

    /// <summary>Reversal creates a sign-flipped new entry linked via ReversesEntryId (§2.6, §6.3).</summary>
    [Test]
    public async Task Reverse_CreatesSignFlippedNewEntry_LinkedViaReversesEntryId()
    {
        // Arrange
        JournalEntry posted = await PostedEntryAsync();
        JournalEntryLine originalDebit = posted.Lines.Single(line => line.DebitAmount > 0m);

        // Act
        Result<JournalEntryDto> result = await _harness.Service.ReverseAsync(
            posted.Id, ReverseRequest(posted, "Sign-flip check"), CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        JournalEntryLineDto reversalForSameAccount =
            result.Value!.Lines.Single(line => line.AccountId == originalDebit.AccountId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.ReversesEntryId, Is.EqualTo(posted.Id));
            Assert.That(reversalForSameAccount.CreditAmount, Is.EqualTo(originalDebit.DebitAmount));
            Assert.That(reversalForSameAccount.DebitAmount, Is.Zero);
        });
    }

    /// <summary>The reversal entry balances in base currency by construction (§2.6, §6.3).</summary>
    [Test]
    public async Task Reverse_NewEntryIsBalanced_ByConstruction()
    {
        // Arrange
        JournalEntry posted = await PostedEntryAsync();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.ReverseAsync(
            posted.Id, ReverseRequest(posted, "Balance check"), CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        decimal baseDebits = result.Value!.Lines.Sum(line => line.BaseDebitAmount);
        decimal baseCredits = result.Value.Lines.Sum(line => line.BaseCreditAmount);
        Assert.That(baseDebits, Is.EqualTo(baseCredits));
    }

    /// <summary>Reversal does not mutate the original entry's lines (§2.6, §2.8, §6.3).</summary>
    [Test]
    public async Task Reverse_DoesNotMutateOriginalLines()
    {
        // Arrange
        JournalEntry posted = await PostedEntryAsync();
        decimal originalDebitTotal = posted.Lines.Sum(line => line.DebitAmount);
        decimal originalCreditTotal = posted.Lines.Sum(line => line.CreditAmount);

        // Act
        await _harness.Service.ReverseAsync(
            posted.Id, ReverseRequest(posted, "Immutability check"), CancellationToken.None);
        JournalEntry original = await ReloadAsync(posted.Id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(original.Lines.Sum(line => line.DebitAmount), Is.EqualTo(originalDebitTotal));
            Assert.That(original.Lines.Sum(line => line.CreditAmount), Is.EqualTo(originalCreditTotal));
            Assert.That(original.EntryNumber, Is.EqualTo("JE-2026-000001"));
        });
    }

    /// <summary>The original's reversal audit StateChange carries the reason (§2.6, §6.3).</summary>
    [Test]
    public async Task Reverse_OriginalAuditStateChange_CarriesReason()
    {
        // Arrange
        JournalEntry posted = await PostedEntryAsync();
        int auditCountAfterPost = _harness.RecordedAudits.Count;

        // Act
        await _harness.Service.ReverseAsync(
            posted.Id, ReverseRequest(posted, "Duplicate posting"), CancellationToken.None);

        // Assert
        AuditEntry reversalAudit = _harness.RecordedAudits[auditCountAfterPost];
        Assert.Multiple(() =>
        {
            Assert.That(reversalAudit.Operation, Is.EqualTo(AuditOperation.StateChange));
            Assert.That(reversalAudit.EventType, Is.EqualTo(JournalAuditEventTypes.JournalEntryReversed));
            Assert.That(reversalAudit.Reason, Is.EqualTo("Duplicate posting"));
            Assert.That(reversalAudit.BeforeJson, Is.Not.Null);
        });
    }

    /// <summary>Reversal publishes JournalEntryReversedEvent with original and reversal ids (§2.11, §6.3).</summary>
    [Test]
    public async Task Reverse_PublishesJournalEntryReversedEvent_WithOriginalAndReversalIds()
    {
        // Arrange
        JournalEntry posted = await PostedEntryAsync();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.ReverseAsync(
            posted.Id, ReverseRequest(posted, "Event check"), CancellationToken.None);

        // Assert
        JournalEntryReversedEvent published =
            _harness.PublishedEvents.OfType<JournalEntryReversedEvent>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(published.OriginalJournalEntryId, Is.EqualTo(posted.Id));
            Assert.That(published.ReversalJournalEntryId, Is.EqualTo(result.Value!.Id));
            Assert.That(published.Reason, Is.EqualTo("Event check"));
            Assert.That(published.CorrelationId, Is.EqualTo(StubCorrelationIdAccessor.CorrelationId));
        });
    }

    /// <summary>Reversal allocates a fresh gapless number for the reversal entry (§2.6, §6.3).</summary>
    [Test]
    public async Task Reverse_AllocatesFreshGaplessNumber_ForReversalEntry()
    {
        // Arrange — the original took JE-2026-000001 at posting.
        JournalEntry posted = await PostedEntryAsync();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.ReverseAsync(
            posted.Id, ReverseRequest(posted, "Numbering check"), CancellationToken.None);

        // Assert
        Assert.That(result.Value!.EntryNumber, Is.EqualTo("JE-2026-000002"));
        _harness.SequenceMock.Verify(
            s => s.NextAsync("JE", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ---- Concurrency (SDD-FIN-002 §3.3) ----

    /// <summary>Updating a draft with a stale row version yields CONCURRENT_MODIFICATION (§2.5, §6.4).</summary>
    [Test]
    public async Task UpdateDraft_StaleRowVersion_ReturnsConcurrentModification()
    {
        // Arrange
        JournalEntry draft = await PersistDraftAsync();
        string staleButValid = Convert.ToBase64String(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 });
        UpdateJournalEntryRequest request = UpdateRequest(draft) with { RowVersion = staleButValid };

        // Act
        Result<JournalEntryDto> result = await _harness.Service.UpdateDraftAsync(
            draft.Id, request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(CommonErrorCodes.CONCURRENT_MODIFICATION));
        });
    }

    // ---- Helpers ----

    private async Task<JournalEntry> PersistDraftAsync()
    {
        Result<JournalEntryDto> created = await _harness.Service.CreateDraftAsync(
            CreateJournalEntryRequestBuilder.Create().Build(),
            JournalEntryServiceTestHarness.BaseCurrencyCode,
            CancellationToken.None);
        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        _scope.Context.ChangeTracker.Clear();
        return await ReloadAsync(created.Value!.Id);
    }

    private async Task<JournalEntry> PostedEntryAsync()
    {
        JournalEntry draft = await PersistDraftAsync();
        Result<JournalEntryDto> posted = await _harness.Service.PostAsync(
            draft.Id, RowVersionRequest(draft), CancellationToken.None);
        Assert.That(posted.IsSuccess, Is.True, posted.ErrorCode);
        _scope.Context.ChangeTracker.Clear();
        return await ReloadAsync(draft.Id);
    }

    private async Task CreateDraftAsync(DateTimeOffset entryDate)
    {
        Result<JournalEntryDto> created = await _harness.Service.CreateDraftAsync(
            CreateJournalEntryRequestBuilder.Create().WithEntryDate(entryDate).Build(),
            JournalEntryServiceTestHarness.BaseCurrencyCode,
            CancellationToken.None);
        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        _scope.Context.ChangeTracker.Clear();
    }

    private async Task<JournalEntry> ReloadAsync(Guid id)
    {
        return await _scope.Context.JournalEntries
            .Include(entry => entry.Lines)
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == id, CancellationToken.None);
    }

    private static PostJournalEntryRequest RowVersionRequest(JournalEntry entry)
    {
        return new PostJournalEntryRequest { RowVersion = Convert.ToBase64String(entry.RowVersion) };
    }

    private static ReverseJournalEntryRequest ReverseRequest(JournalEntry entry, string reason)
    {
        return new ReverseJournalEntryRequest
        {
            Reason = reason,
            RowVersion = Convert.ToBase64String(entry.RowVersion)
        };
    }

    private static UpdateJournalEntryRequest UpdateRequest(JournalEntry entry)
    {
        return new UpdateJournalEntryRequest
        {
            EntryDate = entry.EntryDate,
            Description = "Updated description",
            RowVersion = Convert.ToBase64String(entry.RowVersion),
            Lines =
            [
                JournalEntryLineRequestBuilder.Create().WithAccountId(1).AsDebit(100.00m).Build(),
                JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(100.00m).Build()
            ]
        };
    }
}
