using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.IntegrationTesting;
using Finance.Journal.API.Interfaces;
using Finance.Journal.API.Tests.Integration.TestDoubles;
using Finance.Journal.DBModel;
using Finance.Journal.DBModel.Models;
using Finance.ServiceModel.Journal;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Integration;

/// <summary>
/// Endpoint, real-SQL, and outbox integration tests for the journal-entry lifecycle (SDD-FIN-001 §6.5,
/// SDD-FIN-002 §6.5). Each test boots the real <c>Finance.Journal.API</c> host through
/// <see cref="FinanceApiFactory{TProgram}"/> against the shared Testcontainers SQL Server / Redis /
/// RabbitMQ infrastructure. The gateway-backed <see cref="IReferenceDataReader"/> and
/// <see cref="IPeriodReadClient"/> are replaced with in-memory fakes (the real ones fail closed against
/// the non-running gateway), so the double-entry validation chain and the posting-period guard exercise
/// real behavior end-to-end. Tagged <c>[Category("Integration")]</c> so the offline unit run skips it.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("SDD-FIN-002")]
[Category("SDD-INFRA-003")]
public sealed class JournalEndpointIntegrationTests
{
    private const string CreatePermission = "finance.journal:create";
    private const string PostPermission = "finance.journal:post";
    private const string ReversePermission = "finance.journal:reverse";
    private const string DeletePermission = "finance.journal:delete";
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

    /// <summary>Resets DB rows and fake state before each test for isolation.</summary>
    [SetUp]
    public async Task SetUp()
    {
        await _resetter.ResetAsync();
        _referenceData.DefaultPostable = true;
        _referenceData.DefaultCurrencyActive = true;
        _periods.Status = FiscalPeriodStatus.Open;
    }

    /// <summary>Disposes the host factory after the fixture.</summary>
    [OneTimeTearDown]
    public async Task OneTimeTearDown() => await _factory.DisposeAsync();

