using NUnit.Framework;

namespace Finance.Journal.API.Tests.Integration;

/// <summary>
/// Endpoint, real-SQL, and no-caching integration tests for the General Ledger / Trial Balance read surface
/// (SDD-FIN-003 §6.4). These require a running SQL Server (to exercise the real <c>SELECT … GROUP BY</c>
/// aggregation and <c>decimal(18,2)</c> storage), the auth service plus a real JWT (for the
/// <c>finance.journal:read</c> permission checks), and the hosted endpoint via
/// <c>WebApplicationFactory</c> — none of which are available in the offline unit run. They are marked
/// <c>[Category("Integration")]</c> so the default suite excludes them, and are placeholders the integration
/// phase fleshes out against a real environment (the SQLite in-memory unit tests already cover the
/// aggregation semantics, the date-window boundaries, enrichment, and validation).
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("SDD-FIN-003")]
public sealed class GeneralLedgerEndpointIntegrationTests
{
    /// <summary>GET /trial-balance returns 200 with matching grand totals and Balanced == true (§6.4).</summary>
    [Test]
    public void TrialBalance_Returns200_WithBalancedTotals_OverRealSql()
    {
        Assert.Ignore("Requires SQL Server and the hosted endpoint — runs in the integration phase.");
    }

    /// <summary>GET /general-ledger/accounts/{id} returns 200 with a chronological running balance (§6.4).</summary>
    [Test]
    public void AccountLedger_Returns200_WithRunningBalance_OverRealSql()
    {
        Assert.Ignore("Requires SQL Server and the hosted endpoint — runs in the integration phase.");
    }

    /// <summary>The account-ledger endpoint returns 400 when fromDate is after toDate (§4, §6.4).</summary>
    [Test]
    public void AccountLedger_Returns400_WhenFromDateAfterToDate()
    {
        Assert.Ignore("Requires the hosted endpoint — runs in the integration phase.");
    }

    /// <summary>The trial-balance endpoint returns 403 when the caller lacks finance.journal:read (§2.7, §6.4).</summary>
    [Test]
    public void TrialBalance_Endpoint_Returns403_WhenPermissionMissing()
    {
        Assert.Ignore("Requires the auth service and a real JWT — runs in the integration phase.");
    }

    /// <summary>The account-ledger endpoint returns 403 when the caller lacks finance.journal:read (§2.7, §6.4).</summary>
    [Test]
    public void AccountLedger_Endpoint_Returns403_WhenPermissionMissing()
    {
        Assert.Ignore("Requires the auth service and a real JWT — runs in the integration phase.");
    }

    /// <summary>GL balances are never cached: a recompute reflects a newly posted entry (§2.6, §6.4).</summary>
    [Test]
    public void GlBalances_AreNotCached_RecomputeReflectsNewPosting()
    {
        Assert.Ignore("Requires SQL Server + RabbitMQ posting flow — runs in the integration phase.");
    }
}
