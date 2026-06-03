using NUnit.Framework;

namespace Finance.Journal.API.Tests.Integration;

/// <summary>
/// Endpoint, real-SQL, and outbox integration tests for the Journal service (SDD-FIN-001 §6.5,
/// SDD-FIN-002 §6.5). These require a running SQL Server (gapless <c>UPDLOCK, HOLDLOCK</c> numbering and the
/// real <c>rowversion</c>), Redis, RabbitMQ, and the auth service, none of which are available in the
/// offline unit run. They are marked <c>[Category("Integration")]</c> so the default suite excludes them and
/// are placeholders the integration phase fleshes out against a real environment (the SQLite in-memory unit
/// tests cannot exercise the raw-SQL sequence path, true outbox ordering, or the audit-schema DENY grants).
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("SDD-FIN-002")]
public sealed class JournalEndpointIntegrationTests
{
    /// <summary>POST creates a draft and returns 201 (SDD-FIN-002 §6.5).</summary>
    [Test]
    public void Create_Returns201_AndPersistsDraft()
    {
        Assert.Ignore("Requires SQL Server, Redis, RabbitMQ, and the auth service — runs in the integration phase.");
    }

    /// <summary>Posting writes the outbox row and audit row in one transaction (SDD-FIN-002 §6.5).</summary>
    [Test]
    public void Post_Returns200_AndWritesOutboxAndAuditRow_InSameTransaction()
    {
        Assert.Ignore("Requires SQL Server + RabbitMQ outbox — runs in the integration phase.");
    }

    /// <summary>Concurrent posters: one wins, the other fails with CONCURRENT_MODIFICATION (SDD-FIN-002 §6.5).</summary>
    [Test]
    public void Post_ConcurrentCallers_OneFailsWithConcurrentModification()
    {
        Assert.Ignore("Requires the real SQL Server rowversion under contention — runs in the integration phase.");
    }

    /// <summary>Concurrent posts allocate gapless JE numbers with no gaps (SDD-FIN-002 §6.5; SDD-INFRA-003).</summary>
    [Test]
    public void Post_AllocatesGaplessJeNumbers_UnderConcurrency_NoGaps()
    {
        Assert.Ignore("Requires the SQL-Server-only UPDLOCK, HOLDLOCK sequence path — runs in the integration phase.");
    }

    /// <summary>Reverse persists the reversal entry and flips the original to Reversed (SDD-FIN-002 §6.5).</summary>
    [Test]
    public void Reverse_Returns200_AndPersistsReversalEntry_AndFlipsOriginalToReversed()
    {
        Assert.Ignore("Requires SQL Server + RabbitMQ outbox — runs in the integration phase.");
    }

    /// <summary>Reverse without a reason returns 400 (SDD-FIN-002 §6.5).</summary>
    [Test]
    public void Reverse_Returns400_WhenReasonMissing()
    {
        Assert.Ignore("Requires the hosted endpoint — runs in the integration phase.");
    }

    /// <summary>Posting an already-posted entry returns 409 (SDD-FIN-002 §6.5).</summary>
    [Test]
    public void Post_Returns409_WhenAlreadyPosted()
    {
        Assert.Ignore("Requires the hosted endpoint — runs in the integration phase.");
    }

    /// <summary>Updating a posted entry returns 409 (SDD-FIN-002 §6.5).</summary>
    [Test]
    public void Update_Returns409_WhenEntryPosted()
    {
        Assert.Ignore("Requires the hosted endpoint — runs in the integration phase.");
    }

    /// <summary>An endpoint returns 403 when the caller lacks the permission (SDD-FIN-002 §6.5; SDD-INT-AUTH-001).</summary>
    [Test]
    public void Endpoint_Returns403_WhenPermissionMissing()
    {
        Assert.Ignore("Requires the auth service and a real JWT — runs in the integration phase.");
    }

    /// <summary>A journal entry with lines round-trips with cascade on the lines (SDD-FIN-001 §6.5).</summary>
    [Test]
    public void Persist_JournalEntryWithLines_RoundTrips_WithCascadeOnLines()
    {
        Assert.Ignore("Requires SQL Server for cascade and NEWSEQUENTIALID behaviour — runs in the integration phase.");
    }

    /// <summary>Decimal amounts retain two-decimal precision on real SQL (SDD-FIN-001 §6.5).</summary>
    [Test]
    public void Persist_DecimalAmounts_RetainTwoDecimalPrecision_OnRealSql()
    {
        Assert.Ignore("Requires SQL Server decimal(18,2) storage — runs in the integration phase.");
    }
}
