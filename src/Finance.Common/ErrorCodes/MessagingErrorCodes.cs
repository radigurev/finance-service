namespace Finance.Common.ErrorCodes;

/// <summary>
/// Machine-readable error codes for the resilient message publisher (SDD-INFRA-006):
/// MassTransit + EF Core transactional outbox, RabbitMQ transport, retry/dead-letter,
/// and the Redis-backed idempotency filter. Used as the <c>title</c> field of ProblemDetails
/// responses and in structured log templates.
/// </summary>
public static class MessagingErrorCodes
{
    /// <summary>The RabbitMQ broker host is missing from configuration or is unreachable on readiness.</summary>
    public const string RABBITMQ_UNREACHABLE = nameof(RABBITMQ_UNREACHABLE);

    /// <summary>The idempotency filter caught a replayed message with an already-processed <c>MessageId</c>.</summary>
    public const string DUPLICATE_MESSAGE_SKIPPED = nameof(DUPLICATE_MESSAGE_SKIPPED);

    /// <summary>A consumer exhausted its retry policy and the message was moved to the dead-letter queue.</summary>
    public const string MESSAGE_DEAD_LETTERED = nameof(MESSAGE_DEAD_LETTERED);

    /// <summary>The outbox row count breached the operational growth threshold and requires attention.</summary>
    public const string OUTBOX_GROWTH_ALERT = nameof(OUTBOX_GROWTH_ALERT);
}
