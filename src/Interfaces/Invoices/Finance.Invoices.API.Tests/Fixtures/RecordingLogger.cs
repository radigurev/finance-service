using Microsoft.Extensions.Logging;

namespace Finance.Invoices.API.Tests.Fixtures;

/// <summary>
/// A minimal <see cref="ILogger{TCategoryName}"/> that records the level and rendered message of every entry, so
/// the SDD-INV-001 §2.15 step 4 DERIVATION-DISAGREEMENT warning — a structured log line, not a state change — is
/// assertable without a logging framework.
/// </summary>
/// <typeparam name="TCategory">The logger category.</typeparam>
public sealed class RecordingLogger<TCategory> : ILogger<TCategory>
{
    /// <summary>Every recorded entry, in call order.</summary>
    public List<RecordedLogEntry> Entries { get; } = [];

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Entries.Add(new RecordedLogEntry(logLevel, formatter(state, exception)));
    }

    private sealed class NullScope : IDisposable
    {
        internal static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
