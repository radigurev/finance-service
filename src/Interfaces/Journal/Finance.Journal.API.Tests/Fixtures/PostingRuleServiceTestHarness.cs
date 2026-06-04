using AutoMapper;
using Finance.Common.Results;
using Finance.Common.Validation;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Audit.Models;
using Finance.Journal.API.Mapping;
using Finance.Journal.API.Services;
using Finance.Journal.API.Validators;
using Finance.Journal.DBModel;
using Finance.ServiceModel.Posting;
using Moq;

namespace Finance.Journal.API.Tests.Fixtures;

/// <summary>
/// Assembles a <see cref="PostingRuleService"/> over a SQLite in-memory <see cref="JournalDbContext"/> with
/// the real create validation chain (duplicate-key + structural-balance), the real posting-rule mapping
/// profile, an in-memory recording cache, and a capturing audit service for the SDD-FIN-006 §6.2 CRUD unit
/// tests. The audit service is mocked so audit rows are captured in order without needing an ambient audit
/// context; the cache is the <see cref="RecordingPostingRuleCacheService"/> so invalidation is observable.
/// </summary>
public sealed class PostingRuleServiceTestHarness
{
    /// <summary>The owning country code stamped onto created rules.</summary>
    public const string CountryCode = "BG";

    private PostingRuleServiceTestHarness(
        JournalDbContext db,
        PostingRuleService service,
        RecordingPostingRuleCacheService cache,
        List<AuditEntry> recordedAudits)
    {
        Db = db;
        Service = service;
        Cache = cache;
        RecordedAudits = recordedAudits;
    }

    /// <summary>The SQLite-backed journal context under test.</summary>
    public JournalDbContext Db { get; }

    /// <summary>The system under test.</summary>
    public PostingRuleService Service { get; }

    /// <summary>The in-memory recording cache used to assert invalidation and fall-through.</summary>
    public RecordingPostingRuleCacheService Cache { get; }

    /// <summary>The audit entries captured by the mocked audit service, in call order.</summary>
    public List<AuditEntry> RecordedAudits { get; }

    /// <summary>Builds a harness over the supplied SQLite-backed context.</summary>
    /// <param name="db">The SQLite-backed journal context.</param>
    /// <returns>A wired harness.</returns>
    public static PostingRuleServiceTestHarness Build(JournalDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        List<AuditEntry> recordedAudits = [];

        IMapper mapper = new MapperConfiguration(cfg => cfg.AddProfile<PostingRuleMappingProfile>())
            .CreateMapper();

        ValidationChain<CreatePostingRuleRequest> createChain = new(
        [
            new DuplicatePostingRuleKeyValidator(db),
            new PostingRuleBalanceableValidator()
        ]);

        Mock<IAuditService> auditMock = new();
        auditMock
            .Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Callback<AuditEntry, CancellationToken, bool>((entry, _, _) => recordedAudits.Add(entry))
            .ReturnsAsync(Result.Success());

        RecordingPostingRuleCacheService cache = new();

        PostingRuleService service = new(
            db,
            mapper,
            new StubCorrelationIdAccessor(),
            createChain,
            auditMock.Object,
            cache,
            new StubCurrentUserAccessor());

        return new PostingRuleServiceTestHarness(db, service, cache, recordedAudits);
    }
}
