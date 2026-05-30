namespace Finance.Infrastructure.Sequences;

/// <summary>
/// Determines how often a sequence counter resets to one, controlling the composite
/// key that the generator increments. <see cref="Yearly"/> is the НАП-required default
/// for fiscal document numbering (SDD-INFRA-003 §2.2).
/// </summary>
public enum SequenceResetPolicy
{
    /// <summary>The counter never resets; the composite key is the bare sequence key.</summary>
    Never = 0,

    /// <summary>The counter resets each fiscal year; composite key is <c>{key}:{yyyy}</c>. Required for НАП.</summary>
    Yearly = 1,

    /// <summary>The counter resets each calendar month; composite key is <c>{key}:{yyyyMM}</c>.</summary>
    Monthly = 2,

    /// <summary>The counter resets each calendar day; composite key is <c>{key}:{yyyyMMdd}</c>.</summary>
    Daily = 3
}
