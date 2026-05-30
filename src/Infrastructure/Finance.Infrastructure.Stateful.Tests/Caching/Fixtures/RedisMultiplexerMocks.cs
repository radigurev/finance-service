using Moq;
using StackExchange.Redis;

namespace Finance.Infrastructure.Stateful.Tests.Caching.Fixtures;

/// <summary>
/// Builders for mocked StackExchange.Redis seams used by the cache unit tests (SDD-INFRA-004 §6).
/// They simulate a Redis backend that is unreachable so the cache layer's fall-through-to-factory
/// behaviour can be verified without a real Redis instance.
/// </summary>
public static class RedisMultiplexerMocks
{
    /// <summary>
    /// Builds an <see cref="IConnectionMultiplexer"/> whose <see cref="IDatabase"/> throws a
    /// <see cref="RedisConnectionException"/> on every read and write, simulating an unreachable backend.
    /// </summary>
    /// <returns>A mocked multiplexer that fails all database operations.</returns>
    public static IConnectionMultiplexer ThrowingOnEveryOperation()
    {
        RedisConnectionException failure = new(ConnectionFailureType.UnableToConnect, "Redis is down (simulated).");

        Mock<IDatabase> database = new(MockBehavior.Loose);
        database
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(failure);
        database
            .Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
            .ThrowsAsync(failure);

        Mock<IConnectionMultiplexer> multiplexer = new(MockBehavior.Loose);
        multiplexer
            .Setup(mux => mux.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        return multiplexer.Object;
    }
}
