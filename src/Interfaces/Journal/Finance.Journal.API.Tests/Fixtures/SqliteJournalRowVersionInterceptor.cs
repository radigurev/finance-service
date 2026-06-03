using Finance.Journal.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Finance.Journal.API.Tests.Fixtures;

/// <summary>
/// Emulates the SQL Server store-generated columns on SQLite for the Journal unit tests. SQLite cannot
/// evaluate <c>NEWSEQUENTIALID()</c> for the GUID PK nor auto-increment a <c>rowversion</c> token, so this
/// interceptor assigns a fresh sequential-style GUID to any added <see cref="JournalEntry"/> with an empty
/// <see cref="JournalEntry.Id"/> and stamps a fresh 8-byte token onto every added or modified entry before
/// <c>SaveChanges</c>, mirroring SQL Server. This makes posting (PK assignment), reversal linkage, and the
/// optimistic-concurrency path (SDD-FIN-002 §2.5, §2.12) observable offline.
/// </summary>
public sealed class SqliteJournalRowVersionInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        StampRowVersions(eventData);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StampRowVersions(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void StampRowVersions(DbContextEventData eventData)
    {
        if (eventData.Context is null)
        {
            return;
        }

        IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<JournalEntry>> entries =
            eventData.Context.ChangeTracker.Entries<JournalEntry>();

        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<JournalEntry> entry in entries)
        {
            if (entry.State is EntityState.Added && entry.Entity.Id == Guid.Empty)
            {
                entry.Entity.Id = Guid.NewGuid();
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.RowVersion = Guid.NewGuid().ToByteArray()[..8];
            }
        }
    }
}
