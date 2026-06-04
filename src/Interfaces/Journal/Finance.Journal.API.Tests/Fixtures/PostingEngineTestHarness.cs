using Finance.Common.Enums;
using Finance.Common.Results;
using Finance.Country.Abstractions;
using Finance.Country.BG;
using Finance.Journal.API.Services;
using Finance.Journal.DBModel;
using Finance.Journal.DBModel.Models;
using Finance.ServiceModel.Journal;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Finance.Journal.API.Tests.Fixtures;

/// <summary>
/// Assembles a <see cref="PostingEngine"/> over a SQLite in-memory <see cref="JournalDbContext"/> (for rule
/// resolution) with the in-memory <see cref="FakeReferenceDataReader"/>, the real <see cref="BulgariaStrategy"/>
/// (for the base currency), and a MOCKED <see cref="Finance.Journal.API.Interfaces.IJournalEntryService"/>
/// for the SDD-FIN-006 §6.1 apply tests. The mocked JE service lets a test assert the engine builds the
/// right <see cref="CreateJournalEntryRequest"/> and delegates — the JE path's own behavior is covered by
/// the SDD-FIN-002 suite. By default <c>CreateDraftAsync</c> returns a draft and <c>PostAsync</c> a posted
/// entry, both echoing the captured create lines.
/// </summary>
public sealed class PostingEngineTestHarness
{
    /// <summary>The base currency supplied by the wired Bulgaria strategy.</summary>
    public const string BaseCurrencyCode = "BGN";

    private PostingEngineTestHarness(
        JournalDbContext db,
        PostingEngine engine,
        FakeReferenceDataReader referenceData,
        Mock<Finance.Journal.API.Interfaces.IJournalEntryService> journalServiceMock,
        List<CreateJournalEntryRequest> capturedCreateRequests,
        List<string> capturedBaseCurrencies,
        List<Guid> postedEntryIds)
    {
        Db = db;
        Engine = engine;
        ReferenceData = referenceData;
        JournalServiceMock = journalServiceMock;
        CapturedCreateRequests = capturedCreateRequests;
        CapturedBaseCurrencies = capturedBaseCurrencies;
        PostedEntryIds = postedEntryIds;
    }

    /// <summary>The SQLite-backed journal context holding the resolvable posting rules.</summary>
    public JournalDbContext Db { get; }

    /// <summary>The system under test.</summary>
    public PostingEngine Engine { get; }

    /// <summary>The in-memory account-code resolver; register codes via <see cref="FakeReferenceDataReader.RegisterAccountCode"/>.</summary>
    public FakeReferenceDataReader ReferenceData { get; }

    /// <summary>The mocked journal-entry service the engine delegates to.</summary>
    public Mock<Finance.Journal.API.Interfaces.IJournalEntryService> JournalServiceMock { get; }

    /// <summary>The create requests captured at the delegated <c>CreateDraftAsync</c> call, in order.</summary>
    public List<CreateJournalEntryRequest> CapturedCreateRequests { get; }

    /// <summary>The base currency codes passed to <c>CreateDraftAsync</c>, in order.</summary>
    public List<string> CapturedBaseCurrencies { get; }

    /// <summary>The entry ids passed to <c>PostAsync</c>, in order.</summary>
    public List<Guid> PostedEntryIds { get; }

    /// <summary>Builds a harness over the supplied SQLite-backed context with the default success behavior.</summary>
    /// <param name="db">The SQLite-backed journal context.</param>
    /// <returns>A wired harness.</returns>
    public static PostingEngineTestHarness Build(JournalDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        FakeReferenceDataReader referenceData = new();
        ICountryStrategy countryStrategy = new BulgariaStrategy();

        List<CreateJournalEntryRequest> capturedCreateRequests = [];
        List<string> capturedBaseCurrencies = [];
        List<Guid> postedEntryIds = [];

        Mock<Finance.Journal.API.Interfaces.IJournalEntryService> journalServiceMock = new();
        journalServiceMock
            .Setup(s => s.CreateDraftAsync(
                It.IsAny<CreateJournalEntryRequest>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateJournalEntryRequest, string, CancellationToken>((request, baseCurrency, _) =>
            {
                capturedCreateRequests.Add(request);
                capturedBaseCurrencies.Add(baseCurrency);
            })
            .ReturnsAsync((CreateJournalEntryRequest request, string baseCurrency, CancellationToken _) =>
                Result<JournalEntryDto>.Success(BuildEntry(request, baseCurrency, JournalEntryStatus.Draft)));

        journalServiceMock
            .Setup(s => s.PostAsync(
                It.IsAny<Guid>(),
                It.IsAny<PostJournalEntryRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, PostJournalEntryRequest, CancellationToken>((id, _, _) => postedEntryIds.Add(id))
            .ReturnsAsync((Guid id, PostJournalEntryRequest _, CancellationToken _) =>
                Result<JournalEntryDto>.Success(BuildPostedEntry(id)));

        PostingEngine engine = new(
            db,
            referenceData,
            journalServiceMock.Object,
            countryStrategy,
            NullLogger<PostingEngine>.Instance);

        return new PostingEngineTestHarness(
            db,
            engine,
            referenceData,
            journalServiceMock,
            capturedCreateRequests,
            capturedBaseCurrencies,
            postedEntryIds);
    }

    /// <summary>Persists a posting rule with ordered lines for the engine to resolve, then clears the tracker.</summary>
    /// <param name="rule">The rule to persist.</param>
    public async Task SeedRule(PostingRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        await Db.PostingRules.AddAsync(rule, CancellationToken.None);
        await Db.SaveChangesAsync(CancellationToken.None);
        Db.ChangeTracker.Clear();
    }

    private static JournalEntryDto BuildEntry(
        CreateJournalEntryRequest request,
        string baseCurrency,
        JournalEntryStatus status)
    {
        int lineNumber = 1;
        List<JournalEntryLineDto> lines = request.Lines
            .Select(line => new JournalEntryLineDto
            {
                Id = lineNumber,
                AccountId = line.AccountId,
                DebitAmount = line.DebitAmount,
                CreditAmount = line.CreditAmount,
                CurrencyCode = line.CurrencyCode,
                ExchangeRate = line.ExchangeRate,
                BaseDebitAmount = line.BaseDebitAmount,
                BaseCreditAmount = line.BaseCreditAmount,
                LineNumber = lineNumber++
            })
            .ToList();

        return new JournalEntryDto
        {
            Id = Guid.NewGuid(),
            EntryNumber = null,
            EntryDate = request.EntryDate,
            Description = request.Description,
            BaseCurrencyCode = baseCurrency,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            Lines = lines,
            RowVersion = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8])
        };
    }

    private static JournalEntryDto BuildPostedEntry(Guid id) => new()
    {
        Id = id,
        EntryNumber = "JE-2026-000001",
        EntryDate = DateTimeOffset.UtcNow,
        Description = "Posted",
        BaseCurrencyCode = BaseCurrencyCode,
        Status = JournalEntryStatus.Posted,
        CreatedAt = DateTimeOffset.UtcNow,
        PostedAt = DateTimeOffset.UtcNow,
        Lines = [],
        RowVersion = Convert.ToBase64String([8, 7, 6, 5, 4, 3, 2, 1])
    };
}
