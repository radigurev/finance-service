using System.Net;
using System.Net.Http.Json;
using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Country.Abstractions;
using Finance.IntegrationTesting;
using Finance.Journal.API.Interfaces;
using Finance.Journal.API.Tests.Integration.TestDoubles;
using Finance.Journal.DBModel;
using Finance.Journal.DBModel.Models;
using Finance.ServiceModel.Journal;
using Finance.ServiceModel.Posting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Integration;

/// <summary>
/// Endpoint, real-SQL, and RBAC integration tests for the Posting Rules CRUD and the Posting Engine apply
/// operation (SDD-FIN-006 §6.4). Each test boots the real <c>Finance.Journal.API</c> host through
/// <see cref="FinanceApiFactory{TProgram}"/> against the shared Testcontainers infrastructure; the
/// gateway-backed <see cref="IReferenceDataReader"/> (account-selector resolution) and
/// <see cref="IPeriodReadClient"/> (period guard) are replaced with in-memory fakes so apply can
/// materialize, balance, and post a journal entry end-to-end. Tagged <c>[Category("Integration")]</c> so
/// the offline unit run skips it.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("SDD-FIN-006")]
public sealed class PostingEndpointIntegrationTests
{
    private const string ApplyPermission = "finance.posting:apply";
    private const string RuleReadPermission = "finance.posting-rule:read";
    private const string RuleWritePermission = "finance.posting-rule:write";
    private const string ReadPermission = "finance.journal:read";
    private const string BaseCurrency = "BGN";

    private FinanceApiFactory<Program> _factory = null!;
    private FakeReferenceDataReader _referenceData = null!;
    private FakePeriodReadClient _periods = null!;
    private DatabaseResetter _resetter = null!;

    /// <summary>Builds the host factory once, swapping the gateway-backed reference/period clients for fakes.</summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _referenceData = new FakeReferenceDataReader();
        _periods = new FakePeriodReadClient();

        _factory = new FinanceApiFactory<Program>(services =>
        {
            services.RemoveAll<IReferenceDataReader>();
            services.AddSingleton<IReferenceDataReader>(_referenceData);
            services.RemoveAll<IPeriodReadClient>();
            services.AddSingleton<IPeriodReadClient>(_periods);
        });

