using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Stateful.Tests.Messaging.Fixtures;

/// <summary>
/// Minimal publishing-service <see cref="DbContext"/> used to satisfy the generic type parameter of
/// <c>AddFinanceMessageBus&lt;TDbContext&gt;</c> in startup fail-fast unit tests (SDD-INFRA-006 §3). The
/// context is never resolved or connected; registration only binds the EF Core outbox to its type.
/// </summary>
public sealed class StubMessagingDbContext : DbContext
{
    /// <summary>Initializes the context with the supplied options.</summary>
    /// <param name="options">The context options supplied by the test or DI container.</param>
    public StubMessagingDbContext(DbContextOptions<StubMessagingDbContext> options)
        : base(options)
    {
    }
}
