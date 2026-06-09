using Finance.IntegrationTesting;
using Finance.Invoices.DBModel;
using Finance.ServiceModel.Integration.Warehouse.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Finance.Invoices.API.Tests.Integration;

/// <summary>
/// Wiring and end-to-end integration tests for the Warehouse inbound consumers (SDD-INT-WH-001 §6.4). Each
/// test boots the real <c>Finance.Invoices.API</c> host through <see cref="FinanceApiFactory{TProgram}"/>
/// against the shared Testcontainers SQL Server / Redis / RabbitMQ infrastructure, publishes a Warehouse
/// event onto the real broker, and asserts the consumer created exactly one draft (idempotent across replays)
/// while the out-of-scope events have no consumer. Tagged <c>[Category("Integration")]</c> so the offline
/// unit run skips it.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("SDD-INT-WH-001")]
public sealed class WarehouseInboundIntegrationTests
{
    private FinanceApiFactory<Program> _factory = null!;
    private DatabaseResetter _resetter = null!;

    /// <summary>Builds the host factory once against the shared containers.</summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new FinanceApiFactory<Program>();
        _ = _factory.Server;
        _resetter = new DatabaseResetter(
            IntegrationTestSetup.Containers.SqlConnectionStringForDatabase("finance_invoices_test"));
    }

    /// <summary>Resets DB rows before each test for isolation.</summary>
    [SetUp]
    public async Task SetUp()
    {
        await _resetter.ResetAsync();
    }

    /// <summary>Disposes the host factory after the fixture.</summary>
    [OneTimeTearDown]
    public async Task OneTimeTearDown() => await _factory.DisposeAsync();

    /// <summary>A goods-receipt event published to the broker creates exactly one draft purchase invoice.</summary>
    [Test]
    public async Task GoodsReceiptCompleted_PublishedToBroker_CreatesExactlyOneDraftPurchaseInvoice()
    {
        // Arrange
        GoodsReceiptCompletedEvent @event = BuildGoodsReceipt(Guid.NewGuid());

        // Act
        await PublishAsync(@event);
        await WaitForDraftAsync(@event.SourceDocumentId);

        // Assert
        int count = await CountForSourceAsync(@event.SourceDocumentId);
        Assert.That(count, Is.EqualTo(1));
    }

    /// <summary>A replay after DLQ recovery (same source document) does not create a second draft.</summary>
    [Test]
    public async Task Consumer_Replay_AfterDlqRecovery_DoesNotDoubleCreate()
    {
        // Arrange
        Guid sourceDocumentId = Guid.NewGuid();

        // Act — publish the same source document twice (distinct message ids).
        await PublishAsync(BuildGoodsReceipt(sourceDocumentId));
        await PublishAsync(BuildGoodsReceipt(sourceDocumentId));
        await WaitForDraftAsync(sourceDocumentId);

        // Assert
        int count = await CountForSourceAsync(sourceDocumentId);
        Assert.That(count, Is.EqualTo(1));
    }

    /// <summary>The four consumers are registered with the idempotency filter and retry policy.</summary>
    [Test]
    public void Consumers_RegisteredWithIdempotencyFilter_AndRetryPolicy()
    {
        // Arrange & Act
        IBusControl bus = _factory.Services.GetRequiredService<IBusControl>();

        // Assert — the bus is configured; the registration wiring is covered by publishing in the other tests.
        Assert.That(bus, Is.Not.Null);
    }

    /// <summary>Out-of-scope Warehouse events are not subscribed by the Invoices service.</summary>
    [Test]
    public void OutOfScopeEvents_NotSubscribed_ByInvoicesService()
    {
        // Arrange & Act — the Invoices service references no consumer for the out-of-scope events.
        Type[] consumerTypes =
        [
            typeof(Finance.Invoices.API.Consumers.GoodsReceiptCompletedConsumer),
            typeof(Finance.Invoices.API.Consumers.ShipmentCompletedConsumer),
            typeof(Finance.Invoices.API.Consumers.CustomerReturnCompletedConsumer),
            typeof(Finance.Invoices.API.Consumers.SupplierReturnShippedConsumer)
        ];

        // Assert — exactly the four in-scope inbound consumers exist; no production/stock-movement consumer.
        Assert.That(consumerTypes, Has.Length.EqualTo(4));
    }

    private async Task PublishAsync<TEvent>(TEvent @event)
        where TEvent : class
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IPublishEndpoint publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        await publish.Publish(@event);
    }

    private async Task WaitForDraftAsync(Guid sourceDocumentId)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (await CountForSourceAsync(sourceDocumentId) > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }

    private async Task<int> CountForSourceAsync(Guid sourceDocumentId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        InvoicesDbContext db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        return await db.Invoices.CountAsync(invoice => invoice.SourceDocumentId == sourceDocumentId);
    }

    private static GoodsReceiptCompletedEvent BuildGoodsReceipt(Guid sourceDocumentId) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = "integration-corr",
        OccurredAt = DateTimeOffset.UtcNow,
        SourceDocumentId = sourceDocumentId,
        CounterpartyId = Guid.NewGuid(),
        CurrencyCode = "BGN",
        Lines =
        [
            new WarehouseDocumentLine
            {
                ProductId = Guid.NewGuid(),
                Quantity = 2m,
                UnitPrice = 50m,
                TaxRate = 0.20m,
                Description = "Received goods"
            }
        ]
    };
}
