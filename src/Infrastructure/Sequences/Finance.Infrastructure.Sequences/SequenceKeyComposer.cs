using System.Globalization;

namespace Finance.Infrastructure.Sequences;

/// <summary>
/// Computes the period segment and composite counter key for a sequence by reset policy
/// (SDD-INFRA-003 §2.2): <c>{key}:{yyyy}</c> Yearly, <c>{key}:{yyyyMM}</c> Monthly,
/// <c>{key}:{yyyyMMdd}</c> Daily, and the bare <c>{key}</c> for Never.
/// </summary>
public static class SequenceKeyComposer
{
    /// <summary>
    /// Builds the reset-policy date segment for <paramref name="moment"/> (empty for
    /// <see cref="SequenceResetPolicy.Never"/>).
    /// </summary>
    /// <param name="resetPolicy">The reset policy of the sequence.</param>
    /// <param name="moment">The reference instant (typically now).</param>
    /// <returns>The period segment string, or empty for <see cref="SequenceResetPolicy.Never"/>.</returns>
    public static string PeriodSegment(SequenceResetPolicy resetPolicy, DateTimeOffset moment)
    {
        return resetPolicy switch
        {
            SequenceResetPolicy.Yearly => moment.ToString("yyyy", CultureInfo.InvariantCulture),
            SequenceResetPolicy.Monthly => moment.ToString("yyyyMM", CultureInfo.InvariantCulture),
            SequenceResetPolicy.Daily => moment.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Builds the composite counter key for <paramref name="sequenceKey"/> under
    /// <paramref name="resetPolicy"/> at <paramref name="moment"/>.
    /// </summary>
    /// <param name="sequenceKey">The base sequence key (e.g. <c>JE</c>).</param>
    /// <param name="resetPolicy">The reset policy of the sequence.</param>
    /// <param name="moment">The reference instant (typically now).</param>
    /// <returns>The composite key persisted in <c>infrastructure.Sequences</c>.</returns>
    public static string CompositeKey(string sequenceKey, SequenceResetPolicy resetPolicy, DateTimeOffset moment)
    {
        string segment = PeriodSegment(resetPolicy, moment);
        if (string.IsNullOrEmpty(segment))
        {
            return sequenceKey;
        }

        return string.Concat(sequenceKey, ":", segment);
    }
}
