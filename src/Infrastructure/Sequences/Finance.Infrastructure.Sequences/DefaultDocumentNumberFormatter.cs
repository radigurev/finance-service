using System.Globalization;
using Finance.Common.ErrorCodes;
using Finance.Infrastructure.Sequences.Interfaces;

namespace Finance.Infrastructure.Sequences;

/// <summary>
/// BG-style document-number formatter (SDD-INFRA-003 §2.1, §2.6). Emits
/// <c>{prefix}-{periodSegment}-{zero-padded counter}</c> for period-reset sequences and
/// <c>{prefix}-{zero-padded counter}</c> for <see cref="SequenceResetPolicy.Never"/>. This is
/// the default seam implementation; a country-specific formatter replaces it once SDD-CTRY-001 exists.
/// </summary>
public sealed class DefaultDocumentNumberFormatter : IDocumentNumberFormatter
{
    private readonly IReadOnlyDictionary<string, SequenceDefinition> _definitions;

    /// <summary>
    /// Initializes the formatter with the registered sequence definitions used to resolve
    /// per-key prefix and padding.
    /// </summary>
    /// <param name="definitions">The sequence definitions keyed by sequence key.</param>
    public DefaultDocumentNumberFormatter(IReadOnlyDictionary<string, SequenceDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _definitions = definitions;
    }

    /// <inheritdoc />
    public string Format(string sequenceKey, string periodSegment, long counter)
    {
        if (!_definitions.TryGetValue(sequenceKey, out SequenceDefinition? definition))
        {
            throw new ArgumentException(SequenceErrorCodes.UNKNOWN_SEQUENCE_KEY, nameof(sequenceKey));
        }

        string padded = counter.ToString(CultureInfo.InvariantCulture).PadLeft(definition.Padding, '0');
        if (string.IsNullOrEmpty(periodSegment))
        {
            return string.Concat(definition.Prefix, "-", padded);
        }

        return string.Concat(definition.Prefix, "-", periodSegment, "-", padded);
    }
}
