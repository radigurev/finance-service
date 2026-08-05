using AutoMapper;
using Finance.Common.Validation;
using Finance.Infrastructure.Audit.Models;
using Finance.Payments.API.Interfaces;
using Finance.Payments.API.Services;
using Finance.Payments.API.Validation;
using Finance.Payments.DBModel;
using MassTransit;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// Assembles a <see cref="PaymentAllocationService"/> over a SQLite in-memory <see cref="PaymentsDbContext"/> with
/// the REAL ten-rule invariant chain in the documented registration order, the REAL
/// <see cref="AllocationAmountCalculator"/> and <see cref="SettlementStatusCalculator"/>, a recording realized-FX
/// seam, and recording audit/publish doubles (SDD-PAY-002 §6.1-§6.5).
/// <para>Audit entries and published events land on ONE shared <see cref="Timeline"/> in call order, so the
/// audit-first ordering of SDD-PAY-002 §2.11 and the one-event-per-row rule of §2.10 are assertable.</para>
/// <para><b>Reads MUST go through <c>ListAsync</c>.</b> The inherited <c>SearchAsync</c> from
/// <c>SearchableServiceBase</c> is unscoped and has no <c>PaymentAllocation → PaymentAllocationDto</c> map — only
/// the enriched <c>PaymentAllocationProjectionRow</c> map exists — so it is deliberately never exercised here.</para>
/// </summary>
public sealed class PaymentAllocationTestHarness
{
    private PaymentAllocationTestHarness(
        PaymentsDbContext db,
        PaymentAllocationService service,
        FakePaymentCountryStrategy country,
        RecordingRealizedFxHandler realizedFx,
        StubCorrelationIdAccessor correlation,
        FixedTimeProvider clock,
        ValidationChain<PaymentAllocationValidationContext> chain,
        List<object> timeline)
    {
        Db = db;
        Service = service;
        Country = country;
        RealizedFx = realizedFx;
        Correlation = correlation;
        Clock = clock;
        Chain = chain;
        Timeline = timeline;
    }

    /// <summary>The SQLite-backed payments context under test.</summary>
    public PaymentsDbContext Db { get; }

    /// <summary>The system under test.</summary>
    public PaymentAllocationService Service { get; }

    /// <summary>The fake country strategy owning monetary rounding.</summary>
    public FakePaymentCountryStrategy Country { get; }

    /// <summary>The recording realized-FX seam (SDD-PAY-002 §2.9).</summary>
    public RecordingRealizedFxHandler RealizedFx { get; }

    /// <summary>The settable ambient correlation-id accessor.</summary>
    public StubCorrelationIdAccessor Correlation { get; }

    /// <summary>The settable clock stamping allocation times and the event ordering token.</summary>
    public FixedTimeProvider Clock { get; }

    /// <summary>The real invariant chain, so a test may exercise it directly.</summary>
    public ValidationChain<PaymentAllocationValidationContext> Chain { get; }

    /// <summary>Audit entries and published events in ONE call-ordered list.</summary>
    public List<object> Timeline { get; }

    /// <summary>The audit entries recorded so far, in call order.</summary>
    public IReadOnlyList<AuditEntry> RecordedAudits => [.. Timeline.OfType<AuditEntry>()];

    /// <summary>The domain events published so far, in call order.</summary>
    public IReadOnlyList<object> PublishedEvents =>
        [.. Timeline.Where(recorded => recorded is not AuditEntry)];

    /// <summary>The ten invariant validators in the SDD-PAY-002 §2.5 registration order.</summary>
    /// <returns>The chain validators, ordered.</returns>
    public static IReadOnlyList<IChainValidator<PaymentAllocationValidationContext>> DocumentedChainOrder() =>
    [
        new PaymentAllocatableValidator(),
        new AllocationInvoiceKnownValidator(),
        new AllocationInvoiceEligibleValidator(),
        new AllocationDirectionValidator(),
        new AllocationCounterpartyValidator(),
        new AllocationCurrencyValidator(),
        new AllocationDuplicateValidator(),
        new AllocationWithinPaymentValidator(),
        new AllocationWithinOutstandingValidator(),
        new AllocationControlAccountValidator()
    ];

    /// <summary>Builds a harness over the supplied context.</summary>
    /// <param name="db">The SQLite-backed payments context.</param>
    /// <param name="realizedFxHandler">An optional realized-FX seam override (the recording double by default).</param>
    /// <returns>A wired harness.</returns>
    public static PaymentAllocationTestHarness Build(
        PaymentsDbContext db,
        IRealizedFxHandler? realizedFxHandler = null)
    {
        ArgumentNullException.ThrowIfNull(db);

        List<object> timeline = [];
        IMapper mapper = PaymentTestMapper.Create();
        FakePaymentCountryStrategy country = new();
        RecordingRealizedFxHandler recordingFx = new();
        StubCorrelationIdAccessor correlation = new();
        FixedTimeProvider clock = new();
        ValidationChain<PaymentAllocationValidationContext> chain = new(DocumentedChainOrder());

        PaymentAllocationService service = new(
            db,
            mapper,
            correlation,
            chain,
            new AllocationAmountCalculator(country),
            new SettlementStatusCalculator(),
            realizedFxHandler ?? recordingFx,
            new RecordingAuditService(timeline),
            PaymentTestPublishEndpoint.Create(timeline).Object,
            new StubCurrentUserAccessor(),
            clock);

        return new PaymentAllocationTestHarness(
            db,
            service,
            country,
            recordingFx,
            correlation,
            clock,
            chain,
            timeline);
    }

    /// <summary>The events published so far of the requested type, in call order.</summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <returns>The matching published events.</returns>
    public IReadOnlyList<TEvent> EventsOf<TEvent>() => [.. Timeline.OfType<TEvent>()];
}
