using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Journal.API.Tests.Builders;
using Finance.Journal.API.Tests.Fixtures;
using Finance.Journal.API.Workflow;
using Finance.Journal.DBModel.Models;
using Finance.ServiceModel.Events.Journal;
using Finance.ServiceModel.Journal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Services;

/// <summary>
/// End-to-end seam tests proving SDD-FIN-004 activates the dormant Batch-10 <c>POSTING_PERIOD_CLOSED</c>
/// rule (SDD-FIN-004 §2.7, §6.4; SDD-FIN-002 §2.7). The real <see cref="GatewayPostingPeriodGuard"/> — wired
/// to a faked Periods reader — is injected into the live <see cref="Finance.Journal.API.Services.JournalEntryService"/>
/// posting path in place of the default mocked guard. A closed period must block posting and publish no
/// event; an open period must let posting succeed.
/// </summary>
[TestFixture]
[Category("SDD-FIN-004")]
[Category("SDD-FIN-002")]
public sealed class JournalPostingPeriodSeamTests
{
    private SqliteJournalDbContextScope _scope = null!;
    private FakePeriodReadClient _periods = null!;
    private JournalEntryServiceTestHarness _harness = null!;

    /// <summary>Creates a SQLite-backed harness driven by the real gateway period guard before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteJournalDbContextFactory.Create();
        _periods = new FakePeriodReadClient();
        GatewayPostingPeriodGuard guard = new(_periods, NullLogger<GatewayPostingPeriodGuard>.Instance);
        _harness = JournalEntryServiceTestHarness.BuildWithGuard(_scope.Context, guard);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    /// <summary>
    /// Posting a balanced draft whose period the Periods service reports as closed returns
    /// POSTING_PERIOD_CLOSED through the real guard and publishes no event (§2.7, §6.4).
    /// </summary>
    [Test]
    public async Task Post_IntoClosedPeriod_ReturnsPostingPeriodClosed_ViaRealGuard()
    {
        // Arrange — the real guard reads the faked Periods service, which reports the period as closed.
        _periods.ReturnsClosedPeriod();
        JournalEntry draft = await PersistDraftAsync();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.PostAsync(
            draft.Id, RowVersionRequest(draft), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.POSTING_PERIOD_CLOSED));
            Assert.That(_harness.PublishedEvents.OfType<JournalEntryPostedEvent>(), Is.Empty);
        });
    }

    /// <summary>
    /// Posting a balanced draft whose period the Periods service reports as open succeeds through the real
    /// guard and publishes the posted event (§2.7, §6.4).
    /// </summary>
    [Test]
    public async Task Post_IntoOpenPeriod_Succeeds_ViaRealGuard()
    {
        // Arrange — the real guard reads the faked Periods service, which reports the period as open.
        _periods.ReturnsOpenPeriod();
        JournalEntry draft = await PersistDraftAsync();

        // Act
        Result<JournalEntryDto> result = await _harness.Service.PostAsync(
            draft.Id, RowVersionRequest(draft), CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Status, Is.EqualTo(JournalEntryStatus.Posted));
            Assert.That(_harness.PublishedEvents.OfType<JournalEntryPostedEvent>().Count(), Is.EqualTo(1));
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
        return await _scope.Context.JournalEntries
            .Include(entry => entry.Lines)
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == created.Value!.Id, CancellationToken.None);
    }

    private static PostJournalEntryRequest RowVersionRequest(JournalEntry entry)
    {
        return new PostJournalEntryRequest { RowVersion = Convert.ToBase64String(entry.RowVersion) };
    }
}
