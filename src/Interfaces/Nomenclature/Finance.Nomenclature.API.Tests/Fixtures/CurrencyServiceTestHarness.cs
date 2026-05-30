using AutoMapper;
using Finance.Common.Results;
using Finance.Common.Validation;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Audit.Models;
using Finance.Infrastructure.Caching.Interfaces;
using Finance.Nomenclature.API.Mapping;
using Finance.Nomenclature.API.Services;
using Finance.Nomenclature.API.Validators;
using Finance.Nomenclature.DBModel;
using Finance.ServiceModel.Events.Nomenclature;
using Finance.ServiceModel.Nomenclature;
using MassTransit;
using Moq;

namespace Finance.Nomenclature.API.Tests.Fixtures;

/// <summary>
/// Assembles a <see cref="CurrencyService"/> over a SQLite in-memory context with faked cache, audit, and
/// publish dependencies for the Nomenclature unit tests (SDD-NOM-001 §6). The cross-aggregate validation
/// chain uses the real <see cref="DuplicateCurrencyCodeValidator"/> so the create path exercises a live
/// SQLite-backed uniqueness rule.
/// <para>The publish mock captures both the strongly-typed <see cref="CurrencyCreatedEvent"/> overload (the
/// create path publishes a concrete event) and the non-generic <c>Publish(object, ...)</c> overload (the
/// update/deactivate paths publish a variable typed as <c>object</c>).</para>
/// </summary>
public sealed class CurrencyServiceTestHarness
{
    private CurrencyServiceTestHarness(
        NomenclatureDbContext db,
        CurrencyService service,
        Mock<IAuditService> auditMock,
        Mock<IPublishEndpoint> publishMock,
        Mock<ICacheService<IReadOnlyList<CurrencyDto>>> currencyListCacheMock,
        List<AuditEntry> recordedAudits,
        List<object> publishedEvents)
    {
        Db = db;
        Service = service;
        AuditMock = auditMock;
        PublishMock = publishMock;
        CurrencyListCacheMock = currencyListCacheMock;
        RecordedAudits = recordedAudits;
        PublishedEvents = publishedEvents;
    }

    /// <summary>The SQLite-backed nomenclature context under test.</summary>
    public NomenclatureDbContext Db { get; }

    /// <summary>The system under test.</summary>
    public CurrencyService Service { get; }

    /// <summary>The no-op audit service capturing recorded audit entries.</summary>
    public Mock<IAuditService> AuditMock { get; }

    /// <summary>The no-op publish endpoint capturing published domain events.</summary>
    public Mock<IPublishEndpoint> PublishMock { get; }

    /// <summary>The active-currency-list reference-read cache mock.</summary>
    public Mock<ICacheService<IReadOnlyList<CurrencyDto>>> CurrencyListCacheMock { get; }

    /// <summary>The audit entries captured by <see cref="IAuditService.RecordAsync"/>, in call order.</summary>
    public List<AuditEntry> RecordedAudits { get; }

    /// <summary>The domain events captured by <see cref="IPublishEndpoint"/>, in call order.</summary>
    public List<object> PublishedEvents { get; }

    /// <summary>
    /// Builds a harness over the supplied context. The reference-read cache mock is configured as a
    /// pass-through (always invokes the factory) so the active-list load exercises the real DB.
    /// </summary>
    /// <param name="db">The SQLite-backed nomenclature context.</param>
    /// <returns>A wired harness.</returns>
    public static CurrencyServiceTestHarness Build(NomenclatureDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        List<AuditEntry> recordedAudits = [];
        List<object> publishedEvents = [];

        IMapper mapper = new MapperConfiguration(cfg => cfg.AddProfile<NomenclatureMappingProfile>())
            .CreateMapper();

        ValidationChain<CreateCurrencyRequest> chain = new(
        [
            new DuplicateCurrencyCodeValidator(db)
        ]);

        Mock<IAuditService> auditMock = new();
        auditMock
            .Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Callback<AuditEntry, CancellationToken, bool>((entry, _, _) => recordedAudits.Add(entry))
            .ReturnsAsync(Result.Success());

        Mock<IPublishEndpoint> publishMock = new();
        publishMock
            .Setup(p => p.Publish(It.IsAny<CurrencyCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<CurrencyCreatedEvent, CancellationToken>((message, _) => publishedEvents.Add(message))
            .Returns(Task.CompletedTask);
        publishMock
            .Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((message, _) => publishedEvents.Add(message))
            .Returns(Task.CompletedTask);

        Mock<ICacheService<IReadOnlyList<CurrencyDto>>> currencyListCacheMock = new();
        currencyListCacheMock
            .Setup(c => c.GetOrSetAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<IReadOnlyList<CurrencyDto>?>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<CancellationToken, Task<IReadOnlyList<CurrencyDto>?>>, TimeSpan?, CancellationToken>(
                (_, factory, _, ct) => factory(ct));
        currencyListCacheMock
            .Setup(c => c.RemoveByPatternAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        CurrencyService service = new(
            db,
            mapper,
            new StubCorrelationIdAccessor(),
            chain,
            auditMock.Object,
            publishMock.Object,
            currencyListCacheMock.Object,
            new StubCurrentUserAccessor());

        return new CurrencyServiceTestHarness(
            db,
            service,
            auditMock,
            publishMock,
            currencyListCacheMock,
            recordedAudits,
            publishedEvents);
    }
}
