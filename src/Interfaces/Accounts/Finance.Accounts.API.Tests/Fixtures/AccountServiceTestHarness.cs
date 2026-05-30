using AutoMapper;
using Finance.Accounts.API.Mapping;
using Finance.Accounts.API.Services;
using Finance.Accounts.API.Validators;
using Finance.Accounts.DBModel;
using Finance.Common.Results;
using Finance.Common.Validation;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Audit.Models;
using Finance.Infrastructure.Caching.Interfaces;
using Finance.ServiceModel.Accounts;
using Finance.ServiceModel.Events.Accounts;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Finance.Accounts.API.Tests.Fixtures;

/// <summary>
/// Assembles an <see cref="AccountService"/> over a SQLite in-memory context with faked cache, audit,
/// and publish dependencies for the Accounts unit tests (SDD-ACCT-001 §6). The cross-aggregate
/// validation chain uses the real <see cref="DuplicateAccountCodeValidator"/> and
/// <see cref="ParentAccountValidator"/> so the create path exercises live SQLite-backed rules.
/// <para>The publish mock captures both the strongly-typed generic overload (the create path publishes a
/// concrete <see cref="AccountCreatedEvent"/>) and the non-generic <c>Publish(object, ...)</c> overload
/// (the update/deactivate paths publish a variable typed as <c>object</c>).</para>
/// </summary>
public sealed class AccountServiceTestHarness
{
    /// <summary>The country code stamped onto created accounts and read by the chain validators.</summary>
    public const string CountryCode = "BG";

    private AccountServiceTestHarness(
        AccountsDbContext db,
        AccountService service,
        Mock<IAuditService> auditMock,
        Mock<IPublishEndpoint> publishMock,
        Mock<ICacheService<AccountDto>> accountCacheMock,
        Mock<ICacheService<IReadOnlyList<AccountDto>>> chartCacheMock,
        List<AuditEntry> recordedAudits,
        List<object> publishedEvents)
    {
        Db = db;
        Service = service;
        AuditMock = auditMock;
        PublishMock = publishMock;
        AccountCacheMock = accountCacheMock;
        ChartCacheMock = chartCacheMock;
        RecordedAudits = recordedAudits;
        PublishedEvents = publishedEvents;
    }

    /// <summary>The SQLite-backed accounts context under test.</summary>
    public AccountsDbContext Db { get; }

    /// <summary>The system under test.</summary>
    public AccountService Service { get; }

    /// <summary>The no-op audit service capturing recorded audit entries.</summary>
    public Mock<IAuditService> AuditMock { get; }

    /// <summary>The no-op publish endpoint capturing published domain events.</summary>
    public Mock<IPublishEndpoint> PublishMock { get; }

    /// <summary>The single-account reference-read cache mock.</summary>
    public Mock<ICacheService<AccountDto>> AccountCacheMock { get; }

    /// <summary>The active-chart reference-read cache mock.</summary>
    public Mock<ICacheService<IReadOnlyList<AccountDto>>> ChartCacheMock { get; }

    /// <summary>The audit entries captured by <see cref="IAuditService.RecordAsync"/>, in call order.</summary>
    public List<AuditEntry> RecordedAudits { get; }

    /// <summary>The domain events captured by <see cref="IPublishEndpoint"/>, in call order.</summary>
    public List<object> PublishedEvents { get; }

    /// <summary>
    /// Builds a harness over the supplied context. The reference-read cache mocks are configured as
    /// pass-throughs (always invoke the factory) so get-by-id and the chart load exercise the real DB.
    /// </summary>
    /// <param name="db">The SQLite-backed accounts context.</param>
    /// <returns>A wired harness.</returns>
    public static AccountServiceTestHarness Build(AccountsDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        List<AuditEntry> recordedAudits = [];
        List<object> publishedEvents = [];

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Country:Code"] = CountryCode })
            .Build();

        IMapper mapper = new MapperConfiguration(cfg => cfg.AddProfile<AccountMappingProfile>())
            .CreateMapper();

        ValidationChain<CreateAccountRequest> chain = new(
        [
            new DuplicateAccountCodeValidator(db, configuration),
            new ParentAccountValidator(db, configuration)
        ]);

        Mock<IAuditService> auditMock = new();
        auditMock
            .Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Callback<AuditEntry, CancellationToken, bool>((entry, _, _) => recordedAudits.Add(entry))
            .ReturnsAsync(Result.Success());

        Mock<IPublishEndpoint> publishMock = new();
        publishMock
            .Setup(p => p.Publish(It.IsAny<AccountCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AccountCreatedEvent, CancellationToken>((message, _) => publishedEvents.Add(message))
            .Returns(Task.CompletedTask);
        publishMock
            .Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((message, _) => publishedEvents.Add(message))
            .Returns(Task.CompletedTask);

        Mock<ICacheService<AccountDto>> accountCacheMock = new();
        accountCacheMock
            .Setup(c => c.GetOrSetAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<AccountDto?>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<CancellationToken, Task<AccountDto?>>, TimeSpan?, CancellationToken>(
                (_, factory, _, ct) => factory(ct));
        accountCacheMock
            .Setup(c => c.RemoveByPatternAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<ICacheService<IReadOnlyList<AccountDto>>> chartCacheMock = new();
        chartCacheMock
            .Setup(c => c.GetOrSetAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<IReadOnlyList<AccountDto>?>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<CancellationToken, Task<IReadOnlyList<AccountDto>?>>, TimeSpan?, CancellationToken>(
                (_, factory, _, ct) => factory(ct));

        AccountService service = new(
            db,
            mapper,
            new StubCorrelationIdAccessor(),
            chain,
            auditMock.Object,
            publishMock.Object,
            accountCacheMock.Object,
            chartCacheMock.Object,
            new StubCurrentUserAccessor());

        return new AccountServiceTestHarness(
            db,
            service,
            auditMock,
            publishMock,
            accountCacheMock,
            chartCacheMock,
            recordedAudits,
            publishedEvents);
    }
}
