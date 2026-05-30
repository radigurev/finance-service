using Moq;
using StackExchange.Redis;

namespace Finance.EventLog.API.Tests.Fixtures;

/// <summary>
/// Builds a Moq-backed <see cref="IConnectionMultiplexer"/> whose <see cref="IDatabase.StringSetAsync(RedisKey, RedisValue, TimeSpan?, When, CommandFlags)"/>
/// emulates a Redis <c>SETNX</c> over an in-memory key set, so the production <c>IdempotencyFilter&lt;T&gt;</c>
/// detects a replayed message id exactly as a real Redis would — without a running Redis (SDD-EVTLOG-001 §6,
/// SDD-INFRA-006 §2.5). The first set for a key returns <c>true</c>; subsequent sets return <c>false</c>.
/// </summary>
public static class FakeSetNxRedis
{
    /// <summary>Creates a SETNX-emulating multiplexer over a fresh in-memory key set.</summary>
    /// <returns>A configured <see cref="IConnectionMultiplexer"/>.</returns>
    public static IConnectionMultiplexer Create()
    {
        HashSet<string> keys = [];

        Mock<IDatabase> database = new();
        database
            .Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .Returns<RedisKey, RedisValue, TimeSpan?, When, CommandFlags>(
                (key, _, _, _, _) => Task.FromResult(keys.Add(key.ToString())));
#pragma warning disable CS0618
        database
            .Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
            .Returns<RedisKey, RedisValue, TimeSpan?, When>(
                (key, _, _, _) => Task.FromResult(keys.Add(key.ToString())));
#pragma warning restore CS0618

        Mock<IConnectionMultiplexer> multiplexer = new();
        multiplexer
            .Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);

        return multiplexer.Object;
    }
}
