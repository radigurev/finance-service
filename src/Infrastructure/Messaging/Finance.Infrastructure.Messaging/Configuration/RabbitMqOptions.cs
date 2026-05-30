namespace Finance.Infrastructure.Messaging.Configuration;

/// <summary>
/// Strongly-typed RabbitMQ transport settings bound from the <c>RabbitMQ</c> configuration section
/// (SDD-INFRA-006 §3). The <see cref="Host"/> is mandatory; startup MUST fail when it is missing.
/// </summary>
public sealed class RabbitMqOptions
{
    /// <summary>The configuration section name these options bind from.</summary>
    public const string SectionName = "RabbitMQ";

    /// <summary>Gets or sets the broker host name. REQUIRED — startup fails when absent.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the AMQP virtual host. Defaults to the shared Warehouse vhost root.</summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>Gets or sets the broker port. Defaults to the standard AMQP port.</summary>
    public ushort Port { get; set; } = 5672;

    /// <summary>Gets or sets the broker user name.</summary>
    public string Username { get; set; } = "guest";

    /// <summary>Gets or sets the broker password.</summary>
    public string Password { get; set; } = "guest";
}
