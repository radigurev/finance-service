using AutoMapper;
using Finance.Common.Results;
using Finance.Common.Validation;
using Finance.Common.Workflow;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Audit.Models;
using Finance.Infrastructure.Sequences.Interfaces;
using Finance.Infrastructure.Services.Workflow;
using Finance.Journal.API.Interfaces;
using Finance.Journal.API.Mapping;
using Finance.Journal.API.Services;
using Finance.Journal.API.Validation;
using Finance.Journal.API.Validators;
using Finance.Journal.API.Workflow;
using Finance.Journal.DBModel;
using Finance.Journal.DBModel.Models;
using Finance.ServiceModel.Events.Journal;
using MassTransit;
using Moq;

namespace Finance.Journal.API.Tests.Fixtures;

/// <summary>
/// Assembles a <see cref="JournalEntryService"/> over a SQLite in-memory <see cref="JournalDbContext"/>
/// with the real double-entry validation surface, a real workflow engine (Draft/Posted/Reversed states +
/// the posting-period guard), and faked sequence, audit, publish, and period-guard dependencies for the
/// Journal unit tests (SDD-FIN-001 §6, SDD-FIN-002 §6). The reference-data reader is the in-memory
/// <see cref="FakeReferenceDataReader"/> so postability/currency checks run without HTTP. The sequence
/// generator yields deterministic gapless <c>JE-2026-000001</c>… numbers so posting/reversal numbering is
/// observable; the publish endpoint captures published events in order.
/// </summary>
public sealed class JournalEntryServiceTestHarness
{
    /// <summary>The base currency frozen onto created entries (SDD-FIN-002 §2.3).</summary>
    public const string BaseCurrencyCode = "BGN";

    private JournalEntryServiceTestHarness(
        JournalDbContext db,
        JournalEntryService service,
        FakeReferenceDataReader referenceData,
        Mock<IPostingPeriodGuard> periodGuardMock,
        Mock<ISequenceGenerator> sequenceMock,
        Mock<IAuditService> auditMock,
        Mock<IPublishEndpoint> publishMock,
        List<AuditEntry> recordedAudits,
        List<object> publishedEvents)
    {
        Db = db;
        Service = service;
        ReferenceData = referenceData;
        PeriodGuardMock = periodGuardMock;
        SequenceMock = sequenceMock;
        AuditMock = auditMock;
        PublishMock = publishMock;
        RecordedAudits = recordedAudits;
        PublishedEvents = publishedEvents;
    }

    /// <summary>The SQLite-backed journal context under test.</summary>
    public JournalDbContext Db { get; }

    /// <summary>The system under test.</summary>
    public JournalEntryService Service { get; }

    /// <summary>The in-memory account/currency reference reader (configure not-postable/inactive on it).</summary>
    public FakeReferenceDataReader ReferenceData { get; }

    /// <summary>The mocked posting-period guard (defaults to always-open; reconfigure to reject).</summary>
    public Mock<IPostingPeriodGuard> PeriodGuardMock { get; }

    /// <summary>The mocked gapless sequence generator yielding deterministic JE numbers.</summary>
    public Mock<ISequenceGenerator> SequenceMock { get; }

    /// <summary>The no-op audit service capturing recorded audit entries in call order.</summary>
    public Mock<IAuditService> AuditMock { get; }

    /// <summary>The no-op publish endpoint capturing published domain events in call order.</summary>
    public Mock<IPublishEndpoint> PublishMock { get; }

    /// <summary>The audit entries captured by <see cref="IAuditService.RecordAsync"/>, in call order.</summary>
    public List<AuditEntry> RecordedAudits { get; }

    /// <summary>The domain events captured by <see cref="IPublishEndpoint"/>, in call order.</summary>
    public List<object> PublishedEvents { get; }

