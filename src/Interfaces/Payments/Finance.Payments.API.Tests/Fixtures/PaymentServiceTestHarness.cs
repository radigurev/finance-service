using AutoMapper;
using Finance.Common.Validation;
using Finance.Common.Workflow;
using Finance.Infrastructure.Audit.Models;
using Finance.Infrastructure.Sequences.Interfaces;
using Finance.Infrastructure.Services.Workflow;
using Finance.Payments.API.Services;
using Finance.Payments.API.Workflow;
using Finance.Payments.DBModel;
using Finance.Payments.DBModel.Models;
using MassTransit;
using Moq;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// Assembles a <see cref="PaymentService"/> over a SQLite in-memory <see cref="PaymentsDbContext"/> with the REAL
/// workflow engine (Draft/Confirmed/Posted/Cancelled/Reversed states plus the period workflow guard, matching the
/// composition root), the REAL <see cref="PaymentAmountCalculator"/>, a fake <c>ICountryStrategy</c>, a
/// configurable period guard and settlement-account reader, a deterministic sequence generator, and recording
/// audit/publish doubles (SDD-PAY-001 §6.1-§6.6).
/// <para>The sequence generator yields gapless counter values PER KEY so per-document-type numbering is
/// observable, and both the audit entries and the published events land on ONE shared
/// <see cref="Timeline"/> in call order so the audit-first ordering of SDD-AUDIT-001 is assertable.</para>
/// </summary>
public sealed class PaymentServiceTestHarness
{
    private PaymentServiceTestHarness(
        PaymentsDbContext db,
        PaymentService service,
        FakePaymentCountryStrategy country,
        FakePaymentPeriodGuard periodGuard,
        FakeSettlementAccountReader settlementAccounts,
        StubCorrelationIdAccessor correlation,
        FixedTimeProvider clock,
        Mock<ISequenceGenerator> sequenceMock,
        IReadOnlyDictionary<string, long> sequenceCounters,
        List<object> timeline)
    {
        Db = db;
        Service = service;
        Country = country;
        PeriodGuard = periodGuard;
        SettlementAccounts = settlementAccounts;
        Correlation = correlation;
        Clock = clock;
        SequenceMock = sequenceMock;
        SequenceCounters = sequenceCounters;
        Timeline = timeline;
    }

    /// <summary>The SQLite-backed payments context under test.</summary>
    public PaymentsDbContext Db { get; }

    /// <summary>The system under test.</summary>
    public PaymentService Service { get; }

    /// <summary>The fake country strategy driving rounding and document numbering.</summary>
    public FakePaymentCountryStrategy Country { get; }

    /// <summary>The configurable fiscal-period guard (open by default).</summary>
    public FakePaymentPeriodGuard PeriodGuard { get; }

    /// <summary>The configurable settlement-account reader (usable by default).</summary>
    public FakeSettlementAccountReader SettlementAccounts { get; }

    /// <summary>The settable ambient correlation-id accessor.</summary>
    public StubCorrelationIdAccessor Correlation { get; }

    /// <summary>The settable clock the confirm-year guard and lifecycle stamps read.</summary>
    public FixedTimeProvider Clock { get; }

    /// <summary>The mocked gapless sequence generator.</summary>
    public Mock<ISequenceGenerator> SequenceMock { get; }

    /// <summary>The last allocated counter value per sequence key, so per-type numbering is observable.</summary>
    public IReadOnlyDictionary<string, long> SequenceCounters { get; }

    /// <summary>Audit entries and published events in ONE call-ordered list, so audit-first is assertable.</summary>
    public List<object> Timeline { get; }

    /// <summary>The audit entries recorded so far, in call order.</summary>
    public IReadOnlyList<AuditEntry> RecordedAudits => [.. Timeline.OfType<AuditEntry>()];

    /// <summary>The domain events published so far, in call order.</summary>
    public IReadOnlyList<object> PublishedEvents =>
        [.. Timeline.Where(recorded => recorded is not AuditEntry)];

    /// <summary>Builds a harness over the supplied context.</summary>
    /// <param name="db">The SQLite-backed payments context.</param>
    /// <returns>A wired harness.</returns>
    public static PaymentServiceTestHarness Build(PaymentsDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        List<object> timeline = [];
        Dictionary<string, long> counters = new(StringComparer.Ordinal);

        IMapper mapper = PaymentTestMapper.Create();
        FakePaymentCountryStrategy country = new();
        FakePaymentPeriodGuard periodGuard = new();
        FakeSettlementAccountReader settlementAccounts = new();
        StubCorrelationIdAccessor correlation = new();
        FixedTimeProvider clock = new();

        Mock<ISequenceGenerator> sequenceMock = BuildSequenceMock(counters);
        Mock<IPublishEndpoint> publishMock = PaymentTestPublishEndpoint.Create(timeline);

        PaymentService service = new(
            db,
            mapper,
            correlation,
            BuildWorkflow(periodGuard),
            sequenceMock.Object,
            country,
            new PaymentAmountCalculator(country),
            periodGuard,
            settlementAccounts,
            new RecordingAuditService(timeline),
            publishMock.Object,
            new StubCurrentUserAccessor(),
            clock);

        return new PaymentServiceTestHarness(
            db,
            service,
            country,
            periodGuard,
            settlementAccounts,
            correlation,
            clock,
            sequenceMock,
            counters,
            timeline);
    }

    /// <summary>The number of gapless counter values consumed across every sequence key.</summary>
    /// <returns>The total consumed counter values.</returns>
    public long TotalSequenceValuesConsumed() => SequenceCounters.Values.Sum();

    /// <summary>The events published so far of the requested type, in call order.</summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <returns>The matching published events.</returns>
    public IReadOnlyList<TEvent> EventsOf<TEvent>() => [.. Timeline.OfType<TEvent>()];

    private static Mock<ISequenceGenerator> BuildSequenceMock(Dictionary<string, long> counters)
    {
        Mock<ISequenceGenerator> sequenceMock = new();
        sequenceMock
            .Setup(generator => generator.NextValueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
            {
                long next = counters.TryGetValue(key, out long current) ? current + 1 : 1;
                counters[key] = next;
                return next;
            });

        return sequenceMock;
    }

    private static WorkflowEngine<Payment> BuildWorkflow(FakePaymentPeriodGuard periodGuard)
    {
        List<IWorkflowState<Payment>> states =
        [
            new DraftPaymentState(),
            new ConfirmedPaymentState(),
            new PostedPaymentState(),
            new CancelledPaymentState(),
            new ReversedPaymentState()
        ];

        WorkflowStateRegistry<Payment> registry = new(states);
        List<IChainValidator<WorkflowContext<Payment>>> guards =
        [
            new PaymentPeriodWorkflowGuard(periodGuard)
        ];

        return new WorkflowEngine<Payment>(registry, guards);
    }
}