    /// <summary>POST creates a balanced draft, returns 201, and persists it as Draft with no entry number.</summary>
    [Test]
    public async Task Create_Returns201_AndPersistsDraft()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        CreateJournalEntryRequest request = BuildBalancedCreateRequest();

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/journal-entries", request);
        JournalEntryDto? created = await response.Content.ReadFromJsonAsync<JournalEntryDto>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(created, Is.Not.Null);
        JournalEntry? persisted = await FindEntryAsync(created!.Id);
        Assert.That(persisted, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Status, Is.EqualTo(JournalEntryStatus.Draft));
            Assert.That(persisted.EntryNumber, Is.Null);
            Assert.That(persisted.Lines, Has.Count.EqualTo(2));
            Assert.That(persisted.BaseCurrencyCode, Is.EqualTo(BaseCurrency));
        });
    }

    /// <summary>POST returns 400 UNBALANCED_ENTRY when base-currency debits do not equal credits.</summary>
    [Test]
    public async Task Create_Returns400UnbalancedEntry_WhenLinesDoNotBalance()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        CreateJournalEntryRequest request = new()
        {
            EntryDate = DateTimeOffset.UtcNow,
            Description = "Unbalanced",
            Lines =
            [
                BuildLine(1001, debit: 100m, credit: 0m),
                BuildLine(1002, debit: 0m, credit: 90m)
            ]
        };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/journal-entries", request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(JournalErrorCodes.UNBALANCED_ENTRY));
    }

    /// <summary>POST returns a 400 VALIDATION_FAILED when fewer than two lines are supplied (shape rule).</summary>
    [Test]
    public async Task Create_Returns400ValidationFailed_WhenFewerThanTwoLines()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        CreateJournalEntryRequest request = new()
        {
            EntryDate = DateTimeOffset.UtcNow,
            Description = "Single line",
            Lines = [BuildLine(1001, debit: 100m, credit: 0m)]
        };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/journal-entries", request);
        ValidationProblemDetails? problem =
            await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(CommonErrorCodes.VALIDATION_FAILED));
        Assert.That(problem.Errors.Keys, Has.Some.EqualTo("Lines"));
    }

    /// <summary>Posting a draft assigns a gapless JE number, flips status to Posted, and writes outbox + audit rows.</summary>
    [Test]
    public async Task Post_AssignsEntryNumber_FlipsToPosted_AndWritesOutboxAndAudit()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, PostPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        JournalEntryDto draft = await CreateDraftAsync(client);
        PostJournalEntryRequest postRequest = new() { RowVersion = draft.RowVersion };

        // Act
        HttpResponseMessage response =
            await client.PostAsJsonAsync($"/api/v1/journal-entries/{draft.Id}/post", postRequest);
        JournalEntryDto? posted = await response.Content.ReadFromJsonAsync<JournalEntryDto>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(posted, Is.Not.Null);

        JournalEntry? persisted = await FindEntryAsync(draft.Id);
        int outboxCount = await CountOutboxMessagesAsync();
        bool hasPostedAudit = await HasAuditAsync(draft.Id, "JournalEntryPosted");

        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Status, Is.EqualTo(JournalEntryStatus.Posted));
            Assert.That(persisted.EntryNumber, Is.Not.Null);
            Assert.That(persisted.EntryNumber, Does.StartWith("JE"));
            Assert.That(persisted.PostedAt, Is.Not.Null);
            Assert.That(outboxCount, Is.GreaterThanOrEqualTo(1), "JournalEntryPosted outbox message must be persisted.");
            Assert.That(hasPostedAudit, Is.True, "JournalEntryPosted audit row must be written in the same transaction.");
        });
    }

    /// <summary>
    /// SDD-INFRA-003 §2.3 / SDD-FIN-002 НАП gapless guarantee: N drafts posted CONCURRENTLY each receive a
    /// distinct JE number whose numeric suffixes form a contiguous, gapless run (no gaps, no duplicates),
    /// proving the <c>UPDLOCK, HOLDLOCK</c> serialization plus the CHG-FIX-001 ambient-transaction allocation
    /// hold under load.
    /// </summary>
    [Test]
    public async Task Post_ConcurrentCallers_AllocateUniqueGaplessJeNumbers_NoGaps()
    {
        // Arrange: create N balanced drafts sequentially, capturing each id + rowVersion.
        const int callerCount = 8;
        _factory.PermissionState.Grant(CreatePermission, PostPermission, ReadPermission);
        HttpClient seedClient = _factory.CreateAuthenticatedClient();
        List<JournalEntryDto> drafts = [];
        for (int i = 0; i < callerCount; i++)
        {
            drafts.Add(await CreateDraftAsync(seedClient));
        }

        // Act: post all N concurrently, each with its own authenticated client.
        Task<HttpResponseMessage>[] posts = drafts
            .Select(draft =>
            {
                HttpClient client = _factory.CreateAuthenticatedClient();
                PostJournalEntryRequest postRequest = new() { RowVersion = draft.RowVersion };
                return client.PostAsJsonAsync($"/api/v1/journal-entries/{draft.Id}/post", postRequest);
            })
            .ToArray();
        HttpResponseMessage[] responses = await Task.WhenAll(posts);

        // Assert: every post succeeded and the assigned JE suffixes are distinct and gapless.
        Assert.That(
            responses.Select(response => response.StatusCode),
            Is.All.EqualTo(HttpStatusCode.OK),
            "Every concurrent post must succeed.");

        List<int> suffixes = [];
        foreach (HttpResponseMessage response in responses)
        {
            JournalEntryDto posted = (await response.Content.ReadFromJsonAsync<JournalEntryDto>())!;
            suffixes.Add(ExtractEntryNumberSuffix(posted.EntryNumber));
        }

        List<int> ordered = [.. suffixes.OrderBy(value => value)];
        Assert.Multiple(() =>
        {
            Assert.That(suffixes, Is.Unique, "JE numbers must be unique under concurrency (no duplicates).");
            Assert.That(suffixes, Has.Count.EqualTo(callerCount));
            Assert.That(
                ordered[^1] - ordered[0],
                Is.EqualTo(callerCount - 1),
                "The allocated JE suffixes must form a contiguous gapless run.");
        });
    }

    /// <summary>Reversing a posted entry creates a sign-flipped linked Posted entry and flips the original to Reversed.</summary>
    [Test]
    public async Task Reverse_CreatesSignFlippedLinkedEntry_AndFlipsOriginalToReversed()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, PostPermission, ReversePermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        JournalEntryDto posted = await CreateAndPostAsync(client);
        ReverseJournalEntryRequest reverseRequest = new()
        {
            Reason = "Correcting an error",
            RowVersion = posted.RowVersion
        };

        // Act
        HttpResponseMessage response =
            await client.PostAsJsonAsync($"/api/v1/journal-entries/{posted.Id}/reverse", reverseRequest);
        string body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        JournalEntryDto? reversal = JsonSerializer.Deserialize<JournalEntryDto>(
            body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.That(reversal, Is.Not.Null);

        JournalEntry? original = await FindEntryAsync(posted.Id);
        JournalEntry? reversalEntry = await FindEntryAsync(reversal!.Id);
        JournalEntryLine originalDebit = original!.Lines.Single(line => line.BaseDebitAmount > 0m);
        JournalEntryLine reversalCredit = reversalEntry!.Lines.Single(line => line.AccountId == originalDebit.AccountId);

        Assert.Multiple(() =>
        {
            Assert.That(original.Status, Is.EqualTo(JournalEntryStatus.Reversed));
            Assert.That(reversalEntry.Status, Is.EqualTo(JournalEntryStatus.Posted));
            Assert.That(reversalEntry.ReversesEntryId, Is.EqualTo(posted.Id));
            Assert.That(reversalCredit.BaseCreditAmount, Is.EqualTo(originalDebit.BaseDebitAmount));
            Assert.That(reversalCredit.BaseDebitAmount, Is.EqualTo(0m));
        });
    }

    /// <summary>Posting an already-posted entry returns 409 ENTRY_NOT_DRAFT.</summary>
    [Test]
    public async Task Post_Returns409EntryNotDraft_WhenAlreadyPosted()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, PostPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        JournalEntryDto posted = await CreateAndPostAsync(client);
        PostJournalEntryRequest postRequest = new() { RowVersion = posted.RowVersion };

        // Act
        HttpResponseMessage response =
            await client.PostAsJsonAsync($"/api/v1/journal-entries/{posted.Id}/post", postRequest);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(JournalErrorCodes.ENTRY_NOT_DRAFT));
    }

    /// <summary>Deleting a draft succeeds (200) and removes it; deleting a posted entry is blocked (409).</summary>
    [Test]
    public async Task Delete_RemovesDraft_ButBlocksPostedEntry()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, PostPermission, DeletePermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        JournalEntryDto draft = await CreateDraftAsync(client);
        JournalEntryDto posted = await CreateAndPostAsync(client);

        // Act
        HttpResponseMessage deleteDraft = await client.DeleteAsync($"/api/v1/journal-entries/{draft.Id}");
        HttpResponseMessage deletePosted = await client.DeleteAsync($"/api/v1/journal-entries/{posted.Id}");
        ProblemDetails? postedProblem = await deletePosted.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(deleteDraft.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(deletePosted.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(postedProblem!.Title, Is.EqualTo(JournalErrorCodes.CANNOT_EDIT_POSTED_ENTRY));
        });
        Assert.That(await FindEntryAsync(draft.Id), Is.Null);
    }

    /// <summary>Updating a posted entry returns 409 CANNOT_EDIT_POSTED_ENTRY (posted entries are immutable).</summary>
    [Test]
    public async Task Update_Returns409CannotEditPostedEntry_WhenEntryPosted()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, PostPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        JournalEntryDto posted = await CreateAndPostAsync(client);
        UpdateJournalEntryRequest update = new()
        {
            EntryDate = posted.EntryDate,
            Description = "Tampering",
            Lines = [BuildLine(1001, 50m, 0m), BuildLine(1002, 0m, 50m)],
            RowVersion = posted.RowVersion
        };

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync($"/api/v1/journal-entries/{posted.Id}", update);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(JournalErrorCodes.CANNOT_EDIT_POSTED_ENTRY));
    }

    /// <summary>Posting into a closed fiscal period returns 409 POSTING_PERIOD_CLOSED.</summary>
    [Test]
    public async Task Post_Returns409PostingPeriodClosed_WhenPeriodClosed()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, PostPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        JournalEntryDto draft = await CreateDraftAsync(client);
        _periods.Status = FiscalPeriodStatus.Closed;
        PostJournalEntryRequest postRequest = new() { RowVersion = draft.RowVersion };

        // Act
        HttpResponseMessage response =
            await client.PostAsJsonAsync($"/api/v1/journal-entries/{draft.Id}/post", postRequest);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(JournalErrorCodes.POSTING_PERIOD_CLOSED));
    }

    /// <summary>The post endpoint returns 403 when the caller lacks finance.journal:post.</summary>
    [Test]
    public async Task Post_Returns403_WhenPostPermissionMissing()
    {
        // Arrange: grant create+read but not post.
        _factory.PermissionState.Grant(CreatePermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        JournalEntryDto draft = await CreateDraftAsync(client);
        PostJournalEntryRequest postRequest = new() { RowVersion = draft.RowVersion };

        // Act
        HttpResponseMessage response =
            await client.PostAsJsonAsync($"/api/v1/journal-entries/{draft.Id}/post", postRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>The create endpoint returns 403 when the caller lacks finance.journal:create.</summary>
    [Test]
    public async Task Create_Returns403_WhenCreatePermissionMissing()
    {
        // Arrange: grant only read, then call create.
        _factory.PermissionState.Grant(ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        CreateJournalEntryRequest request = BuildBalancedCreateRequest();

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/journal-entries", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>GET by id returns the persisted entry, and the list endpoint returns it in a paged envelope.</summary>
    [Test]
    public async Task GetAndList_ReturnSeededEntry()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        JournalEntryDto draft = await CreateDraftAsync(client);

        // Act
        HttpResponseMessage getResponse = await client.GetAsync($"/api/v1/journal-entries/{draft.Id}");
        JournalEntryDto? fetched = await getResponse.Content.ReadFromJsonAsync<JournalEntryDto>();
        HttpResponseMessage listResponse = await client.GetAsync("/api/v1/journal-entries");
        Finance.GenericFiltering.Models.PagedResult<JournalEntryDto>? page =
            await listResponse.Content.ReadFromJsonAsync<Finance.GenericFiltering.Models.PagedResult<JournalEntryDto>>();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(fetched!.Id, Is.EqualTo(draft.Id));
            Assert.That(listResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(page!.TotalCount, Is.EqualTo(1));
            Assert.That(page.Items.Single().Id, Is.EqualTo(draft.Id));
        });
    }

    /// <summary>Creates a balanced draft through the API and returns its DTO.</summary>
    /// <param name="client">The authenticated HTTP client.</param>
    /// <returns>The created draft entry DTO.</returns>
    private async Task<JournalEntryDto> CreateDraftAsync(HttpClient client)
    {
        HttpResponseMessage response =
            await client.PostAsJsonAsync("/api/v1/journal-entries", BuildBalancedCreateRequest());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JournalEntryDto>())!;
    }

    /// <summary>Creates a balanced draft and posts it through the API, returning the posted DTO.</summary>
    /// <param name="client">The authenticated HTTP client (must hold create + post permissions).</param>
    /// <returns>The posted entry DTO.</returns>
    private async Task<JournalEntryDto> CreateAndPostAsync(HttpClient client)
    {
        JournalEntryDto draft = await CreateDraftAsync(client);
        PostJournalEntryRequest postRequest = new() { RowVersion = draft.RowVersion };
        HttpResponseMessage response =
            await client.PostAsJsonAsync($"/api/v1/journal-entries/{draft.Id}/post", postRequest);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JournalEntryDto>())!;
    }

    /// <summary>Extracts the numeric suffix from a <c>JE-{year}-{NNNNNN}</c> entry number (SDD-INFRA-003 format).</summary>
    /// <param name="entryNumber">The assigned entry number (must be non-null on a posted entry).</param>
    /// <returns>The trailing numeric sequence value.</returns>
    private static int ExtractEntryNumberSuffix(string? entryNumber)
    {
        Assert.That(entryNumber, Is.Not.Null.And.StartsWith("JE"));
        string suffix = entryNumber!.Split('-')[^1];
        return int.Parse(suffix, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Builds a balanced two-line create request (100 debit / 100 credit, base currency).</summary>
    /// <returns>A valid balanced create request.</returns>
    private static CreateJournalEntryRequest BuildBalancedCreateRequest() => new()
    {
        EntryDate = DateTimeOffset.UtcNow,
        Description = "Test entry",
        Lines =
        [
            BuildLine(1001, debit: 100m, credit: 0m),
            BuildLine(1002, debit: 0m, credit: 100m)
        ]
    };

    /// <summary>Builds a single base-currency journal line request.</summary>
    /// <param name="accountId">The target account id.</param>
    /// <param name="debit">The debit amount (also the base debit).</param>
    /// <param name="credit">The credit amount (also the base credit).</param>
    /// <returns>The line request.</returns>
    private static JournalEntryLineRequest BuildLine(int accountId, decimal debit, decimal credit) => new()
    {
        AccountId = accountId,
        DebitAmount = debit,
        CreditAmount = credit,
        CurrencyCode = BaseCurrency,
        ExchangeRate = 1.000000m,
        BaseDebitAmount = debit,
        BaseCreditAmount = credit
    };

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

    /// <summary>Counts the rows in the MassTransit EF Core outbox table.</summary>
    /// <returns>The number of pending outbox messages.</returns>
    private async Task<int> CountOutboxMessagesAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        JournalDbContext db = scope.ServiceProvider.GetRequiredService<JournalDbContext>();
        return await db.Set<OutboxMessage>().AsNoTracking().CountAsync();
    }

    /// <summary>Determines whether an audit row of the given event type exists for the entry.</summary>
    /// <param name="entityId">The entry identifier (the audit EntityId).</param>
    /// <param name="eventType">The audit event type.</param>
    /// <returns><see langword="true"/> when a matching audit row exists.</returns>
    private async Task<bool> HasAuditAsync(Guid entityId, string eventType)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        JournalDbContext db = scope.ServiceProvider.GetRequiredService<JournalDbContext>();
        string id = entityId.ToString();
        return await db.OperationsEvents
            .AsNoTracking()
            .AnyAsync(e => e.EntityId == id && e.EventType == eventType);
    }
}
