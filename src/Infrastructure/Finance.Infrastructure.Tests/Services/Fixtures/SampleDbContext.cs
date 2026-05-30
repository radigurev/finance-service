using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Tests.Services.Fixtures;

/// <summary>A minimal SQLite-backed <see cref="DbContext"/> used by the service-layer tests.</summary>
public sealed class SampleDbContext : DbContext
{
    /// <summary>Initializes the context with the supplied options.</summary>
    /// <param name="options">The context options (configured for SQLite in-memory).</param>
    public SampleDbContext(DbContextOptions<SampleDbContext> options)
        : base(options)
    {
    }

    /// <summary>The sample entity set.</summary>
    public DbSet<SampleEntity> Samples => Set<SampleEntity>();
}
