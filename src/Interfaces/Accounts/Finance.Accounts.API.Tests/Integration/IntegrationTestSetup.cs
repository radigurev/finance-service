using Finance.IntegrationTesting;
using NUnit.Framework;

namespace Finance.Accounts.API.Tests.Integration;

/// <summary>
/// Assembly-level fixture for the <c>Integration</c> namespace. Starts the shared SQL Server / Redis /
/// RabbitMQ containers once before any integration test runs and disposes them afterwards. When the
/// default suite excludes <c>[Category("Integration")]</c>, no test in this namespace is selected, so
/// the containers are never started — preserving the fast offline unit run.
/// </summary>
[SetUpFixture]
public sealed class IntegrationTestSetup
{
    /// <summary>The running infrastructure containers shared by all integration tests in this assembly.</summary>
    public static FinanceContainers Containers { get; private set; } = null!;

    /// <summary>Starts the containers before the first integration test.</summary>
    [OneTimeSetUp]
    public async Task StartContainersAsync()
    {
        Containers = new FinanceContainers();
        await Containers.StartAsync();
        IntegrationEnvironment.Apply(Containers, "FinanceAccountsDb", "finance_accounts_test");
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
