using Finance.Common.Abstractions;

namespace Finance.Infrastructure.Tests.Services.Fixtures;

/// <summary>A fixed-value <see cref="ICorrelationIdAccessor"/> used by the service-layer tests.</summary>
public sealed class TestCorrelationIdAccessor : ICorrelationIdAccessor
{
    /// <inheritdoc />
    public string Get() => "test-correlation-id";
}
