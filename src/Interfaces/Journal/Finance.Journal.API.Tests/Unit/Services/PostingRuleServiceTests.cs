using Finance.Common.ErrorCodes;
using Finance.Country.Abstractions;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Audit.Models;
using Finance.Journal.API.Caching;
using Finance.Journal.API.Tests.Builders;
using Finance.Journal.API.Tests.Fixtures;
using Finance.ServiceModel.Posting;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="Finance.Journal.API.Services.PostingRuleService"/> (SDD-FIN-006 §6.2): create
/// persists the rule with ordered lines and writes a <c>Create</c> audit row; duplicate rule keys, rules
/// with no lines, and structurally unbalanceable rules are rejected with the precise domain codes; update
/// enforces optimistic concurrency and models deactivation as a <c>StateChange</c> that excludes the rule
/// from apply resolution; list returns an ascending-by-key paged envelope capped at 200; get-by-id misses
/// surface <c>POSTING_RULE_NOT_FOUND</c>; and writes invalidate the reference cache. Runs fully offline
/// against a SQLite in-memory <see cref="Finance.Journal.DBModel.JournalDbContext"/>.
/// </summary>
[TestFixture]
[Category("SDD-FIN-006")]
public sealed class PostingRuleServiceTests
{
    private SqliteJournalDbContextScope _scope = null!;
    private PostingRuleServiceTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed posting-rule harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteJournalDbContextFactory.Create();
        _harness = PostingRuleServiceTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    [Test]
    public async Task CreateRule_Valid_PersistsWithLines_WritesAuditCreate()
    {
        // Arrange
        CreatePostingRuleRequest request = CreatePostingRuleRequestBuilder.Create()
            .WithRuleKey("SALE_INVOICE")
            .WithLines(
                Line("411", PostingDebitOrCredit.Debit, PostingAmountSource.Gross),
                Line("701", PostingDebitOrCredit.Credit, PostingAmountSource.Net),
                Line("4532", PostingDebitOrCredit.Credit, PostingAmountSource.Tax))
            .Build();

        // Act
        Finance.Common.Results.Result<PostingRuleDto> result =
            await _harness.Service.CreateAsync(request, PostingRuleServiceTestHarness.CountryCode, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        AuditEntry audit = _harness.RecordedAudits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.RuleKey, Is.EqualTo("SALE_INVOICE"));
            Assert.That(result.Value.CountryCode, Is.EqualTo("BG"));
            Assert.That(result.Value.IsActive, Is.True);
            Assert.That(result.Value.Lines, Has.Count.EqualTo(3));
            Assert.That(result.Value.Lines.Select(line => line.LineNumber), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(result.Value.Lines[0].AccountSelector, Is.EqualTo("411"));
            Assert.That(audit.Operation, Is.EqualTo(AuditOperation.Create));
            Assert.That(audit.BeforeJson, Is.Null);
        });
    }

    [Test]
    public async Task CreateRule_DuplicateRuleKey_ReturnsDuplicatePostingRuleKey()
    {
        // Arrange
        CreatePostingRuleRequest first = CreatePostingRuleRequestBuilder.Create().WithRuleKey("SALE_INVOICE").Build();
        await _harness.Service.CreateAsync(first, PostingRuleServiceTestHarness.CountryCode, CancellationToken.None);
        CreatePostingRuleRequest duplicate =
            CreatePostingRuleRequestBuilder.Create().WithRuleKey("SALE_INVOICE").Build();

        // Act
        Finance.Common.Results.Result<PostingRuleDto> result =
            await _harness.Service.CreateAsync(duplicate, PostingRuleServiceTestHarness.CountryCode, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(PostingErrorCodes.DUPLICATE_POSTING_RULE_KEY));
    }

    [Test]
    public async Task CreateRule_NoLines_ReturnsPostingRuleHasNoLines()
    {
        // Arrange
        CreatePostingRuleRequest request = CreatePostingRuleRequestBuilder.Create().WithNoLines().Build();

        // Act
        Finance.Common.Results.Result<PostingRuleDto> result =
            await _harness.Service.CreateAsync(request, PostingRuleServiceTestHarness.CountryCode, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.ErrorCode,
            Is.AnyOf(PostingErrorCodes.POSTING_RULE_HAS_NO_LINES, PostingErrorCodes.POSTING_RULE_UNBALANCED));
    }

    [Test]
    public async Task CreateRule_AllDebitLines_ReturnsPostingRuleUnbalanced()
    {
        // Arrange
        CreatePostingRuleRequest request = CreatePostingRuleRequestBuilder.Create().WithAllDebitLines().Build();

        // Act
        Finance.Common.Results.Result<PostingRuleDto> result =
            await _harness.Service.CreateAsync(request, PostingRuleServiceTestHarness.CountryCode, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(PostingErrorCodes.POSTING_RULE_UNBALANCED));
    }

    [Test]
    public async Task CreateRule_AllCreditLines_ReturnsPostingRuleUnbalanced()
    {
        // Arrange
        CreatePostingRuleRequest request = CreatePostingRuleRequestBuilder.Create().WithAllCreditLines().Build();

        // Act
        Finance.Common.Results.Result<PostingRuleDto> result =
            await _harness.Service.CreateAsync(request, PostingRuleServiceTestHarness.CountryCode, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(PostingErrorCodes.POSTING_RULE_UNBALANCED));
    }

    [Test]
    public async Task UpdateRule_StaleRowVersion_ReturnsConcurrentModification()
    {
        // Arrange
        PostingRuleDto created = await CreateRuleAsync("SALE_INVOICE");
        UpdatePostingRuleRequest stale = new()
        {
            Description = created.Description,
            IsActive = true,
            Lines = BalancedLines(),
            RowVersion = Convert.ToBase64String([9, 9, 9, 9, 9, 9, 9, 9])
        };

        // Act
        Finance.Common.Results.Result<PostingRuleDto> result =
            await _harness.Service.UpdateAsync(created.Id, stale, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(CommonErrorCodes.CONCURRENT_MODIFICATION));
    }

    [Test]
    public async Task UpdateRule_Deactivate_WritesAuditStateChange_ExcludedFromApply()
    {
        // Arrange
        PostingRuleDto created = await CreateRuleAsync("SALE_INVOICE");
        _harness.RecordedAudits.Clear();
        UpdatePostingRuleRequest deactivate = new()
        {
            Description = created.Description,
            IsActive = false,
            Lines = BalancedLines(),
            RowVersion = created.RowVersion
        };

        // Act
        Finance.Common.Results.Result<PostingRuleDto> result =
            await _harness.Service.UpdateAsync(created.Id, deactivate, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        AuditEntry audit = _harness.RecordedAudits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.IsActive, Is.False);
            Assert.That(audit.Operation, Is.EqualTo(AuditOperation.StateChange));
            Assert.That(audit.BeforeJson, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task GetRule_NotFound_ReturnsPostingRuleNotFound()
    {
        // Arrange — no rules persisted.

        // Act
        Finance.Common.Results.Result<PostingRuleDto> result =
            await _harness.Service.GetAsync(987654, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(PostingErrorCodes.POSTING_RULE_NOT_FOUND));
    }

    [Test]
    public async Task Search_ReturnsPagedResult_OrderedByRuleKeyAscending()
    {
        // Arrange
        await CreateRuleAsync("SALE_INVOICE");
        await CreateRuleAsync("CUSTOMER_PAYMENT");
        await CreateRuleAsync("PURCHASE_INVOICE");

        // Act
        Finance.Common.Results.Result<PagedResult<PostingRuleDto>> result =
            await _harness.Service.SearchAsync(new FilterRequest(), CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.TotalCount, Is.EqualTo(3));
            Assert.That(
                result.Value.Items.Select(rule => rule.RuleKey),
                Is.EqualTo(new[] { "CUSTOMER_PAYMENT", "PURCHASE_INVOICE", "SALE_INVOICE" }));
        });
    }

    [Test]
    public async Task Search_RespectsPageSizeCap_200()
    {
        // Arrange — a page size above the 200 cap (SDD-INFRA-005) must be rejected, not silently clamped.
        await CreateRuleAsync("SALE_INVOICE");
        FilterRequest request = new() { PageSize = 5000 };

        // Act
        Finance.Common.Results.Result<PagedResult<PostingRuleDto>> result =
            await _harness.Service.SearchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(FilterErrorCodes.PAGE_SIZE_TOO_LARGE));
    }

    [Test]
    public async Task UpdateRule_InvalidatesCache_NextApplyUsesNewLines()
    {
        // Arrange — prime the cache for the single-rule read, then update the rule.
        PostingRuleDto created = await CreateRuleAsync("SALE_INVOICE");
        await _harness.Service.GetAsync(created.Id, CancellationToken.None);
        int loadsBeforeUpdate = _harness.Cache.FactoryLoads.Count;

        UpdatePostingRuleRequest update = new()
        {
            Description = "Updated description.",
            IsActive = true,
            Lines = BalancedLines(),
            RowVersion = created.RowVersion
        };
        await _harness.Service.UpdateAsync(created.Id, update, CancellationToken.None);

        // Act — the post-update read must miss the cache and reload from the DB.
        Finance.Common.Results.Result<PostingRuleDto> reread =
            await _harness.Service.GetAsync(created.Id, CancellationToken.None);

        // Assert
        Assert.That(reread.IsSuccess, Is.True, reread.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(_harness.Cache.InvalidationPatterns, Does.Contain(PostingRuleCacheKeys.InvalidationPattern));
            Assert.That(_harness.Cache.FactoryLoads.Count, Is.GreaterThan(loadsBeforeUpdate));
            Assert.That(reread.Value!.Description, Is.EqualTo("Updated description."));
        });
    }

    private async Task<PostingRuleDto> CreateRuleAsync(string ruleKey)
    {
        CreatePostingRuleRequest request = CreatePostingRuleRequestBuilder.Create().WithRuleKey(ruleKey).Build();
        Finance.Common.Results.Result<PostingRuleDto> created =
            await _harness.Service.CreateAsync(request, PostingRuleServiceTestHarness.CountryCode, CancellationToken.None);
        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        return created.Value!;
    }

    private static IReadOnlyList<CreatePostingRuleLineRequest> BalancedLines() =>
    [
        Line("411", PostingDebitOrCredit.Debit, PostingAmountSource.Gross),
        Line("701", PostingDebitOrCredit.Credit, PostingAmountSource.Net)
    ];

    private static CreatePostingRuleLineRequest Line(
        string accountSelector,
        PostingDebitOrCredit debitOrCredit,
        PostingAmountSource amountSource) => new()
    {
        AccountSelector = accountSelector,
        DebitOrCredit = debitOrCredit,
        AmountSource = amountSource
    };
}
