using Finance.Accounts.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Finance.Accounts.API.Tests.Fixtures;

/// <summary>
/// Emulates the SQL Server <c>rowversion</c> column on SQLite for the Accounts unit tests. SQLite has no
/// native auto-incrementing concurrency token, so this interceptor stamps a fresh 8-byte token onto every
/// added or modified <see cref="Account"/> before <c>SaveChanges</c>, mirroring how SQL Server bumps the
/// token on each write. This makes the optimistic-concurrency path (SDD-ACCT-001 §2.10) observable offline.
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

        IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<Account>> entries =
            eventData.Context.ChangeTracker.Entries<Account>();

        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<Account> entry in entries)
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.RowVersion = Guid.NewGuid().ToByteArray()[..8];
            }
        }
    }
}