        _ = _factory.Server;
        _resetter = new DatabaseResetter(
            IntegrationTestSetup.Containers.SqlConnectionStringForDatabase("finance_journal_test"));
    }

    /// <summary>Resets DB rows and fake state, mapping the sample rule's account codes, before each test.</summary>
    [SetUp]
    public async Task SetUp()
    {
        await _resetter.ResetAsync();
        _referenceData.DefaultPostable = true;
        _referenceData.DefaultCurrencyActive = true;
        _periods.Status = FiscalPeriodStatus.Open;

        // Map the SALE_INVOICE selector codes to postable account ids.
        _referenceData.MapCodeToAccountId("411", 411);
        _referenceData.MapCodeToAccountId("701", 701);
        _referenceData.MapCodeToAccountId("4532", 4532);
    }

    /// <summary>Disposes the host factory after the fixture.</summary>
    [OneTimeTearDown]
    public async Task OneTimeTearDown() => await _factory.DisposeAsync();

    /// <summary>POST /posting/apply materializes a balanced entry, posts it, and persists a Posted entry.</summary>
    [Test]
    public async Task Apply_MaterializesBalancedEntry_AndPostsIt()
    {
        // Arrange
        await SeedSaleInvoiceRuleAsync();
        _factory.PermissionState.Grant(ApplyPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        ApplyPostingRuleRequest request = BuildApplyRequest(net: 100m, tax: 20m, gross: 120m);

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/posting/apply", request);
        JournalEntryDto? entry = await response.Content.ReadFromJsonAsync<JournalEntryDto>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(entry, Is.Not.Null);
        JournalEntry? persisted = await FindEntryAsync(entry!.Id);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Status, Is.EqualTo(JournalEntryStatus.Posted));
            Assert.That(persisted.EntryNumber, Does.StartWith("JE"));
            Assert.That(persisted.Lines, Has.Count.EqualTo(3));
            Assert.That(persisted.Lines.Sum(line => line.BaseDebitAmount), Is.EqualTo(120m));
            Assert.That(persisted.Lines.Sum(line => line.BaseCreditAmount), Is.EqualTo(120m));
        });
    }

    /// <summary>POST /posting/apply with PostImmediately=false leaves the materialized entry as a Draft.</summary>
    [Test]
    public async Task Apply_WithoutPostImmediately_LeavesDraft()
    {
        // Arrange
        await SeedSaleInvoiceRuleAsync();
        _factory.PermissionState.Grant(ApplyPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        ApplyPostingRuleRequest request = BuildApplyRequest(100m, 20m, 120m) with { PostImmediately = false };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/posting/apply", request);
        JournalEntryDto? entry = await response.Content.ReadFromJsonAsync<JournalEntryDto>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        JournalEntry? persisted = await FindEntryAsync(entry!.Id);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Status, Is.EqualTo(JournalEntryStatus.Draft));
            Assert.That(persisted.EntryNumber, Is.Null);
        });
    }

    /// <summary>POST /posting/apply returns 409 POSTING_RULE_UNBALANCED when materialized lines do not net to zero.</summary>
    [Test]
    public async Task Apply_Returns409Unbalanced_WhenAmountsDoNotNet()
    {
        // Arrange: SALE_INVOICE debits Gross, credits Net + Tax. Net + Tax != Gross here.
        await SeedSaleInvoiceRuleAsync();
        _factory.PermissionState.Grant(ApplyPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        ApplyPostingRuleRequest request = BuildApplyRequest(net: 100m, tax: 20m, gross: 200m);

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/posting/apply", request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(PostingErrorCodes.POSTING_RULE_UNBALANCED));
    }

    /// <summary>POST /posting/apply returns 404 POSTING_RULE_NOT_FOUND for an unknown rule key.</summary>
    [Test]
    public async Task Apply_Returns404_WhenRuleKeyUnknown()
    {
        // Arrange
        _factory.PermissionState.Grant(ApplyPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        ApplyPostingRuleRequest request = BuildApplyRequest(100m, 20m, 120m) with { RuleKey = "DOES_NOT_EXIST" };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/posting/apply", request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(PostingErrorCodes.POSTING_RULE_NOT_FOUND));
    }

    /// <summary>POST /posting/apply returns 404 POSTING_RULE_NOT_FOUND when the rule exists but is inactive.</summary>
    [Test]
    public async Task Apply_Returns404_WhenRuleInactive()
    {
        // Arrange
        await SeedSaleInvoiceRuleAsync(isActive: false);
        _factory.PermissionState.Grant(ApplyPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        ApplyPostingRuleRequest request = BuildApplyRequest(100m, 20m, 120m);

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/posting/apply", request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(PostingErrorCodes.POSTING_RULE_NOT_FOUND));
    }

    /// <summary>POST /posting/apply returns 400 MISSING_POSTING_AMOUNT when a referenced amount source is absent.</summary>
    [Test]
    public async Task Apply_Returns400MissingAmount_WhenRequiredSourceMissing()
    {
        // Arrange: omit Tax, which the SALE_INVOICE rule's VAT line requires.
        await SeedSaleInvoiceRuleAsync();
        _factory.PermissionState.Grant(ApplyPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        ApplyPostingRuleRequest request = new()
        {
            RuleKey = "SALE_INVOICE",
            Amounts = new Dictionary<PostingAmountSource, decimal>
            {
                [PostingAmountSource.Net] = 100m,
                [PostingAmountSource.Gross] = 120m
            },
            CurrencyCode = BaseCurrency,
            EntryDate = DateTimeOffset.UtcNow
        };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/posting/apply", request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(PostingErrorCodes.MISSING_POSTING_AMOUNT));
    }

    /// <summary>POST /posting/apply returns 422 POSTING_RULE_ACCOUNT_NOT_FOUND when a selector resolves to no account.</summary>
    [Test]
    public async Task Apply_Returns422_WhenAccountSelectorUnresolvable()
    {
        // Arrange: leave "4532" unmapped so it resolves to null.
        await SeedSaleInvoiceRuleAsync();
        _referenceData.MapCodeToAccountId("4532", null);
        _factory.PermissionState.Grant(ApplyPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        ApplyPostingRuleRequest request = BuildApplyRequest(100m, 20m, 120m);

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/posting/apply", request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(PostingErrorCodes.POSTING_RULE_ACCOUNT_NOT_FOUND));
    }

    /// <summary>POST /posting/apply returns 403 when the caller lacks finance.posting:apply.</summary>
    [Test]
    public async Task Apply_Returns403_WhenApplyPermissionMissing()
    {
        // Arrange: grant only read.
        await SeedSaleInvoiceRuleAsync();
        _factory.PermissionState.Grant(ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        ApplyPostingRuleRequest request = BuildApplyRequest(100m, 20m, 120m);

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/posting/apply", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>
    /// SDD-FIN-006 §107 regression guard: after a PUT changes a rule's lines, the NEXT apply for that
    /// RuleKey materializes the NEW lines (the write invalidated <c>finance-journal:posting-rule:*</c>,
    /// so apply resolves the fresh rule, never a stale cached copy).
    /// </summary>
    [Test]
    public async Task UpdateRule_ThenApply_ReflectsNewLines()
    {
        // Arrange: create a 2-line rule (debit 411 gross, credit 701 gross) and map both selectors.
        const string ruleKey = "CACHE_INVALIDATION_RULE";
        _referenceData.MapCodeToAccountId("701", 701);
        _referenceData.MapCodeToAccountId("702", 702);
        _factory.PermissionState.Grant(
            ApplyPermission, ReadPermission, RuleWritePermission, RuleReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();

        PostingRuleDto created = await CreateRuleAsync(client, ruleKey, creditSelector: "701");
        ApplyPostingRuleRequest applyRequest = new()
        {
            RuleKey = ruleKey,
            Amounts = new Dictionary<PostingAmountSource, decimal> { [PostingAmountSource.Gross] = 120m },
            CurrencyCode = BaseCurrency,
            EntryDate = DateTimeOffset.UtcNow
        };

        // Act: apply once (credits 701), then PUT changing the credit line to 702, then apply again.
        JournalEntryDto firstEntry = await ApplyAsync(client, applyRequest);
        await UpdateRuleCreditSelectorAsync(client, created, ruleKey, newCreditSelector: "702");
        JournalEntryDto secondEntry = await ApplyAsync(client, applyRequest);

        // Assert: the first entry credited 701; the second credited 702 (new lines, not stale cache).
        JournalEntry? firstPersisted = await FindEntryAsync(firstEntry.Id);
        JournalEntry? secondPersisted = await FindEntryAsync(secondEntry.Id);
        JournalEntryLine firstCredit = firstPersisted!.Lines.Single(line => line.BaseCreditAmount > 0m);
        JournalEntryLine secondCredit = secondPersisted!.Lines.Single(line => line.BaseCreditAmount > 0m);
        Assert.Multiple(() =>
        {
            Assert.That(firstCredit.AccountId, Is.EqualTo(701), "First apply must credit the original account.");
            Assert.That(secondCredit.AccountId, Is.EqualTo(702), "Second apply must credit the UPDATED account.");
            Assert.That(secondPersisted.Status, Is.EqualTo(JournalEntryStatus.Posted));
        });
    }

    /// <summary>POST /posting-rules returns 201 and persists the rule with its ordered lines.</summary>
    [Test]
    public async Task CreateRule_Returns201_AndPersists()
    {
        // Arrange
        _factory.PermissionState.Grant(RuleWritePermission, RuleReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        CreatePostingRuleRequest request = new()
        {
            RuleKey = "TEST_RULE",
            Description = "A test posting rule",
            Lines =
            [
                new CreatePostingRuleLineRequest
                {
                    AccountSelector = "411",
                    DebitOrCredit = PostingDebitOrCredit.Debit,
                    AmountSource = PostingAmountSource.Gross
                },
                new CreatePostingRuleLineRequest
                {
                    AccountSelector = "701",
                    DebitOrCredit = PostingDebitOrCredit.Credit,
                    AmountSource = PostingAmountSource.Gross
                }
            ]
        };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/posting-rules", request);
        PostingRuleDto? created = await response.Content.ReadFromJsonAsync<PostingRuleDto>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(created, Is.Not.Null);
        PostingRule? persisted = await FindRuleAsync(created!.Id);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.RuleKey, Is.EqualTo("TEST_RULE"));
            Assert.That(persisted.IsActive, Is.True);
            Assert.That(persisted.Lines, Has.Count.EqualTo(2));
        });
    }

    /// <summary>POST /posting-rules returns 409 DUPLICATE_POSTING_RULE_KEY for an existing key.</summary>
    [Test]
    public async Task CreateRule_Returns409_WhenDuplicateKey()
    {
        // Arrange
        await SeedSaleInvoiceRuleAsync();
        _factory.PermissionState.Grant(RuleWritePermission, RuleReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        CreatePostingRuleRequest request = new()
        {
            RuleKey = "SALE_INVOICE",
            Description = "Duplicate",
            Lines =
            [
                new CreatePostingRuleLineRequest
                {
                    AccountSelector = "411",
                    DebitOrCredit = PostingDebitOrCredit.Debit,
                    AmountSource = PostingAmountSource.Gross
                },
                new CreatePostingRuleLineRequest
                {
                    AccountSelector = "701",
                    DebitOrCredit = PostingDebitOrCredit.Credit,
                    AmountSource = PostingAmountSource.Gross
                }
            ]
        };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/posting-rules", request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(PostingErrorCodes.DUPLICATE_POSTING_RULE_KEY));
    }

    /// <summary>The posting-rules write endpoint returns 403 without finance.posting-rule:write.</summary>
    [Test]
    public async Task CreateRule_Returns403_WhenWritePermissionMissing()
    {
        // Arrange: grant only read.
        _factory.PermissionState.Grant(RuleReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        CreatePostingRuleRequest request = new()
        {
            RuleKey = "NO_PERM_RULE",
            Description = "Should be blocked",
            Lines =
            [
                new CreatePostingRuleLineRequest
                {
                    AccountSelector = "411",
                    DebitOrCredit = PostingDebitOrCredit.Debit,
                    AmountSource = PostingAmountSource.Gross
                },
                new CreatePostingRuleLineRequest
                {
                    AccountSelector = "701",
                    DebitOrCredit = PostingDebitOrCredit.Credit,
                    AmountSource = PostingAmountSource.Gross
                }
            ]
        };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/posting-rules", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>Creates a two-line gross rule (debit 411, credit the supplied selector) through the API.</summary>
    /// <param name="client">The authenticated HTTP client holding write permission.</param>
    /// <param name="ruleKey">The unique rule key to create.</param>
    /// <param name="creditSelector">The account-selector code for the credit line.</param>
    /// <returns>The created rule DTO (carrying its RowVersion for a subsequent update).</returns>
    private static async Task<PostingRuleDto> CreateRuleAsync(
        HttpClient client,
        string ruleKey,
        string creditSelector)
    {
        CreatePostingRuleRequest request = new()
        {
            RuleKey = ruleKey,
            Description = "Cache invalidation rule",
            Lines =
            [
                new CreatePostingRuleLineRequest
                {
                    AccountSelector = "411",
                    DebitOrCredit = PostingDebitOrCredit.Debit,
                    AmountSource = PostingAmountSource.Gross
                },
                new CreatePostingRuleLineRequest
                {
                    AccountSelector = creditSelector,
                    DebitOrCredit = PostingDebitOrCredit.Credit,
                    AmountSource = PostingAmountSource.Gross
                }
            ]
        };

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/posting-rules", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PostingRuleDto>())!;
    }

    /// <summary>PUTs a rule replacing its credit line's account selector under optimistic concurrency.</summary>
    /// <param name="client">The authenticated HTTP client holding write permission.</param>
    /// <param name="current">The current rule DTO carrying the RowVersion to round-trip.</param>
    /// <param name="ruleKey">The owning rule key (used to fetch the fresh RowVersion).</param>
    /// <param name="newCreditSelector">The new account-selector code for the credit line.</param>
    private async Task UpdateRuleCreditSelectorAsync(
        HttpClient client,
        PostingRuleDto current,
        string ruleKey,
        string newCreditSelector)
    {
        string rowVersion = await GetRowVersionAsync(client, current.Id);
        UpdatePostingRuleRequest request = new()
        {
            Description = current.Description,
            IsActive = true,
            RowVersion = rowVersion,
            Lines =
            [
                new CreatePostingRuleLineRequest
                {
                    AccountSelector = "411",
                    DebitOrCredit = PostingDebitOrCredit.Debit,
                    AmountSource = PostingAmountSource.Gross
                },
                new CreatePostingRuleLineRequest
                {
                    AccountSelector = newCreditSelector,
                    DebitOrCredit = PostingDebitOrCredit.Credit,
                    AmountSource = PostingAmountSource.Gross
                }
            ]
        };

        HttpResponseMessage response = await client.PutAsJsonAsync($"/api/v1/posting-rules/{current.Id}", request);
        response.EnsureSuccessStatusCode();
        _ = ruleKey;
    }

    /// <summary>Fetches the current RowVersion for a rule via GET (reflecting the latest committed write).</summary>
    /// <param name="client">The authenticated HTTP client holding read permission.</param>
    /// <param name="id">The rule identifier.</param>
    /// <returns>The base64 RowVersion token.</returns>
    private static async Task<string> GetRowVersionAsync(HttpClient client, int id)
    {
        HttpResponseMessage response = await client.GetAsync($"/api/v1/posting-rules/{id}");
        response.EnsureSuccessStatusCode();
        PostingRuleDto rule = (await response.Content.ReadFromJsonAsync<PostingRuleDto>())!;
        return rule.RowVersion;
    }

    /// <summary>Applies a posting rule and returns the materialized (posted) journal entry DTO.</summary>
    /// <param name="client">The authenticated HTTP client holding apply permission.</param>
    /// <param name="request">The apply request.</param>
    /// <returns>The posted journal entry DTO.</returns>
    private static async Task<JournalEntryDto> ApplyAsync(HttpClient client, ApplyPostingRuleRequest request)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/posting/apply", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JournalEntryDto>())!;
    }

    /// <summary>Builds an apply request for the SALE_INVOICE rule with the supplied amounts.</summary>
    /// <param name="net">The net amount.</param>
    /// <param name="tax">The tax amount.</param>
    /// <param name="gross">The gross amount.</param>
    /// <returns>The apply request.</returns>
    private static ApplyPostingRuleRequest BuildApplyRequest(decimal net, decimal tax, decimal gross) => new()
    {
        RuleKey = "SALE_INVOICE",
        Amounts = new Dictionary<PostingAmountSource, decimal>
        {
            [PostingAmountSource.Net] = net,
            [PostingAmountSource.Tax] = tax,
            [PostingAmountSource.Gross] = gross
        },
        CurrencyCode = BaseCurrency,
        EntryDate = DateTimeOffset.UtcNow
    };

    /// <summary>Seeds the SALE_INVOICE posting rule (debit 411 gross, credit 701 net, credit 4532 tax) directly via EF.</summary>
    /// <param name="isActive">Whether the seeded rule is active.</param>
    private async Task SeedSaleInvoiceRuleAsync(bool isActive = true)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        JournalDbContext db = scope.ServiceProvider.GetRequiredService<JournalDbContext>();
        PostingRule rule = new()
        {
            RuleKey = "SALE_INVOICE",
            Description = "Sale invoice",
            CountryCode = "BG",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            Lines =
            [
                new PostingRuleLine
                {
                    LineNumber = 1,
                    AccountSelector = "411",
                    DebitOrCredit = PostingDebitOrCredit.Debit,
                    AmountSource = PostingAmountSource.Gross
                },
                new PostingRuleLine
                {
                    LineNumber = 2,
                    AccountSelector = "701",
                    DebitOrCredit = PostingDebitOrCredit.Credit,
                    AmountSource = PostingAmountSource.Net
                },
                new PostingRuleLine
                {
                    LineNumber = 3,
                    AccountSelector = "4532",
                    DebitOrCredit = PostingDebitOrCredit.Credit,
                    AmountSource = PostingAmountSource.Tax
                }
            ]
        };
        db.PostingRules.Add(rule);
        await db.SaveChangesAsync();
    }

    /// <summary>Loads a journal entry with its lines, no tracking, returning null when absent.</summary>
    /// <param name="id">The entry identifier.</param>
    /// <returns>The persisted entry, or null.</returns>
    private async Task<JournalEntry?> FindEntryAsync(Guid id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        JournalDbContext db = scope.ServiceProvider.GetRequiredService<JournalDbContext>();
        return await db.JournalEntries
            .AsNoTracking()
            .Include(entry => entry.Lines)
            .SingleOrDefaultAsync(entry => entry.Id == id);
    }

    /// <summary>Loads a posting rule with its lines, no tracking, returning null when absent.</summary>
    /// <param name="id">The rule identifier.</param>
    /// <returns>The persisted rule, or null.</returns>
    private async Task<PostingRule?> FindRuleAsync(int id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        JournalDbContext db = scope.ServiceProvider.GetRequiredService<JournalDbContext>();
        return await db.PostingRules
            .AsNoTracking()
            .Include(rule => rule.Lines)
            .SingleOrDefaultAsync(rule => rule.Id == id);
    }
}
