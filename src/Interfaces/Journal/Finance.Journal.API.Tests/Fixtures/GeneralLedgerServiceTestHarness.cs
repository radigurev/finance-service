using Finance.Journal.API.Services;
using Finance.Journal.DBModel;
using Finance.Journal.DBModel.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Finance.Journal.API.Tests.Fixtures;

/// <summary>
/// Assembles a <see cref="GeneralLedgerService"/> over a SQLite in-memory <see cref="JournalDbContext"/>
/// with the in-memory <see cref="FakeReferenceDataReader"/> for the SDD-FIN-003 read tests. The GL service
/// is a pure read aggregation, so the harness exposes a <see cref="Seed"/> helper that persists prebuilt
/// <see cref="JournalEntry"/> rows directly (bypassing the write-path service) to give each test precise
/// control over status, entry date, accounts, and base-currency amounts.
/// </summary>
public sealed class GeneralLedgerServiceTestHarness
{
    private readonly JournalDbContext _db;

    private GeneralLedgerServiceTestHarness(
        JournalDbContext db,
        GeneralLedgerService service,
        FakeReferenceDataReader referenceData)
    {
        _db = db;
        Service = service;
        ReferenceData = referenceData;
    }

    /// <summary>The system under test.</summary>
    public GeneralLedgerService Service { get; }

    /// <summary>The in-memory account reference reader (register code/name or leave blank to simulate degraded enrichment).</summary>
    public FakeReferenceDataReader ReferenceData { get; }

    /// <summary>Builds a harness over the supplied SQLite-backed context.</summary>
    /// <param name="db">The SQLite-backed journal context.</param>
    /// <returns>A wired harness.</returns>
    public static GeneralLedgerServiceTestHarness Build(JournalDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        FakeReferenceDataReader referenceData = new();
        GeneralLedgerService service = new(
            db,
            referenceData,
            NullLogger<GeneralLedgerService>.Instance);

        return new GeneralLedgerServiceTestHarness(db, service, referenceData);
    }

    /// <summary>Persists the supplied prebuilt entries and clears the change tracker so reads start cold.</summary>
    /// <param name="entries">The entries to persist.</param>
    public async Task Seed(params JournalEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        await _db.JournalEntries.AddRangeAsync(entries, CancellationToken.None);
        await _db.SaveChangesAsync(CancellationToken.None);
        _db.ChangeTracker.Clear();
    }
}
