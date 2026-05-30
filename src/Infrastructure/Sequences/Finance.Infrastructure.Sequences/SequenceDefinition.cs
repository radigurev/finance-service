namespace Finance.Infrastructure.Sequences;

/// <summary>
/// Immutable description of a registered sequence: its key, BG-default prefix, zero-padding
/// width, and reset policy. The set of built-in definitions is supplied by
/// <see cref="SequenceDefinitions"/> (SDD-INFRA-003 §2.1). Per-country definitions arrive
/// with SDD-CTRY-001.
/// </summary>
public sealed record SequenceDefinition
{
    /// <summary>The unique sequence key (e.g. <c>JE</c>, <c>PINV</c>). Non-empty.</summary>
    public required string Key { get; init; }

    /// <summary>The BG-default document-number prefix emitted by <see cref="DefaultDocumentNumberFormatter"/>.</summary>
    public required string Prefix { get; init; }

    /// <summary>The zero-padding width applied to the counter. MUST be between 1 and 12 inclusive.</summary>
    public required int Padding { get; init; }

    /// <summary>The reset policy controlling the composite counter key and fiscal-year segment.</summary>
    public required SequenceResetPolicy ResetPolicy { get; init; }
}
