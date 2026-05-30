using Finance.Common.ErrorCodes;
using MassTransit;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Finance.Infrastructure.Messaging.Filters;

/// <summary>
/// MassTransit consume filter that enforces message idempotency (SDD-INFRA-006 §2.5). It performs a
/// Redis <c>SETNX</c> on <c>finance:processed:{MessageId}</c> with a 7-day TTL over the
/// <see cref="IConnectionMultiplexer"/> owned by <c>Finance.Infrastructure.Caching</c> (SDD-INFRA-004):
/// the first occurrence is forwarded down the pipe, while a replay (retry or dead-letter re-queue) is
/// logged and skipped so consumers cannot double-post. When the transport supplies no inbound
/// <c>MessageId</c>, the filter falls back to a freshly generated identifier so the message is always
/// treated as a first occurrence rather than rejected.
/// </summary>
/// <typeparam name="T">The consumed message contract type.</typeparam>
public sealed class IdempotencyFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    private static readonly TimeSpan ProcessedTtl = TimeSpan.FromDays(7);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<IdempotencyFilter<T>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyFilter{T}"/> class.
    /// </summary>
    /// <param name="redis">The shared Redis multiplexer registered by the Caching library.</param>
    /// <param name="logger">Logger used for the duplicate-skipped warning.</param>
    public IdempotencyFilter(IConnectionMultiplexer redis, ILogger<IdempotencyFilter<T>> logger)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(logger);

        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Claims the message identifier in Redis and forwards the first occurrence to <paramref name="next"/>,
    /// short-circuiting and logging any duplicate. Redis failures propagate so MassTransit retries rather
    /// than risk processing the same message twice.
    /// </summary>
    /// <param name="context">The consume context carrying the message and its identifier.</param>
    /// <param name="next">The downstream pipe representing the remaining consume pipeline.</param>
    /// <returns>A task that completes when the message has been processed or skipped.</returns>
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        Guid messageId = ResolveMessageId(context);
        IDatabase database = _redis.GetDatabase();
        string key = $"finance:processed:{messageId}";

        bool isFirstOccurrence = await database
            .StringSetAsync(key, "1", ProcessedTtl, When.NotExists)
            .ConfigureAwait(false);

        if (!isFirstOccurrence)
        {
            _logger.LogWarning(
                "Duplicate message {MessageId} of type {MessageType} skipped. Code={ErrorCode}",
                messageId,
                typeof(T).Name,
                MessagingErrorCodes.DUPLICATE_MESSAGE_SKIPPED);
            return;
        }

        await next.Send(context).ConfigureAwait(false);
    }

    /// <summary>Describes this filter for MassTransit pipeline diagnostics (<c>probe</c>).</summary>
    /// <param name="context">The probe context to populate.</param>
    public void Probe(ProbeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CreateFilterScope("financeIdempotency");
    }

    /// <summary>
    /// Resolves the message identifier used as the idempotency key, falling back to the broker-assigned
    /// identifier when the transport does not supply one.
    /// </summary>
    /// <param name="context">The consume context to read the identifier from.</param>
    /// <returns>A non-empty message identifier.</returns>
    private static Guid ResolveMessageId(ConsumeContext<T> context)
    {
        return context.MessageId ?? NewId.NextGuid();
    }
}
