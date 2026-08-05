using Microsoft.Extensions.Logging;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>One log line captured by <see cref="RecordingLogger{TCategory}"/>: its level and rendered message.</summary>
/// <param name="Level">The severity the entry was written at.</param>
/// <param name="Message">The rendered message text.</param>
public sealed record RecordedLogEntry(LogLevel Level, string Message);
