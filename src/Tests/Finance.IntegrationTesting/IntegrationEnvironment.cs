namespace Finance.IntegrationTesting;

/// <summary>
/// Publishes the test infrastructure connection details as process environment variables BEFORE any
/// host is created. The Finance services read all configuration (JWT keys, connection strings,
/// RabbitMQ settings) eagerly during <c>ConfigureServices</c> — i.e. before <c>builder.Build()</c> —
/// so a <c>WebApplicationFactory.ConfigureAppConfiguration</c> source would be applied too late.
/// Environment variables are read when the <c>WebApplicationBuilder</c> is created, so they reach the
/// startup validation and DI registration. Called once per assembly from the integration SetUpFixture
/// after the containers start.
/// </summary>
public static class IntegrationEnvironment
{
    /// <summary>
    /// Sets the shared (JWT, Redis, RabbitMQ, country) environment plus the service-specific database
    /// connection string under the supplied <paramref name="connectionStringKey"/>.
    /// </summary>
    public static void Apply(FinanceContainers containers, string connectionStringKey, string databaseName)
    {
        Set($"ConnectionStrings__{connectionStringKey}", containers.SqlConnectionStringForDatabase(databaseName));
        Set("ConnectionStrings__Redis", containers.RedisConnectionString);

        Set("RabbitMQ__Host", containers.RabbitMqHost);
        Set("RabbitMQ__Port", containers.RabbitMqPort.ToString());
        Set("RabbitMQ__VirtualHost", "/");
        Set("RabbitMQ__Username", containers.RabbitMqUsername);
        Set("RabbitMQ__Password", containers.RabbitMqPassword);

        Set("Jwt__SecretKey", TestTokens.SecretKey);
        Set("Jwt__Issuer", TestTokens.Issuer);
        Set("Jwt__Audience", TestTokens.Audience);
        Set("Jwt__AccessTokenExpirationMinutes", "30");
        Set("Jwt__RefreshTokenExpirationDays", "7");

        Set("PermissionValidation__AuthApiBaseAddress", "http://localhost:65535");
        Set("Country__Code", "BG");
    }

    private static void Set(string key, string value) =>
        Environment.SetEnvironmentVariable(key, value);
}
