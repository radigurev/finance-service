using Finance.Infrastructure.Audit.Configurations;
using Finance.Infrastructure.Audit.Entities;
using Finance.Infrastructure.Audit.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Stateful.Tests.Audit.Fixtures;

/// <summary>
/// SQLite-backed <see cref="DbContext"/> implementing <see cref="IAuditDbContext"/> used by the
/// audit write-path unit tests (SDD-AUDIT-001 §6). Applies the shipped
/// <see cref="OperationsEventConfiguration"/> so the test exercises the real mapping.
/// </summary>
public sealed class TestAuditDbContext : DbContext, IAuditDbContext
{
    /// <summary>Initializes the context with the supplied options.</summary>
    /// <param name="options">The context options (configured for SQLite in-memory).</param>
    public TestAuditDbContext(DbContextOptions<TestAuditDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    public DbSet<OperationsEvent> OperationsEvents => Set<OperationsEvent>();

    /// <summary>Applies the audit entity configuration shipped by the library.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new OperationsEventConfiguration());
    }
}
