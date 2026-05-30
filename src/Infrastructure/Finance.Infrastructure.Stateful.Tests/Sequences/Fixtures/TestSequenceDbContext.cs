using Finance.Infrastructure.Sequences;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Stateful.Tests.Sequences.Fixtures;

/// <summary>
/// SQLite-backed <see cref="DbContext"/> mapping <see cref="SequenceCounter"/> for the sequence
/// generator unit tests (SDD-INFRA-003 §6). Applies the shipped
/// <see cref="SequenceCounterConfiguration"/> then clears the <c>SYSDATETIMEOFFSET()</c> default,
/// which is a SQL Server function unavailable on SQLite.
/// </summary>
public sealed class TestSequenceDbContext : DbContext
{
    /// <summary>Initializes the context with the supplied options.</summary>
    /// <param name="options">The context options (configured for SQLite in-memory).</param>
    public TestSequenceDbContext(DbContextOptions<TestSequenceDbContext> options)
        : base(options)
    {
    }

    /// <summary>The counter set backing the sequence generator.</summary>
    public DbSet<SequenceCounter> Sequences => Set<SequenceCounter>();

    /// <summary>Applies the sequence configuration and removes the SQL Server timestamp default.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new SequenceCounterConfiguration());
        modelBuilder.Entity<SequenceCounter>()
            .Property(counter => counter.ModifiedAt)
            .HasDefaultValueSql(null);
    }
}
