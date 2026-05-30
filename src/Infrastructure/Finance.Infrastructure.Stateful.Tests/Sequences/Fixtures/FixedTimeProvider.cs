namespace Finance.Infrastructure.Stateful.Tests.Sequences.Fixtures;

/// <summary>
/// A <see cref="TimeProvider"/> that always returns a fixed UTC instant so period-segment and
/// fiscal-year computation in the sequence generator is deterministic in unit tests.
/// </summary>
public sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    /// <summary>Initializes the provider with the instant it should always return.</summary>
    /// <param name="now">The fixed instant returned by <see cref="GetUtcNow"/>.</param>
    public FixedTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;
}
