using AutoMapper;
using Finance.Common.Validation;
using Finance.Common.Workflow;
using Finance.Infrastructure.Audit;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Services.Workflow;
using Finance.Periods.API.Mapping;
using Finance.Periods.API.Services;
using Finance.Periods.API.Workflow;
using Finance.Periods.DBModel;
using Finance.Periods.DBModel.Models;
using Finance.ServiceModel.Events.Periods;
using Finance.ServiceModel.Periods;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Finance.Periods.API.Tests.Fixtures;

/// <summary>
/// Assembles a <see cref="FiscalPeriodService"/> over a SQLite in-memory <see cref="PeriodsDbContext"/> with
/// the real fiscal-period workflow engine (Open/Closed states + the ordering guard), the real calendar-month
/// fiscal calendar, the real write-path audit service, the recording reference cache, and a mocked
/// <see cref="IPublishEndpoint"/> that captures published events in order for the Periods unit tests
/// (SDD-FIN-004 §6). The ordering guard shares the same context instance as the service so sibling-period
/// checks observe in-flight state. The publish mock additionally captures the count of audit rows already
/// tracked at publish time, so audit-first ordering (SDD-AUDIT-001 §2.4) is observable offline.
/// </summary>
public sealed class FiscalPeriodServiceTestHarness
{
    private FiscalPeriodServiceTestHarness(
        PeriodsDbContext db,
        FiscalPeriodService service,
        RecordingCacheService<FiscalPeriodDto> cache,
        Mock<IPublishEndpoint> publishMock,
        List<object> publishedEvents,
        List<int> auditRowsTrackedAtPublishTime)
    {
        Db = db;
        Service = service;
        Cache = cache;
        PublishMock = publishMock;
        PublishedEvents = publishedEvents;
        AuditRowsTrackedAtPublishTime = auditRowsTrackedAtPublishTime;
    }

    /// <summary>The SQLite-backed periods context under test.</summary>
    public PeriodsDbContext Db { get; }

    /// <summary>The system under test.</summary>
    public FiscalPeriodService Service { get; }

    /// <summary>The recording reference cache (records every invalidation pattern).</summary>
    public RecordingCacheService<FiscalPeriodDto> Cache { get; }

    /// <summary>The mocked publish endpoint capturing published domain events in call order.</summary>
    public Mock<IPublishEndpoint> PublishMock { get; }

    /// <summary>The domain events captured by <see cref="IPublishEndpoint"/>, in call order.</summary>
    public List<object> PublishedEvents { get; }

    /// <summary>
    /// For each publish call, the number of <c>audit.OperationsEvents</c> rows already tracked on the context
    /// at publish time. A non-zero value proves the audit row was added before the outbox publish.
    /// </summary>
    public List<int> AuditRowsTrackedAtPublishTime { get; }

    /// <summary>Builds a harness over the supplied SQLite context.</summary>
    /// <param name="db">The SQLite-backed periods context.</param>
    /// <returns>A wired harness.</returns>
    public static FiscalPeriodServiceTestHarness Build(PeriodsDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        List<object> publishedEvents = [];
        List<int> auditRowsTrackedAtPublishTime = [];

        IMapper mapper = new MapperConfiguration(cfg => cfg.AddProfile<PeriodMappingProfile>())
            .CreateMapper();

        WorkflowEngine<FiscalPeriod> workflow = BuildWorkflow(db);

        IAuditService audit = new AuditService<PeriodsDbContext>(
            db, NullLogger<AuditService<PeriodsDbContext>>.Instance);

        RecordingCacheService<FiscalPeriodDto> cache = new();

        Mock<IPublishEndpoint> publishMock = new();
        publishMock
            .Setup(p => p.Publish(It.IsAny<FiscalPeriodClosedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<FiscalPeriodClosedEvent, CancellationToken>((message, _) =>
                CapturePublish(message, db, publishedEvents, auditRowsTrackedAtPublishTime))
            .Returns(Task.CompletedTask);
        publishMock
            .Setup(p => p.Publish(It.IsAny<FiscalPeriodReopenedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<FiscalPeriodReopenedEvent, CancellationToken>((message, _) =>
                CapturePublish(message, db, publishedEvents, auditRowsTrackedAtPublishTime))
            .Returns(Task.CompletedTask);

        FiscalPeriodService service = new(
            db,
            mapper,
            new StubCorrelationIdAccessor(),
            new CalendarMonthFiscalCalendar(),
            workflow,
            audit,
            publishMock.Object,
            cache,
            new StubCurrentUserAccessor());

        return new FiscalPeriodServiceTestHarness(
            db, service, cache, publishMock, publishedEvents, auditRowsTrackedAtPublishTime);
    }

    private static void CapturePublish(
        object message,
        PeriodsDbContext db,
        List<object> publishedEvents,
        List<int> auditRowsTrackedAtPublishTime)
    {
        publishedEvents.Add(message);
        auditRowsTrackedAtPublishTime.Add(
            db.ChangeTracker.Entries<Finance.Infrastructure.Audit.Entities.OperationsEvent>().Count());
    }

    private static WorkflowEngine<FiscalPeriod> BuildWorkflow(PeriodsDbContext db)
    {
        List<IWorkflowState<FiscalPeriod>> states =
        [
            new OpenFiscalPeriodState(),
            new ClosedFiscalPeriodState()
        ];

        WorkflowStateRegistry<FiscalPeriod> registry = new(states);
        List<IChainValidator<WorkflowContext<FiscalPeriod>>> guards =
        [
            new PeriodOrderingWorkflowGuard(db)
        ];

        return new WorkflowEngine<FiscalPeriod>(registry, guards);
    }
}
