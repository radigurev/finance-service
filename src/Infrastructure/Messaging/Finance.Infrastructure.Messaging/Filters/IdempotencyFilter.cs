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
/// A consume that FAILS releases the claim before the exception is rethrown (CHG-FIX-006), so the
/// outer <c>UseMessageRetry</c> filter re-enters this filter on a genuinely unprocessed message and
/// MassTransit's retry and dead-letter policy runs to completion instead of the failed message being
/// short-circuited as a duplicate of itself and silently acknowledged.
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
    /// than risk processing the same message twice. When <paramref name="next"/> throws, the claim is
    /// released and the original exception is rethrown with its stack intact (CHG-FIX-006) so the message
    /// is not treated as already processed on redelivery and MassTransit's retry and dead-letter policy
    /// governs its fate.
    /// </summary>
    /// <param name="context">The consume context carrying the message and its identifier.</param>
    /// <param name="next">The downstream pipe representing the remaining consume pipeline.</param>
    /// <returns>
    /// A task that completes when the message has been processed or skipped, and that faults with the
    /// downstream exception once the claim has been released.
    /// </returns>
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

        try
        {
            await next.Send(context).ConfigureAwait(false);
        }
        catch
        {
            await ReleaseClaimAsync(database, key, messageId).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Describes this filter for MassTransit pipeline diagnostics (<c>probe</c>).</summary>
    /// <param name="context">The probe context to populate.</param>
    public void Probe(ProbeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CreateFilterScope("financeIdempotency");
    }

    /// <summary>
    /// Deletes the Redis claim for a message whose downstream consume failed, so the redelivery produced by
    /// the outer retry filter is seen as a first occurrence rather than as a duplicate of itself
    /// (CHG-FIX-006).
    /// </summary>
    /// <param name="database">The Redis database holding the claim.</param>
    /// <param name="key">The <c>finance:processed:{MessageId}</c> key to release.</param>
    /// <param name="messageId">The message identifier the claim was taken for, used for logging.</param>
    /// <returns>A task that completes when the claim has been released.</returns>
    private async Task ReleaseClaimAsync(IDatabase database, string key, Guid messageId)
    {
        try
        {
            await database.KeyDeleteAsync(key).ConfigureAwait(false);
        }
        catch (RedisException releaseFailure)
        {
            _logger.LogError(
                releaseFailure,
                "Failed to release the idempotency claim for message {MessageId} of type {MessageType} after a failed consume. The claim is still held, so a redelivery within the {TtlDays}-day window will be skipped as a duplicate and the message will be acknowledged without being processed.",
                messageId,
                typeof(T).Name,
                ProcessedTtl.TotalDays);

            return;
        }

        _logger.LogWarning(
            "Released idempotency claim for message {MessageId} of type {MessageType} after a failed consume; retry and dead-letter policy applies.",
            messageId,
            typeof(T).Name);
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
