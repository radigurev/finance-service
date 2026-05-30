using Finance.Infrastructure.Sequences.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Finance.Infrastructure.Sequences;

/// <summary>
/// DI registration for the gapless sequence generator (SDD-INFRA-003 §1, §2.6). Wires the
/// built-in sequence definitions, the default BG-style formatter seam, and the per-context
/// <see cref="ISequenceGenerator"/>. The owning service must register its <see cref="DbContext"/>
/// and apply <see cref="SequenceCounterConfiguration"/> in its model.
/// </summary>
public static class SequenceGeneratorServiceCollectionExtensions
{
    /// <summary>
    /// Registers the built-in sequence definitions, <see cref="DefaultDocumentNumberFormatter"/>,
    /// and <see cref="SequenceGenerator{TContext}"/> as the scoped <see cref="ISequenceGenerator"/>.
    /// </summary>
    /// <typeparam name="TDbContext">The owning <see cref="DbContext"/> mapping the sequences table.</typeparam>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSequenceGenerator<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        SequenceDefinitions.ValidatePadding(SequenceDefinitions.BuiltIn);

        services.TryAddSingleton<IReadOnlyDictionary<string, SequenceDefinition>>(SequenceDefinitions.BuiltIn);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDocumentNumberFormatter>(provider =>
            new DefaultDocumentNumberFormatter(
                provider.GetRequiredService<IReadOnlyDictionary<string, SequenceDefinition>>()));

        services.AddScoped<ISequenceGenerator>(provider =>
            new SequenceGenerator<TDbContext>(
                provider.GetRequiredService<TDbContext>(),
                provider.GetRequiredService<IReadOnlyDictionary<string, SequenceDefinition>>(),
                provider.GetRequiredService<IDocumentNumberFormatter>(),
                provider.GetRequiredService<TimeProvider>()));

        return services;
    }
}
