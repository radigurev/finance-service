using Finance.IntegrationTesting;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Integration;

/// <summary>
/// Assembly-level fixture for the Journal <c>Integration</c> namespace. Starts the shared SQL Server /
/// Redis / RabbitMQ containers once before any integration test runs, publishes the connection details
/// for the <c>finance_journal_test</c> database, and disposes them afterwards. When the default suite
/// excludes <c>[Category("Integration")]</c>, no test here is selected, so the containers never start —
/// preserving the fast offline unit run.
/// </summary>
[SetUpFixture]
public sealed class IntegrationTestSetup
{
    /// <summary>The running infrastructure containers shared by all Journal integration tests.</summary>
    public static FinanceContainers Containers { get; private set; } = null!;

    /// <summary>Starts the containers before the first integration test.</summary>
    [OneTimeSetUp]
    public async Task StartContainersAsync()
    {
        Containers = new FinanceContainers();
        await Containers.StartAsync();
        IntegrationEnvironment.Apply(Containers, "FinanceJournalDb", "finance_journal_test");

        // The Journal host eagerly registers the gateway-backed Accounts/Currencies/Periods Refit clients
        // during ConfigureServices and requires Gateway:BaseUrl to be present. The reference and period
        // clients are replaced with in-memory fakes per fixture, so the URL is never dialed; it only needs
        // to be a syntactically valid absolute URI to satisfy startup validation.
        Environment.SetEnvironmentVariable("Gateway__BaseUrl", "http://localhost:65535");
        Environment.SetEnvironmentVariable("Country__BaseCurrency", "BGN");
    }

    /// <summary>Disposes the containers after the last integration test.</summary>
    [OneTimeTearDown]
    public async Task StopContainersAsync()
    {
        if (Containers is not null)
        {
            await Containers.DisposeAsync();
        }
    }
}
