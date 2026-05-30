namespace Finance.Infrastructure.Sequences;

/// <summary>
/// Persistent counter row backing one composite sequence key (SDD-INFRA-003 §2.2). Mapped to
/// <c>infrastructure.Sequences</c>. The physical table and migration are owned by each publishing
/// service DbContext (Batch 4+); this library ships only the entity and its configuration.
/// </summary>
public sealed class SequenceCounter
{
    /// <summary>
    /// The composite key for this counter: <c>{key}:{yyyy|yyyyMM|yyyyMMdd}</c> by reset policy,
    /// or the bare <c>{key}</c> for <see cref="SequenceResetPolicy.Never"/>.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The most recently allocated counter value for <see cref="Key"/>. Starts at one.</summary>
    public long CurrentValue { get; set; }

    /// <summary>The UTC-offset timestamp of the last increment, defaulted by <c>SYSDATETIMEOFFSET()</c>.</summary>
    public DateTimeOffset ModifiedAt { get; set; }
}
