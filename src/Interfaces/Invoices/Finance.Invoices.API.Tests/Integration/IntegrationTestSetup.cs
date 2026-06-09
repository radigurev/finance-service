using Finance.IntegrationTesting;
using NUnit.Framework;

namespace Finance.Invoices.API.Tests.Integration;

/// <summary>
/// Assembly-level fixture for the Invoices <c>Integration</c> namespace. Starts the shared SQL Server /
/// Redis / RabbitMQ containers once before any integration test runs, publishes the connection details for
/// the <c>finance_invoices_test</c> database, and disposes them afterwards. When the default suite excludes
/// <c>[Category("Integration")]</c>, no test here is selected, so the containers never start — preserving the
/// fast offline unit run (SDD-INV-001 §6.6, SDD-INT-WH-001 §6.4).
/// </summary>
[SetUpFixture]
public sealed class IntegrationTestSetup
{
    /// <summary>The running infrastructure containers shared by all Invoices integration tests.</summary>
    public static FinanceContainers Containers { get; private set; } = null!;

    /// <summary>Starts the containers before the first integration test.</summary>
    [OneTimeSetUp]
    public async Task StartContainersAsync()
    {
        Containers = new FinanceContainers();
        await Containers.StartAsync();
        IntegrationEnvironment.Apply(Containers, "FinanceInvoicesDb", "finance_invoices_test");

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
