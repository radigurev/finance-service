using Finance.Periods.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Finance.Periods.API.Tests.Fixtures;

/// <summary>
/// Emulates the SQL Server <c>rowversion</c> store-generated column on SQLite for the Periods unit tests
/// (SDD-FIN-004 §6). SQLite cannot auto-increment a <c>rowversion</c> token, so this interceptor stamps a
/// fresh 8-byte token onto every added or modified <see cref="FiscalPeriod"/> before <c>SaveChanges</c>,
/// mirroring SQL Server. This makes the optimistic-concurrency path (SDD-FIN-004 §2.12) observable offline.
/// </summary>
public sealed class SqlitePeriodsRowVersionInterceptor : SaveChangesInterceptor
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

        IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<FiscalPeriod>> entries =
            eventData.Context.ChangeTracker.Entries<FiscalPeriod>();

        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<FiscalPeriod> entry in entries)
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.RowVersion = Guid.NewGuid().ToByteArray()[..8];
            }
        }
    }
}
