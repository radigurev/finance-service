using Finance.Nomenclature.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Finance.Nomenclature.API.Tests.Fixtures;

/// <summary>
/// Emulates the SQL Server <c>rowversion</c> column on SQLite for the Nomenclature unit tests. SQLite has
/// no native auto-incrementing concurrency token, so this interceptor stamps a fresh 8-byte token onto
/// every added or modified <see cref="Currency"/> before <c>SaveChanges</c>, mirroring how SQL Server
/// bumps the token on each write. This makes the optimistic-concurrency path (SDD-NOM-001 §2.1)
/// observable offline.
/// </summary>
public sealed class SqliteRowVersionInterceptor : SaveChangesInterceptor
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

        IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<Currency>> entries =
            eventData.Context.ChangeTracker.Entries<Currency>();

        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<Currency> entry in entries)
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.RowVersion = Guid.NewGuid().ToByteArray()[..8];
            }
        }
    }
}
