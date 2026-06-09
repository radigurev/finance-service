using Finance.Invoices.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Finance.Invoices.API.Tests.Fixtures;

/// <summary>
/// Emulates the SQL Server store-generated columns on SQLite for the Invoices unit tests. SQLite cannot
/// evaluate <c>NEWSEQUENTIALID()</c> for the GUID PK nor auto-increment a <c>rowversion</c> token, so this
/// interceptor assigns a fresh GUID to any added <see cref="Invoice"/> with an empty
/// <see cref="Invoice.Id"/> and stamps a fresh 8-byte token onto every added or modified invoice before
/// <c>SaveChanges</c>, mirroring SQL Server. This makes the confirm number assignment, the posting handshake,
/// and the optimistic-concurrency path (SDD-INV-001 §2.4, §2.5, §2.9) observable offline.
/// </summary>
public sealed class SqliteInvoicesRowVersionInterceptor : SaveChangesInterceptor
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

        IEnumerable<EntityEntry<Invoice>> entries = eventData.Context.ChangeTracker.Entries<Invoice>();

        foreach (EntityEntry<Invoice> entry in entries)
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