    /// <summary>
    /// Builds a harness over the supplied context. The validation surface uses the real shape validator and
    /// the real chain (Balance → LineBaseAmount → AccountPostability → LineCurrency, the Program.cs order)
    /// so the create/post paths exercise live double-entry rules.
    /// </summary>
    /// <param name="db">The SQLite-backed journal context.</param>
    /// <returns>A wired harness.</returns>
    public static JournalEntryServiceTestHarness Build(JournalDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        List<AuditEntry> recordedAudits = [];
        List<object> publishedEvents = [];

        IMapper mapper = new MapperConfiguration(cfg => cfg.AddProfile<JournalMappingProfile>())
            .CreateMapper();

        FakeReferenceDataReader referenceData = new();

        JournalEntryValidator validator = new(
            new JournalEntryShapeValidator(),
            new ValidationChain<JournalEntryValidationContext>(
            [
                new BalanceValidator(),
                new LineBaseAmountValidator(),
                new AccountPostabilityValidator(referenceData),
                new LineCurrencyValidator(referenceData)
            ]));

        Mock<IPostingPeriodGuard> periodGuardMock = new();
        periodGuardMock
            .Setup(g => g.EnsurePostableAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        WorkflowEngine<JournalEntry> workflow = BuildWorkflow(periodGuardMock.Object);

        JournalEntryServiceTestHarness harness = BuildWithDependencies(
            db, mapper, validator, workflow, referenceData, periodGuardMock, recordedAudits, publishedEvents);

        return harness;
    }

    private static JournalEntryServiceTestHarness BuildWithDependencies(
        JournalDbContext db,
        IMapper mapper,
        JournalEntryValidator validator,
        WorkflowEngine<JournalEntry> workflow,
        FakeReferenceDataReader referenceData,
        Mock<IPostingPeriodGuard> periodGuardMock,
        List<AuditEntry> recordedAudits,
        List<object> publishedEvents)
    {
        Mock<ISequenceGenerator> sequenceMock = new();
        int counter = 0;
        sequenceMock
            .Setup(s => s.NextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"JE-2026-{++counter:000000}");

        Mock<IAuditService> auditMock = new();
        auditMock
            .Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Callback<AuditEntry, CancellationToken, bool>((entry, _, _) => recordedAudits.Add(entry))
            .ReturnsAsync(Result.Success());

        Mock<IPublishEndpoint> publishMock = new();
        publishMock
            .Setup(p => p.Publish(It.IsAny<JournalEntryPostedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<JournalEntryPostedEvent, CancellationToken>((message, _) => publishedEvents.Add(message))
            .Returns(Task.CompletedTask);
        publishMock
            .Setup(p => p.Publish(It.IsAny<JournalEntryReversedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<JournalEntryReversedEvent, CancellationToken>((message, _) => publishedEvents.Add(message))
            .Returns(Task.CompletedTask);
        publishMock
            .Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((message, _) => publishedEvents.Add(message))
            .Returns(Task.CompletedTask);

        JournalEntryService service = new(
            db,
            mapper,
            new StubCorrelationIdAccessor(),
            validator,
            workflow,
            sequenceMock.Object,
            auditMock.Object,
            publishMock.Object,
            new StubCurrentUserAccessor());

        return new JournalEntryServiceTestHarness(
            db,
            service,
            referenceData,
            periodGuardMock,
            sequenceMock,
            auditMock,
            publishMock,
            recordedAudits,
            publishedEvents);
    }

    private static WorkflowEngine<JournalEntry> BuildWorkflow(IPostingPeriodGuard periodGuard)
    {
        List<IWorkflowState<JournalEntry>> states =
        [
            new DraftJournalEntryState(),
            new PostedJournalEntryState(),
            new ReversedJournalEntryState()
        ];

        WorkflowStateRegistry<JournalEntry> registry = new(states);
        List<IChainValidator<WorkflowContext<JournalEntry>>> guards =
        [
            new PostingPeriodWorkflowGuard(periodGuard)
        ];

        return new WorkflowEngine<JournalEntry>(registry, guards);
    }
}
