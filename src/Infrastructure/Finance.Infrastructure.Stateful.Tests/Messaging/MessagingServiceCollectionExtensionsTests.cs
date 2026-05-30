using Finance.Infrastructure.Messaging;
using Finance.Infrastructure.Stateful.Tests.Messaging.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Finance.Infrastructure.Stateful.Tests.Messaging;

/// <summary>
/// Unit tests for the startup fail-fast guards of
/// <see cref="MessagingServiceCollectionExtensions.AddFinanceMessageBus{TDbContext}(IServiceCollection, IConfiguration)"/>
/// (SDD-INFRA-006 §3). Registration MUST throw <see cref="InvalidOperationException"/> when
/// <c>RabbitMQ:Host</c> or <c>ConnectionStrings:Redis</c> is missing, before the bus is wired. With both
/// present the MassTransit registration is configured but not started, so registration MUST NOT throw or
/// connect to the broker.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-006")]
public sealed class MessagingServiceCollectionExtensionsTests
{
    /// <summary>Missing RabbitMQ:Host fails fast at registration with InvalidOperationException.</summary>
    [Test]
    public void AddFinanceMessageBus_MissingRabbitMqHost_ThrowsInvalidOperationException()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:6379,abortConnect=false"
            })
            .Build();

        // Act & Assert
        Assert.That(
            () => services.AddFinanceMessageBus<StubMessagingDbContext>(configuration),
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>Missing ConnectionStrings:Redis fails fast at registration with InvalidOperationException.</summary>
    [Test]
    public void AddFinanceMessageBus_MissingRedisConnectionString_ThrowsInvalidOperationException()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:Host"] = "localhost"
            })
            .Build();

        // Act & Assert
        Assert.That(
            () => services.AddFinanceMessageBus<StubMessagingDbContext>(configuration),
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>A whitespace-only RabbitMQ:Host is treated as missing and fails fast.</summary>
    [Test]
    public void AddFinanceMessageBus_WhitespaceRabbitMqHost_ThrowsInvalidOperationException()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:Host"] = "   ",
                ["ConnectionStrings:Redis"] = "localhost:6379,abortConnect=false"
            })
            .Build();

        // Act & Assert
        Assert.That(
            () => services.AddFinanceMessageBus<StubMessagingDbContext>(configuration),
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>With RabbitMQ:Host and ConnectionStrings:Redis present, registration completes without throwing.</summary>
    [Test]
    public void AddFinanceMessageBus_WithRabbitMqHostAndRedis_DoesNotThrow()
    {
        // Arrange
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:Host"] = "localhost",
                ["RabbitMQ:Username"] = "guest",
                ["RabbitMQ:Password"] = "guest",
                ["ConnectionStrings:Redis"] = "localhost:6379,abortConnect=false"
            })
            .Build();

        // Act & Assert
        Assert.That(
            () => services.AddFinanceMessageBus<StubMessagingDbContext>(configuration),
            Throws.Nothing);
    }
}
