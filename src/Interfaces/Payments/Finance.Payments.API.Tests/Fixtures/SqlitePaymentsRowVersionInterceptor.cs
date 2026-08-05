using Finance.Payments.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// Emulates the SQL Server store-generated columns on SQLite for the Payments unit tests. SQLite cannot
/// evaluate <c>NEWSEQUENTIALID()</c> for the GUID PK nor auto-increment a <c>rowversion</c> token, so this
/// interceptor assigns a fresh GUID to any added <see cref="Payment"/> with an empty <see cref="Payment.Id"/>
/// and stamps a fresh 8-byte token onto every added or modified payment, allocation, and open-item row before
/// <c>SaveChanges</c>, mirroring SQL Server. This makes confirm numbering, the posting handshake, and the
/// optimistic-concurrency paths (SDD-PAY-001 §2.4/§2.5, SDD-PAY-002 §2.4/§2.14) observable offline.
/// <para><see cref="TamperOpenItemRowVersionOnce"/> simulates a SDD-PAY-002 §2.14 projection consumer bumping
/// <see cref="InvoiceOpenItem.RowVersion"/> mid-allocation: the tracked original token is replaced so the
/// <c>UPDATE … WHERE RowVersion = …</c> matches no row and EF raises the retryable concurrency failure.</para>
/// </summary>
public sealed class SqlitePaymentsRowVersionInterceptor : SaveChangesInterceptor
{
    /// <summary>
    /// When <c>true</c>, the next <c>SaveChanges</c> replaces the ORIGINAL row version of every modified
    /// <see cref="InvoiceOpenItem"/> with a foreign token, then resets itself. Used to simulate a concurrent
    /// projection write landing mid-allocation (SDD-PAY-002 §2.14).
    /// </summary>
    public bool TamperOpenItemRowVersionOnce { get; set; }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Apply(eventData);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContextEventData eventData)
    {
        if (eventData.Context is null)
        {
            return;
        }

        StampPayments(eventData.Context);
        StampTokens<PaymentAllocation>(eventData.Context, entity => entity.RowVersion = NewToken());
        TamperOpenItems(eventData.Context);
        StampTokens<InvoiceOpenItem>(eventData.Context, entity => entity.RowVersion = NewToken());
    }

    private static void StampPayments(DbContext context)
    {
        foreach (EntityEntry<Payment> entry in context.ChangeTracker.Entries<Payment>())
        {
            if (entry.State is EntityState.Added && entry.Entity.Id == Guid.Empty)
            {
                entry.Entity.Id = Guid.NewGuid();
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.RowVersion = NewToken();
            }
        }
    }

    private static void StampTokens<TEntity>(DbContext context, Action<TEntity> stamp)
        where TEntity : class
    {
        foreach (EntityEntry<TEntity> entry in context.ChangeTracker.Entries<TEntity>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                stamp(entry.Entity);
            }
        }
    }

    private void TamperOpenItems(DbContext context)
    {
        if (!TamperOpenItemRowVersionOnce)
        {
            return;
        }

        TamperOpenItemRowVersionOnce = false;

        foreach (EntityEntry<InvoiceOpenItem> entry in context.ChangeTracker.Entries<InvoiceOpenItem>())
        {
            if (entry.State is EntityState.Modified)
            {
                entry.Property(item => item.RowVersion).OriginalValue = NewToken();
            }
        }
    }

    private static byte[] NewToken() => Guid.NewGuid().ToByteArray()[..8];
}
