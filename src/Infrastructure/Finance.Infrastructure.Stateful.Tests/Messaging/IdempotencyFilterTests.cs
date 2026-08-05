using Finance.Infrastructure.Messaging.Filters;
using Finance.Infrastructure.Stateful.Tests.Messaging.Fixtures;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using StackExchange.Redis;

namespace Finance.Infrastructure.Stateful.Tests.Messaging;

/// <summary>
/// Unit tests for <see cref="IdempotencyFilter{T}"/> covering the Redis <c>SETNX</c> idempotency rule
/// (SDD-INFRA-006 §2.5): the first occurrence of a <c>MessageId</c> is forwarded down the pipe while a
/// replay is skipped, and a FAILED consume releases the claim so MassTransit's retry and dead-letter
/// policy can run (CHG-FIX-006). The Redis <c>SETNX</c> seam is faked via a mocked
/// <see cref="IConnectionMultiplexer"/>; no real Redis is required.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-006")]
public sealed class IdempotencyFilterTests
{
    private Mock<IDatabase> _database = null!;
    private Mock<IConnectionMultiplexer> _multiplexer = null!;
    private Mock<IPipe<ConsumeContext<SampleFinanceEvent>>> _next = null!;

    /// <summary>Builds fresh Redis and pipe mocks before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _database = new Mock<IDatabase>(MockBehavior.Loose);
        _multiplexer = new Mock<IConnectionMultiplexer>(MockBehavior.Loose);
        _multiplexer
            .Setup(mux => mux.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_database.Object);
        _next = new Mock<IPipe<ConsumeContext<SampleFinanceEvent>>>(MockBehavior.Loose);
    }

    /// <summary>When SETNX reports a duplicate (false), the message is skipped and not forwarded.</summary>
    [Test]
    public async Task Send_SkipsDuplicateMessageId_WhenSetNxReportsDuplicate()
    {
        // Arrange
        SetNxReturns(isFirstOccurrence: false);
        IdempotencyFilter<SampleFinanceEvent> filter = BuildFilter();
        ConsumeContext<SampleFinanceEvent> context = BuildContext(Guid.NewGuid());

        // Act
        await filter.Send(context, _next.Object);

        // Assert
        _next.Verify(
            pipe => pipe.Send(It.IsAny<ConsumeContext<SampleFinanceEvent>>()),
            Times.Never);
    }

    /// <summary>When SETNX claims the key (true) for a first-seen message, it is forwarded down the pipe.</summary>
    [Test]
    public async Task Send_ForwardsFirstOccurrence_WhenSetNxClaimsKey()
    {
        // Arrange
        SetNxReturns(isFirstOccurrence: true);
        IdempotencyFilter<SampleFinanceEvent> filter = BuildFilter();
        ConsumeContext<SampleFinanceEvent> context = BuildContext(Guid.NewGuid());

        // Act
        await filter.Send(context, _next.Object);

        // Assert
        _next.Verify(pipe => pipe.Send(context), Times.Once);
    }

    /// <summary>The idempotency key is composed as <c>finance:processed:{MessageId}</c> with a 7-day TTL and NotExists.</summary>
    [Test]
    public async Task Send_UsesProcessedKeyConvention_WithSevenDayTtlAndNotExists()
    {
        // Arrange
        SetNxReturns(isFirstOccurrence: true);
        IdempotencyFilter<SampleFinanceEvent> filter = BuildFilter();
        Guid messageId = Guid.NewGuid();
        ConsumeContext<SampleFinanceEvent> context = BuildContext(messageId);

        // Act
        await filter.Send(context, _next.Object);

        // Assert
        _database.Verify(
            db => db.StringSetAsync(
                It.Is<RedisKey>(key => key == $"finance:processed:{messageId}"),
                It.Is<RedisValue>(value => value == "1"),
                It.Is<TimeSpan?>(ttl => ttl == TimeSpan.FromDays(7)),
                When.NotExists),
            Times.Once);
    }

    /// <summary>
    /// When the downstream pipe throws, the claim is deleted and the original exception propagates with its
    /// identity intact, so MassTransit's retry and dead-letter policy governs the message (CHG-FIX-006).
    /// </summary>
    [Test]
    public void Send_DownstreamPipeThrows_DeletesProcessedKeyAndRethrowsOriginalException()
    {
        // Arrange
        SetNxReturns(isFirstOccurrence: true);
        InvalidOperationException consumerFailure = new("Consumer failed.");
        _next
            .Setup(pipe => pipe.Send(It.IsAny<ConsumeContext<SampleFinanceEvent>>()))
            .ThrowsAsync(consumerFailure);
        IdempotencyFilter<SampleFinanceEvent> filter = BuildFilter();
        Guid messageId = Guid.NewGuid();
        ConsumeContext<SampleFinanceEvent> context = BuildContext(messageId);

        // Act
        InvalidOperationException? thrown = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await filter.Send(context, _next.Object));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(consumerFailure));
            _database.Verify(
                db => db.KeyDeleteAsync(
                    It.Is<RedisKey>(key => key == $"finance:processed:{messageId}"),
                    It.IsAny<CommandFlags>()),
                Times.Once);
            _next.Verify(pipe => pipe.Send(context), Times.Once);
        });
    }

    /// <summary>
    /// A genuine duplicate arriving after a SUCCESSFUL consume is still short-circuited: the pipe is not
    /// invoked a second time and the claim is left in place (SDD-INFRA-006 §2.5, CHG-FIX-006).
    /// </summary>
    [Test]
    public async Task Send_GenuineDuplicateAfterSuccessfulConsume_DoesNotInvokeNextAgain_AndKeepsClaim()
    {
        // Arrange
        _database
            .SetupSequence(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        IdempotencyFilter<SampleFinanceEvent> filter = BuildFilter();
        ConsumeContext<SampleFinanceEvent> context = BuildContext(Guid.NewGuid());

        // Act
        await filter.Send(context, _next.Object);
        await filter.Send(context, _next.Object);

        // Assert
        Assert.Multiple(() =>
        {
            _next.Verify(pipe => pipe.Send(context), Times.Once);
            _database.Verify(
                db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()),
                Times.Never);
        });
    }

    /// <summary>A null consume context is rejected with an ArgumentNullException.</summary>
    [Test]
    public void Send_NullContext_ThrowsArgumentNullException()
    {
        // Arrange
        IdempotencyFilter<SampleFinanceEvent> filter = BuildFilter();

        // Act & Assert
        Assert.That(
            async () => await filter.Send(null!, _next.Object),
            Throws.TypeOf<ArgumentNullException>());
    }

    private void SetNxReturns(bool isFirstOccurrence)
    {
        _database
            .Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
            .ReturnsAsync(isFirstOccurrence);
    }

    private IdempotencyFilter<SampleFinanceEvent> BuildFilter()
    {
        return new IdempotencyFilter<SampleFinanceEvent>(
            _multiplexer.Object,
            NullLogger<IdempotencyFilter<SampleFinanceEvent>>.Instance);
    }

    private static ConsumeContext<SampleFinanceEvent> BuildContext(Guid messageId)
    {
        SampleFinanceEvent message = new()
        {
            MessageId = messageId,
            CorrelationId = "corr-001",
            OccurredAt = DateTimeOffset.UtcNow,
            EntryNumber = "JE-2026-000001"
        };

        Mock<ConsumeContext<SampleFinanceEvent>> context = new(MockBehavior.Loose);
        context.SetupGet(ctx => ctx.MessageId).Returns(messageId);
        context.SetupGet(ctx => ctx.Message).Returns(message);
        return context.Object;
    }
}
