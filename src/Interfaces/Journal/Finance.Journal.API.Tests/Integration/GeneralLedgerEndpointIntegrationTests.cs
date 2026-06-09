using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.IntegrationTesting;
using Finance.Journal.API.Interfaces;
using Finance.Journal.API.Tests.Integration.TestDoubles;
using Finance.ServiceModel.Journal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Integration;

/// <summary>
/// Endpoint and real-SQL integration tests for the General Ledger / Trial Balance read surface
/// (SDD-FIN-003 §6.4). Each test boots the real <c>Finance.Journal.API</c> host through
/// <see cref="FinanceApiFactory{TProgram}"/> against the shared Testcontainers SQL Server, posts real
/// journal entries through the API (with the gateway-backed reference / period clients replaced by
/// in-memory fakes), and asserts the GL aggregation over genuine <c>SELECT ... GROUP BY</c> SQL.
/// Tagged <c>[Category("Integration")]</c> so the offline unit run skips it.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("SDD-FIN-003")]
public sealed class GeneralLedgerEndpointIntegrationTests
{
    private const string CreatePermission = "finance.journal:create";
    private const string PostPermission = "finance.journal:post";
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

    /// <summary>GET /trial-balance returns 200 with balanced grand totals over posted entries (real SQL).</summary>
    [Test]
    public async Task TrialBalance_Returns200_WithBalancedTotals_OverRealSql()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, PostPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        await PostEntryAsync(client, 1001, 1002, 100m);
        await PostEntryAsync(client, 1001, 1002, 50m);
        string asOf = DateTimeOffset.UtcNow.AddDays(1).ToString("O", CultureInfo.InvariantCulture);

        // Act
        HttpResponseMessage response = await client.GetAsync($"/api/v1/trial-balance?asOfDate={Uri.EscapeDataString(asOf)}");
        TrialBalanceDto? balance = await response.Content.ReadFromJsonAsync<TrialBalanceDto>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(balance, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(balance!.GrandTotalDebit, Is.EqualTo(150m));
            Assert.That(balance.GrandTotalCredit, Is.EqualTo(150m));
            Assert.That(balance.Balanced, Is.True);
            Assert.That(balance.Rows, Has.Count.EqualTo(2));
        });
    }

    /// <summary>GET /general-ledger/accounts/{id} returns 200 with a chronological running balance (real SQL).</summary>
    [Test]
    public async Task AccountLedger_Returns200_WithRunningBalance_OverRealSql()
    {
        // Arrange: two debits to account 1001 => running balance 100 then 150.
        _factory.PermissionState.Grant(CreatePermission, PostPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        await PostEntryAsync(client, 1001, 1002, 100m);
        await PostEntryAsync(client, 1001, 1002, 50m);

        // Act
        HttpResponseMessage response = await client.GetAsync("/api/v1/general-ledger/accounts/1001");
        AccountLedgerDto? ledger = await response.Content.ReadFromJsonAsync<AccountLedgerDto>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(ledger, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(ledger!.AccountId, Is.EqualTo(1001));
            Assert.That(ledger.OpeningBalance, Is.EqualTo(0m));
            Assert.That(ledger.ClosingBalance, Is.EqualTo(150m));
            Assert.That(ledger.Lines.Items, Has.Count.EqualTo(2));
            Assert.That(ledger.Lines.Items.Last().RunningBalance, Is.EqualTo(150m));
        });
    }

    /// <summary>The account-ledger endpoint returns 400 INVALID_DATE_RANGE when fromDate is after toDate.</summary>
    [Test]
    public async Task AccountLedger_Returns400InvalidDateRange_WhenFromDateAfterToDate()
    {
        // Arrange
        _factory.PermissionState.Grant(ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        string from = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        string to = DateTimeOffset.UtcNow.AddDays(-10).ToString("O", CultureInfo.InvariantCulture);

        // Act
        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/general-ledger/accounts/1001?fromDate={Uri.EscapeDataString(from)}&toDate={Uri.EscapeDataString(to)}");
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo(JournalErrorCodes.INVALID_DATE_RANGE));
    }

    /// <summary>GL balances are never cached: a recompute reflects a newly posted entry (SDD-INFRA-004).</summary>
    [Test]
    public async Task GlBalances_AreNotCached_RecomputeReflectsNewPosting()
    {
        // Arrange
        _factory.PermissionState.Grant(CreatePermission, PostPermission, ReadPermission);
        HttpClient client = _factory.CreateAuthenticatedClient();
        await PostEntryAsync(client, 1001, 1002, 100m);
        string asOf = DateTimeOffset.UtcNow.AddDays(1).ToString("O", CultureInfo.InvariantCulture);
        string url = $"/api/v1/trial-balance?asOfDate={Uri.EscapeDataString(asOf)}";

        // Act: first read, then post more, then read again.
        TrialBalanceDto? first = await client.GetFromJsonAsync<TrialBalanceDto>(url);
        await PostEntryAsync(client, 1001, 1002, 75m);
        TrialBalanceDto? second = await client.GetFromJsonAsync<TrialBalanceDto>(url);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(first!.GrandTotalDebit, Is.EqualTo(100m));
            Assert.That(second!.GrandTotalDebit, Is.EqualTo(175m), "Recompute must reflect the new posting (no caching).");
        });
    }

    /// <summary>The trial-balance endpoint returns 403 when the caller lacks finance.journal:read.</summary>
    [Test]
    public async Task TrialBalance_Returns403_WhenReadPermissionMissing()
    {
        // Arrange
        _factory.PermissionState.RevokeAll();
        HttpClient client = _factory.CreateAuthenticatedClient();
        string asOf = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        // Act
        HttpResponseMessage response =
            await client.GetAsync($"/api/v1/trial-balance?asOfDate={Uri.EscapeDataString(asOf)}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>The account-ledger endpoint returns 403 when the caller lacks finance.journal:read.</summary>
    [Test]
    public async Task AccountLedger_Returns403_WhenReadPermissionMissing()
    {
        // Arrange
        _factory.PermissionState.RevokeAll();
        HttpClient client = _factory.CreateAuthenticatedClient();

        // Act
        HttpResponseMessage response = await client.GetAsync("/api/v1/general-ledger/accounts/1001");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>Creates a balanced two-line entry and posts it through the API.</summary>
    /// <param name="client">The authenticated HTTP client (create + post permissions).</param>
    /// <param name="debitAccount">The debited account id.</param>
    /// <param name="creditAccount">The credited account id.</param>
    /// <param name="amount">The base-currency amount.</param>
    private async Task PostEntryAsync(HttpClient client, int debitAccount, int creditAccount, decimal amount)
    {
        CreateJournalEntryRequest createRequest = new()
        {
            EntryDate = DateTimeOffset.UtcNow,
            Description = "GL entry",
            Lines =
            [
                BuildLine(debitAccount, debit: amount, credit: 0m),
                BuildLine(creditAccount, debit: 0m, credit: amount)
            ]
        };

        HttpResponseMessage createResponse =
            await client.PostAsJsonAsync("/api/v1/journal-entries", createRequest);
        createResponse.EnsureSuccessStatusCode();
        JournalEntryDto draft = (await createResponse.Content.ReadFromJsonAsync<JournalEntryDto>())!;

        PostJournalEntryRequest postRequest = new() { RowVersion = draft.RowVersion };
        HttpResponseMessage postResponse =
            await client.PostAsJsonAsync($"/api/v1/journal-entries/{draft.Id}/post", postRequest);
        postResponse.EnsureSuccessStatusCode();
    }

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
}
