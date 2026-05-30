using Finance.Infrastructure.Audit.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Infrastructure.Audit.Extensions;

/// <summary>
/// DI registration for the write-path audit service (SDD-AUDIT-001 §2.3).
/// </summary>
public static class AuditServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IAuditService"/> backed by <see cref="AuditService{TContext}"/> for the
    /// supplied service DbContext. The DbContext must already be registered and must implement
    /// <see cref="IAuditDbContext"/> so audit rows are written into the same ambient context (and
    /// therefore the same transaction) as the change they describe.
    /// </summary>
    /// <typeparam name="TContext">The service DbContext type that owns the audit set.</typeparam>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddFinanceAudit<TContext>(this IServiceCollection services)
        where TContext : DbContext, IAuditDbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAuditService, AuditService<TContext>>();
        return services;
    }
}
