using AutoMapper;
using Finance.Common.Results;
using Finance.Common.Validation;
using Finance.Common.Workflow;
using Finance.Country.Abstractions;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Audit.Models;
using Finance.Infrastructure.Sequences.Interfaces;
using Finance.Infrastructure.Services.Workflow;
using Finance.Invoices.API.Mapping;
using Finance.Invoices.API.Services;
using Finance.Invoices.API.Workflow;
using Finance.Invoices.DBModel;
using Finance.Invoices.DBModel.Models;
using Finance.ServiceModel.Events.Invoices;
using MassTransit;
using Moq;

namespace Finance.Invoices.API.Tests.Fixtures;

/// <summary>
/// Assembles an <see cref="InvoiceService"/> over a SQLite in-memory <see cref="InvoicesDbContext"/> with the
/// real workflow engine (Draft/Confirmed/Posted/Cancelled/Reversed states + the period-open guard), the real
/// totals calculator, a fake <see cref="ICountryStrategy"/>, a fake period guard, and mocked sequence, audit,
/// and publish dependencies (SDD-INV-001 §6.1-§6.5). The sequence generator yields deterministic gapless
/// counter values per key so per-document-type numbering is observable; the publish endpoint captures
/// published events in call order so the outbox handshake can be asserted via the in-memory mock.
/// </summary>
public sealed class InvoiceServiceTestHarness
{
    private InvoiceServiceTestHarness(
        InvoicesDbContext db,
        InvoiceService service,
        FakeInvoiceCountryStrategy country,
        FakeInvoicePeriodGuard periodGuard,
        Mock<ISequenceGenerator> sequenceMock,
        Mock<IAuditService> auditMock,
        Mock<IPublishEndpoint> publishMock,
        IReadOnlyDictionary<string, int> sequenceCounters,
        List<AuditEntry> recordedAudits,
        List<object> publishedEvents)
    {
        Db = db;
        Service = service;
        Country = country;
        PeriodGuard = periodGuard;
        SequenceMock = sequenceMock;
        AuditMock = auditMock;
        PublishMock = publishMock;
        SequenceCounters = sequenceCounters;
        RecordedAudits = recordedAudits;
        PublishedEvents = publishedEvents;
    }

    /// <summary>The SQLite-backed invoices context under test.</summary>
    public InvoicesDbContext Db { get; }

    /// <summary>The system under test.</summary>
    public InvoiceService Service { get; }

    /// <summary>The fake country strategy driving tax rounding and document numbering.</summary>
    public FakeInvoiceCountryStrategy Country { get; }

    /// <summary>The configurable period guard (open by default; set closed to exercise the FIN-004 seam).</summary>
    public FakeInvoicePeriodGuard PeriodGuard { get; }

    /// <summary>The mocked gapless sequence generator yielding deterministic per-key counter values.</summary>
    public Mock<ISequenceGenerator> SequenceMock { get; }

    /// <summary>The mocked audit service capturing recorded audit entries in call order.</summary>
    public Mock<IAuditService> AuditMock { get; }

    /// <summary>The mocked publish endpoint capturing published domain events in call order.</summary>
    public Mock<IPublishEndpoint> PublishMock { get; }

    /// <summary>The last allocated counter value per sequence key (so per-type numbering is observable).</summary>
    public IReadOnlyDictionary<string, int> SequenceCounters { get; }

    /// <summary>The audit entries captured by <see cref="IAuditService.RecordAsync"/>, in call order.</summary>
    public List<AuditEntry> RecordedAudits { get; }

    /// <summary>The domain events captured by <see cref="IPublishEndpoint"/>, in call order.</summary>
    public List<object> PublishedEvents { get; }

    /// <summary>Builds a harness over the supplied context with a period guard that allows every period.</summary>
    /// <param name="db">The SQLite-backed invoices context.</param>
    /// <returns>A wired harness.</returns>
    public static InvoiceServiceTestHarness Build(InvoicesDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        List<AuditEntry> recordedAudits = [];
        List<object> publishedEvents = [];
        Dictionary<string, int> counters = new(StringComparer.Ordinal);

        IMapper mapper = new MapperConfiguration(cfg => cfg.AddProfile<InvoiceMappingProfile>())
            .CreateMapper();

        FakeInvoiceCountryStrategy country = new();
        FakeInvoicePeriodGuard periodGuard = new();
        InvoiceTotalsCalculator totals = new(country);
        WorkflowEngine<Invoice> workflow = BuildWorkflow(periodGuard);

        Mock<ISequenceGenerator> sequenceMock = new();
        sequenceMock
            .Setup(s => s.NextValueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
            {
                int next = counters.TryGetValue(key, out int current) ? current + 1 : 1;
                counters[key] = next;
                return next;
            });

        Mock<IAuditService> auditMock = new();
        auditMock
            .Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Callback<AuditEntry, CancellationToken, bool>((entry, _, _) => recordedAudits.Add(entry))
            .ReturnsAsync(Result.Success());

        Mock<IPublishEndpoint> publishMock = new();
        publishMock
            .Setup(p => p.Publish(It.IsAny<InvoiceConfirmedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<InvoiceConfirmedEvent, CancellationToken>((message, _) => publishedEvents.Add(message))
            .Returns(Task.CompletedTask);
        publishMock
            .Setup(p => p.Publish(It.IsAny<InvoiceCancelledEvent>(), It.IsAny<CancellationToken>()))
            .Callback<InvoiceCancelledEvent, CancellationToken>((message, _) => publishedEvents.Add(message))
            .Returns(Task.CompletedTask);
        publishMock
            .Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((message, _) => publishedEvents.Add(message))
            .Returns(Task.CompletedTask);

        InvoiceService service = new(
            db,
            mapper,
            new StubCorrelationIdAccessor(),
            workflow,
            sequenceMock.Object,
            country,
            totals,
            periodGuard,
            auditMock.Object,
            publishMock.Object,
            new StubCurrentUserAccessor());

        return new InvoiceServiceTestHarness(
            db,
            service,
            country,
            periodGuard,
            sequenceMock,
            auditMock,
            publishMock,
            counters,
            recordedAudits,
            publishedEvents);
    }

    private static WorkflowEngine<Invoice> BuildWorkflow(FakeInvoicePeriodGuard periodGuard)
    {
        List<IWorkflowState<Invoice>> states =
        [
            new DraftInvoiceState(),
            new ConfirmedInvoiceState(),
            new PostedInvoiceState(),
            new CancelledInvoiceState(),
            new ReversedInvoiceState()
        ];

        WorkflowStateRegistry<Invoice> registry = new(states);
        List<IChainValidator<WorkflowContext<Invoice>>> guards =
        [
            new InvoicePeriodWorkflowGuard(periodGuard)
        ];

        return new WorkflowEngine<Invoice>(registry, guards);
    }
}
