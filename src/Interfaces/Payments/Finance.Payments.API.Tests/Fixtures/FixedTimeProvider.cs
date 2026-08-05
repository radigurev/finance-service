namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// A deterministic, settable <see cref="TimeProvider"/> for the Payments unit tests. <c>TimeProvider</c> is not
/// registered explicitly in <c>Finance.Payments.API</c> — it arrives via <c>AddSequenceGenerator</c>'s
/// <c>TryAddSingleton(TimeProvider.System)</c> — yet <c>PaymentService</c>, <c>PaymentAllocationService</c>,
/// <c>AgingService</c>, <c>InvoiceOpenItemProjection</c>, and both date validators depend on it. It is the lever
/// for the confirm-clock-year guard (<c>PAYMENT_DATE_YEAR_MISMATCH</c>, SDD-PAY-001 §2.4), the future-date rules
/// (SDD-PAY-001 §3.1, SDD-PAY-003 §2.3), and the historical as-of path (SDD-PAY-003 §2.3).
/// </summary>
public sealed class FixedTimeProvider : TimeProvider
{
    /// <summary>The default "now" every harness starts from: mid-2026, so a 2026 payment date is valid.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Creates a provider pinned to <see cref="DefaultNow"/>.</summary>
    public FixedTimeProvider()
        : this(DefaultNow)
    {
    }

    /// <summary>Creates a provider pinned to the supplied instant.</summary>
    /// <param name="now">The instant every call returns until <see cref="UtcNow"/> is changed.</param>
    public FixedTimeProvider(DateTimeOffset now)
    {
        UtcNow = now;
    }

    /// <summary>The instant <see cref="GetUtcNow"/> returns; settable so a test can advance the clock.</summary>
    public DateTimeOffset UtcNow { get; set; }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => UtcNow;
}
