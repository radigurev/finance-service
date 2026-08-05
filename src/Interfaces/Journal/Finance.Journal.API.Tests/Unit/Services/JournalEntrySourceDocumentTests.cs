using Finance.Common.Enums;
using Finance.Common.Results;
using Finance.Journal.API.Consumers;
using Finance.Journal.API.Tests.Builders;
using Finance.Journal.API.Tests.Fixtures;
using Finance.Journal.DBModel.Models;
using Finance.ServiceModel.Journal;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the duplicate-post backstop on <see cref="Finance.Journal.API.Services.JournalEntryService"/>
/// (SDD-PAY-001 §2.5): the source-document pair supplied on the create request is STAMPED on the entry, the
/// dedupe lookup returns only the <c>Posted</c> entry for the pair, and a REVERSING entry leaves both columns
/// NULL so it never claims the source document's slot in the unique filtered index.
/// <para>The reversal rule is load-bearing in the negative direction: <c>BuildReversal</c> deliberately copies
/// neither column, and "fixing" that omission would make the offsetting entry collide with — or take over — the
/// original's index slot. A legitimately reversed original also leaves the <c>Posted</c> filter, which is why the
/// lookup then reports nothing (§2.18).</para>
/// Runs fully offline against a SQLite in-memory context with the real validation surface and workflow engine.
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
[Category("SDD-FIN-002")]
public sealed class JournalEntrySourceDocumentTests
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

    /// <summary>
    /// A create request carrying the source-document pair stamps both columns on the persisted entry, which is
    /// what the DB backstop constrains (§2.5).
    /// </summary>
    [Test]
    public async Task CreateDraft_WithSourceDocumentPair_StampsBothColumnsOnTheEntry()
    {
        // Arrange
        Guid paymentId = Guid.NewGuid();
        CreateJournalEntryRequest request = SourceDocumentRequest(JournalSourceDocumentTypes.Payment, paymentId);

        // Act
        Result<JournalEntryDto> created = await _harness.Service.CreateDraftAsync(
            request, JournalEntryServiceTestHarness.BaseCurrencyCode, CancellationToken.None);

        // Assert
        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        JournalEntry persisted = await ReloadAsync(created.Value!.Id);
        Assert.Multiple(() =>
        {
            Assert.That(persisted.SourceDocumentType, Is.EqualTo("Payment"));
            Assert.That(persisted.SourceDocumentId, Is.EqualTo(paymentId));
        });
    }

    /// <summary>
    /// A manually created entry supplies no source document and leaves both columns NULL, so it stays exempt from
    /// the unique filtered index (§2.5).
    /// </summary>
    [Test]
    public async Task CreateDraft_WithoutSourceDocumentPair_LeavesBothColumnsNull()
    {
        // Arrange
        CreateJournalEntryRequest request = CreateJournalEntryRequestBuilder.Create().Build();

        // Act
        Result<JournalEntryDto> created = await _harness.Service.CreateDraftAsync(
            request, JournalEntryServiceTestHarness.BaseCurrencyCode, CancellationToken.None);

        // Assert
        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        JournalEntry persisted = await ReloadAsync(created.Value!.Id);
        Assert.Multiple(() =>
        {
            Assert.That(persisted.SourceDocumentType, Is.Null);
            Assert.That(persisted.SourceDocumentId, Is.Null);
        });
    }

    /// <summary>
    /// The dedupe lookup returns the posted entry booked for the pair — the aggregate-level guard the document
    /// consumers consult before applying a posting rule (§2.5).
    /// </summary>
    [Test]
    public async Task FindPostedBySourceDocument_PostedEntryExists_ReturnsThatEntry()
    {
        // Arrange
        Guid paymentId = Guid.NewGuid();
        JournalEntry posted = await PostedEntryForSourceDocumentAsync(
            JournalSourceDocumentTypes.Payment, paymentId);

        // Act
        JournalEntryDto? found = await _harness.Service.FindPostedBySourceDocumentAsync(
            JournalSourceDocumentTypes.Payment, paymentId, CancellationToken.None);

        // Assert
        Assert.That(found, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(found!.Id, Is.EqualTo(posted.Id));
            Assert.That(found.EntryNumber, Is.EqualTo("JE-2026-000001"));
            Assert.That(found.Status, Is.EqualTo(JournalEntryStatus.Posted));
        });
    }

    /// <summary>
    /// A DRAFT entry for the pair does not satisfy the lookup — only a <c>Posted</c> entry blocks a second post,
    /// so an in-flight draft never suppresses a legitimate posting (§2.5).
    /// </summary>
    [Test]
    public async Task FindPostedBySourceDocument_OnlyADraftExists_ReturnsNull()
    {
        // Arrange
        Guid paymentId = Guid.NewGuid();
        await _harness.Service.CreateDraftAsync(
            SourceDocumentRequest(JournalSourceDocumentTypes.Payment, paymentId),
            JournalEntryServiceTestHarness.BaseCurrencyCode,
            CancellationToken.None);
        _scope.Context.ChangeTracker.Clear();

        // Act
        JournalEntryDto? found = await _harness.Service.FindPostedBySourceDocumentAsync(
            JournalSourceDocumentTypes.Payment, paymentId, CancellationToken.None);

        // Assert
        Assert.That(found, Is.Null);
    }

    /// <summary>
    /// A different source-document TYPE with the same id is a different document — the lookup matches on both
    /// members of the pair (§2.5).
    /// </summary>
    [Test]
    public async Task FindPostedBySourceDocument_DifferentSourceDocumentType_ReturnsNull()
    {
        // Arrange
        Guid documentId = Guid.NewGuid();
        await PostedEntryForSourceDocumentAsync(JournalSourceDocumentTypes.Invoice, documentId);

        // Act
        JournalEntryDto? found = await _harness.Service.FindPostedBySourceDocumentAsync(
            JournalSourceDocumentTypes.Payment, documentId, CancellationToken.None);

        // Assert
        Assert.That(found, Is.Null);
    }

    /// <summary>
    /// A legitimately REVERSED original leaves the <c>Posted</c> filter, so the lookup reports nothing — the
    /// §2.18 edge case the payment-side <c>PAYMENT_NOT_CONFIRMED</c> guard then catches.
    /// </summary>
    [Test]
    public async Task FindPostedBySourceDocument_OriginalEntryReversed_ReturnsNull()
    {
        // Arrange
        Guid paymentId = Guid.NewGuid();
        JournalEntry posted = await PostedEntryForSourceDocumentAsync(
            JournalSourceDocumentTypes.Payment, paymentId);
        await _harness.Service.ReverseAsync(
            posted.Id, ReverseRequest(posted, "Reversed original"), CancellationToken.None);
        _scope.Context.ChangeTracker.Clear();

        // Act
        JournalEntryDto? found = await _harness.Service.FindPostedBySourceDocumentAsync(
            JournalSourceDocumentTypes.Payment, paymentId, CancellationToken.None);

        // Assert
        Assert.That(found, Is.Null);
    }

    /// <summary>
    /// The REVERSING entry leaves both source-document columns NULL while the original keeps them, so the
    /// offsetting entry never claims the source document's slot in the unique filtered index (§2.5).
    /// </summary>
    [Test]
    public async Task Reverse_ReversalEntry_LeavesSourceDocumentColumnsNull_WhileOriginalKeepsThem()
    {
        // Arrange
        Guid paymentId = Guid.NewGuid();
        JournalEntry posted = await PostedEntryForSourceDocumentAsync(
            JournalSourceDocumentTypes.Payment, paymentId);

        // Act
        Result<JournalEntryDto> reversed = await _harness.Service.ReverseAsync(
            posted.Id, ReverseRequest(posted, "Duplicate cash receipt"), CancellationToken.None);

        // Assert
        Assert.That(reversed.IsSuccess, Is.True, reversed.ErrorCode);
        JournalEntry reversalEntry = await ReloadAsync(reversed.Value!.Id);
        JournalEntry original = await ReloadAsync(posted.Id);
        Assert.Multiple(() =>
        {
            Assert.That(reversalEntry.SourceDocumentType, Is.Null);
            Assert.That(reversalEntry.SourceDocumentId, Is.Null);
            Assert.That(reversalEntry.ReversesEntryId, Is.EqualTo(posted.Id));
            Assert.That(original.SourceDocumentType, Is.EqualTo("Payment"));
            Assert.That(original.SourceDocumentId, Is.EqualTo(paymentId));
        });
    }

    private async Task<JournalEntry> PostedEntryForSourceDocumentAsync(string sourceType, Guid sourceId)
    {
        Result<JournalEntryDto> created = await _harness.Service.CreateDraftAsync(
            SourceDocumentRequest(sourceType, sourceId),
            JournalEntryServiceTestHarness.BaseCurrencyCode,
            CancellationToken.None);
        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        _scope.Context.ChangeTracker.Clear();

        JournalEntry draft = await ReloadAsync(created.Value!.Id);
        Result<JournalEntryDto> posted = await _harness.Service.PostAsync(
            draft.Id,
            new PostJournalEntryRequest { RowVersion = Convert.ToBase64String(draft.RowVersion) },
            CancellationToken.None);
        Assert.That(posted.IsSuccess, Is.True, posted.ErrorCode);
        _scope.Context.ChangeTracker.Clear();

        return await ReloadAsync(draft.Id);
    }

    private async Task<JournalEntry> ReloadAsync(Guid id)
    {
        _scope.Context.ChangeTracker.Clear();
        return await _scope.Context.JournalEntries
            .Include(entry => entry.Lines)
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == id, CancellationToken.None);
    }

    private static CreateJournalEntryRequest SourceDocumentRequest(string sourceType, Guid sourceId)
    {
        CreateJournalEntryRequest baseRequest = CreateJournalEntryRequestBuilder.Create().Build();
        return baseRequest with
        {
            SourceDocumentType = sourceType,
            SourceDocumentId = sourceId
        };
    }

    private static ReverseJournalEntryRequest ReverseRequest(JournalEntry entry, string reason) => new()
    {
        Reason = reason,
        RowVersion = Convert.ToBase64String(entry.RowVersion)
    };
}
