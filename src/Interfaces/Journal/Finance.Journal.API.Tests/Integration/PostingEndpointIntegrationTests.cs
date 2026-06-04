using NUnit.Framework;

namespace Finance.Journal.API.Tests.Integration;

/// <summary>
/// Endpoint, real-SQL, real-Redis, and RBAC integration tests for the Posting Rules CRUD and the Posting
/// Engine apply operation (SDD-FIN-006 §6.4). These require a running SQL Server (the real <c>rowversion</c>,
/// the gapless <c>UPDLOCK, HOLDLOCK</c> numbering on the delegated post, the audit-schema DENY grants and the
/// transactional outbox), Redis (cache + invalidation), RabbitMQ (the delegated <c>JournalEntryPostedEvent</c>),
/// and the auth service (a real JWT for the <c>finance.posting-rule:*</c> / <c>finance.posting:apply</c>
/// permissions) — none of which are available in the offline unit run. They are marked
/// <c>[Category("Integration")]</c> so the default suite excludes them and are placeholders the integration
/// phase fleshes out against a real environment via <c>WebApplicationFactory</c>.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("SDD-FIN-006")]
public sealed class PostingEndpointIntegrationTests
{
    /// <summary>POST /posting/apply returns 200 and posts a balanced entry end-to-end (SDD-FIN-006 §6.4).</summary>
    [Test]
    public void Apply_Returns200_AndPostsEntry_OverRealSql_WithOutboxAndAudit()
    {
        Assert.Ignore("Requires SQL Server + RabbitMQ outbox + the auth service — runs in the integration phase.");
    }

    /// <summary>POST /posting/apply returns 409 when the materialized lines do not balance (SDD-FIN-006 §6.4).</summary>
    [Test]
    public void Apply_Returns409_WhenRuleUnbalancedForContext()
    {
        Assert.Ignore("Requires the hosted endpoint and a real JWT — runs in the integration phase.");
    }

    /// <summary>POST /posting/apply returns 404 for an unknown rule key (SDD-FIN-006 §6.4).</summary>
    [Test]
    public void Apply_Returns404_WhenRuleKeyUnknown()
    {
        Assert.Ignore("Requires the hosted endpoint and a real JWT — runs in the integration phase.");
    }

    /// <summary>POST /posting-rules returns 201 and persists the rule with its lines (SDD-FIN-006 §6.4).</summary>
    [Test]
    public void CreateRule_Returns201_AndPersists()
    {
        Assert.Ignore("Requires SQL Server and the auth service — runs in the integration phase.");
    }

    /// <summary>POST /posting-rules returns 409 on a duplicate rule key (SDD-FIN-006 §6.4).</summary>
    [Test]
    public void CreateRule_Returns409_WhenDuplicateKey()
    {
        Assert.Ignore("Requires the hosted endpoint and a real JWT — runs in the integration phase.");
    }

    /// <summary>The seeder inserts the BG defaults over real SQL idempotently (SDD-FIN-006 §6.4).</summary>
    [Test]
    public void Seeder_OverRealSql_InsertsBgDefaults_Idempotent()
    {
        Assert.Ignore("Requires SQL Server and the EnablePostingRuleSeeding feature flag — runs in the integration phase.");
    }

    /// <summary>Posting rules are cached and invalidated on write over real Redis (SDD-FIN-006 §6.4).</summary>
    [Test]
    public void PostingRules_AreCached_InvalidatedOnWrite_OverRealRedis()
    {
        Assert.Ignore("Requires a real Redis instance — runs in the integration phase.");
    }

    /// <summary>POST /posting/apply returns 403 when the apply permission is missing (SDD-FIN-006 §6.4; SDD-INT-AUTH-001).</summary>
    [Test]
    public void Apply_Endpoint_Returns403_WhenPostingApplyPermissionMissing()
    {
        Assert.Ignore("Requires the auth service and a real JWT — runs in the integration phase.");
    }

    /// <summary>The posting-rules write endpoint returns 403 without the write permission (SDD-FIN-006 §6.4).</summary>
    [Test]
    public void PostingRules_Endpoint_Returns403_WhenWritePermissionMissing()
    {
        Assert.Ignore("Requires the auth service and a real JWT — runs in the integration phase.");
    }
}
