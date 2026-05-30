using Finance.EventLog.API.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Finance.EventLog.API.Services;

/// <summary>
/// Daily background job that prunes expired event-log rows (SDD-EVTLOG-001 §2.7). On each tick it opens a
/// DI scope, resolves the scoped <see cref="IEventLogRetentionService"/>, deletes every row older than
/// <c>EventLog:RetentionDays</c>, and logs the deleted count with a structured NLog template. A purge
/// failure is logged and swallowed so a transient DB outage cannot crash the host; the next tick retries.
/// </summary>
public sealed class EventLogRetentionHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventLogRetentionHostedService> _logger;

    /// <summary>Creates a new <see cref="EventLogRetentionHostedService"/>.</summary>
    /// <param name="scopeFactory">The scope factory used to resolve the scoped retention service per tick.</param>
    /// <param name="logger">The logger used for the structured deleted-count message.</param>
    public EventLogRetentionHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<EventLogRetentionHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);

        do
        {
            await PurgeOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task PurgeOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IEventLogRetentionService retention =
                scope.ServiceProvider.GetRequiredService<IEventLogRetentionService>();

            int deletedCount = await retention.PurgeExpiredAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Event-log retention deleted {DeletedCount} expired entries",
                deletedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event-log retention purge failed; retrying on the next daily tick");
        }
    }
}
