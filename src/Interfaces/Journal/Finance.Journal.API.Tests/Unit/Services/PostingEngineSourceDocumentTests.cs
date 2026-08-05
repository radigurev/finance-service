using Finance.Common.Results;
using Finance.Country.Abstractions;
using Finance.Journal.API.Consumers;
using Finance.Journal.API.Tests.Builders;
using Finance.Journal.API.Tests.Fixtures;
using Finance.ServiceModel.Journal;
using Finance.ServiceModel.Posting;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the source-document seam through
/// <see cref="Finance.Journal.API.Services.PostingEngine"/> (SDD-PAY-001 §2.5): the optional, nullable
/// <c>SourceDocumentType</c>/<c>SourceDocumentId</c> pair on <see cref="ApplyPostingRuleRequest"/> is COPIED onto
/// the delegated <see cref="CreateJournalEntryRequest"/>, so the duplicate-post backstop the document consumers
/// stamp actually reaches the entry the journal service builds.
/// <para>A manual apply supplies neither, and the engine MUST leave both <c>null</c> so a hand-posted entry never
/// claims a source document's slot in the unique filtered index.</para>
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
[Category("SDD-FIN-006")]
public sealed class PostingEngineSourceDocumentTests
{
    private static readonly DateTimeOffset EntryDate = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);

    private SqliteJournalDbContextScope _scope = null!;
    private PostingEngineTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed posting-engine harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteJournalDbContextFactory.Create();
        _harness = PostingEngineTestHarness.Build(_scope.Context);
        _harness.ReferenceData.RegisterAccountCode("503", 503);
        _harness.ReferenceData.RegisterAccountCode("411", 411);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    /// <summary>
    /// The engine copies the source-document pair onto the create request it delegates, so the pair the payment
    /// consumer supplies is what the journal service stamps on the entry (§2.5).
    /// </summary>
    [Test]
    public async Task Apply_WithSourceDocumentPair_CopiesItOntoTheDelegatedCreateRequest()
    {
        // Arrange
        Guid paymentId = Guid.NewGuid();
        await _harness.SeedRule(ReceiptRule());
        ApplyPostingRuleRequest request = ReceiptApply(250.00m) with
        {
            SourceDocumentType = JournalSourceDocumentTypes.Payment,
            SourceDocumentId = paymentId
        };

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        CreateJournalEntryRequest captured = _harness.CapturedCreateRequests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(captured.SourceDocumentType, Is.EqualTo("Payment"));
            Assert.That(captured.SourceDocumentId, Is.EqualTo(paymentId));
        });
    }

    /// <summary>
    /// A manual apply that supplies no source document leaves both columns null on the delegated create request,
    /// so a hand-posted entry stays outside the unique filtered index (§2.5).
    /// </summary>
    [Test]
    public async Task Apply_WithoutSourceDocumentPair_LeavesBothColumnsNull_OnTheDelegatedCreateRequest()
    {
        // Arrange
        await _harness.SeedRule(ReceiptRule());
        ApplyPostingRuleRequest request = ReceiptApply(250.00m);

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        CreateJournalEntryRequest captured = _harness.CapturedCreateRequests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(captured.SourceDocumentType, Is.Null);
            Assert.That(captured.SourceDocumentId, Is.Null);
        });
    }

    private static Finance.Journal.DBModel.Models.PostingRule ReceiptRule() =>
        PostingRuleBuilder.Create()
            .WithRuleKey("PAYMENT_CUSTOMER_RECEIPT")
            .WithLines(
                ("503", PostingDebitOrCredit.Debit, PostingAmountSource.Gross),
                ("411", PostingDebitOrCredit.Credit, PostingAmountSource.Gross))
            .Build();

    private static ApplyPostingRuleRequest ReceiptApply(decimal gross) => new()
    {
        RuleKey = "PAYMENT_CUSTOMER_RECEIPT",
        Amounts = new Dictionary<PostingAmountSource, decimal>
        {
            [PostingAmountSource.Gross] = gross
        },
        CurrencyCode = "BGN",
        EntryDate = EntryDate
    };
}
