using Testcontainers.MsSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Finance.IntegrationTesting;

/// <summary>
/// Owns the disposable SQL Server, Redis, and RabbitMQ containers shared by an integration-test
/// assembly. Started once per assembly via a <c>[SetUpFixture]</c> and exposes the connection
/// details the <see cref="FinanceApiFactory{TProgram}"/> injects into the host under test.
/// </summary>
public sealed class FinanceContainers : IAsyncDisposable
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7.4-alpine")
        .Build();

    private const string RabbitMqUser = "finance";
    private const string RabbitMqPass = "finance";

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:4.1-management-alpine")
        .WithUsername(RabbitMqUser)
        .WithPassword(RabbitMqPass)
        .Build();

    /// <summary>The base SQL Server connection string (no specific database selected).</summary>
    public string SqlConnectionString => _sql.GetConnectionString();

    /// <summary>The Redis connection string for the cache/idempotency stores.</summary>
    public string RedisConnectionString => _redis.GetConnectionString();

    /// <summary>The mapped RabbitMQ host (always localhost for the test host).</summary>
    public string RabbitMqHost => _rabbitMq.Hostname;

    /// <summary>The mapped RabbitMQ AMQP port.</summary>
    public ushort RabbitMqPort => _rabbitMq.GetMappedPublicPort(5672);

    /// <summary>The RabbitMQ username for the test broker.</summary>
    public string RabbitMqUsername => RabbitMqUser;

    /// <summary>The RabbitMQ password for the test broker.</summary>
    public string RabbitMqPassword => RabbitMqPass;

    /// <summary>Starts all three containers in parallel.</summary>
    public async Task StartAsync()
    {
        await Task.WhenAll(
            _sql.StartAsync(),
            _redis.StartAsync(),
            _rabbitMq.StartAsync()).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a connection string targeting the named database on the test SQL Server, so each
    /// service gets its own database (database-per-service parity with production).
    /// </summary>
    public string SqlConnectionStringForDatabase(string databaseName)
    {
        Microsoft.Data.SqlClient.SqlConnectionStringBuilder builder =
            new(SqlConnectionString) { InitialCatalog = databaseName };
        return builder.ConnectionString;
    }

    /// <summary>Disposes all three containers.</summary>
    public async ValueTask DisposeAsync()
    {
        await _sql.DisposeAsync().ConfigureAwait(false);
        await _redis.DisposeAsync().ConfigureAwait(false);
        await _rabbitMq.DisposeAsync().ConfigureAwait(false);
    }
}
