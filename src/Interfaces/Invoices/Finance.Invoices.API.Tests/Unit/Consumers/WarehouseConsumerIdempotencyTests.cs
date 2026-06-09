using Finance.Common.Enums;
using Finance.Infrastructure.Messaging.Filters;
using Finance.Invoices.API.Consumers;
using Finance.Invoices.API.Interfaces;
using Finance.Invoices.API.Tests.Builders;
using Finance.Invoices.API.Tests.Fixtures;
using Finance.ServiceModel.Integration.Warehouse.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using StackExchange.Redis;

namespace Finance.Invoices.API.Tests.Unit.Consumers;

/// <summary>
/// Unit tests for the idempotency and source-document dedupe of the Warehouse inbound consumers
/// (SDD-INT-WH-001 §6.2). The <c>MessageId</c> idempotency is enforced by the shared
/// <see cref="IdempotencyFilter{T}"/> (Redis <c>SETNX</c>) — exercised here over a faked
/// <see cref="IConnectionMultiplexer"/> so a replay is skipped without invoking the consumer. The
/// source-document dedupe is enforced inside the factory and proven against the real create path: a distinct
/// <c>MessageId</c> for the same source document creates no second draft, and a transient failure followed by
/// a retry leaves exactly one draft.
/// </summary>
[TestFixture]
[Category("SDD-INT-WH-001")]
public sealed class WarehouseConsumerIdempotencyTests
{
    private SqliteInvoicesDbContextScope _scope = null!;
    private InvoiceServiceTestHarness _invoices = null!;
    private WarehouseConsumerTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed consumer harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteInvoicesDbContextFactory.Create();
        _invoices = InvoiceServiceTestHarness.Build(_scope.Context);
        _harness = WarehouseConsumerTestHarness.Build(_invoices);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    /// <summary>A duplicate MessageId is skipped by the idempotency filter — the consumer pipe is not run (§2.1.1, §6.2).</summary>
    [Test]
    public async Task Consumer_DuplicateMessageId_IsSkipped_ByIdempotencyFilter()
    {
        // Arrange — Redis SETNX reports the key already exists (a replay).
        Mock<IDatabase> database = new(MockBehavior.Loose);
        database
            .Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);
        Mock<IConnectionMultiplexer> multiplexer = new(MockBehavior.Loose);
        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

        IdempotencyFilter<ShipmentCompletedEvent> filter = new(
            multiplexer.Object, NullLogger<IdempotencyFilter<ShipmentCompletedEvent>>.Instance);
        Mock<IPipe<ConsumeContext<ShipmentCompletedEvent>>> next = new(MockBehavior.Loose);
        ConsumeContext<ShipmentCompletedEvent> context = ContextFor(
            WarehouseEventBuilder.Create().BuildShipment(), Guid.NewGuid());

        // Act
        await filter.Send(context, next.Object);

        // Assert — the downstream consumer pipe is never invoked for the replayed message.
        next.Verify(pipe => pipe.Send(It.IsAny<ConsumeContext<ShipmentCompletedEvent>>()), Times.Never);
    }

    /// <summary>A distinct MessageId for the same source document creates no second draft (source-doc dedupe) (§2.1.2, §6.2).</summary>
    [Test]
    public async Task Consumer_DistinctMessageIdSameSourceDocument_DoesNotCreateSecondDraft()
    {
        // Arrange — two events sharing a source document but with different message ids.
        Guid sourceDocumentId = Guid.NewGuid();
        GoodsReceiptCompletedEvent first = WarehouseEventBuilder.Create()
            .WithMessageId(Guid.NewGuid())
            .WithSourceDocumentId(sourceDocumentId)
            .BuildGoodsReceipt();
        GoodsReceiptCompletedEvent second = WarehouseEventBuilder.Create()
            .WithMessageId(Guid.NewGuid())
            .WithSourceDocumentId(sourceDocumentId)
            .BuildGoodsReceipt();

        // Act
        await _harness.ConsumeAsync(first);
        await _harness.ConsumeAsync(second);

        // Assert
        Assert.That(await _scope.Context.Invoices.CountAsync(CancellationToken.None), Is.EqualTo(1));
    }

    /// <summary>A transient failure then a retry of the same source document yields exactly one draft (§2.4, §6.2).</summary>
    [Test]
    public async Task Consumer_TransientFailureThenRetry_CreatesExactlyOneDraft()
    {
        // Arrange — a factory that throws once (transient) then delegates to the real create path on retry.
        Guid sourceDocumentId = Guid.NewGuid();
        WarehouseInvoiceDraftFactoryDecorator factory = new(_harness.Factory, throwOnFirstCall: true);
        Finance.Invoices.API.Consumers.GoodsReceiptCompletedConsumer consumer = new(
            factory, NullLogger<Finance.Invoices.API.Consumers.GoodsReceiptCompletedConsumer>.Instance);
        GoodsReceiptCompletedEvent @event = WarehouseEventBuilder.Create()
            .WithSourceDocumentId(sourceDocumentId)
            .BuildGoodsReceipt();

        // Act — first delivery throws (MassTransit would retry); the retry succeeds.
        Assert.That(
            async () => await consumer.Consume(ContextFor(@event, @event.MessageId)),
            Throws.TypeOf<TimeoutException>());
        await consumer.Consume(ContextFor(@event, @event.MessageId));

        // Assert
        Assert.That(await _scope.Context.Invoices.CountAsync(CancellationToken.None), Is.EqualTo(1));
    }

    private static ConsumeContext<TEvent> ContextFor<TEvent>(TEvent message, Guid messageId)
        where TEvent : class
    {
        Mock<ConsumeContext<TEvent>> context = new();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.MessageId).Returns(messageId);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    /// <summary>
    /// Test decorator that fails the first <c>CreateDraftAsync</c> call with a transient exception then
    /// delegates to the real factory, simulating a transient infrastructure failure followed by a retry while
    /// the underlying source-document dedupe guarantees only one draft is ever created.
    /// </summary>
    private sealed class WarehouseInvoiceDraftFactoryDecorator : IWarehouseInvoiceDraftFactory
    {
        private readonly IWarehouseInvoiceDraftFactory _inner;
        private bool _shouldThrow;

        public WarehouseInvoiceDraftFactoryDecorator(IWarehouseInvoiceDraftFactory inner, bool throwOnFirstCall)
        {
            _inner = inner;
            _shouldThrow = throwOnFirstCall;
        }

        public Task<WarehouseDraftOutcome> CreateDraftAsync(
            IWarehouseDocumentEvent @event,
            InvoiceDocumentType documentType,
            string sourceDocumentType,
            Guid? correctsInvoiceId,
            CancellationToken cancellationToken)
        {
            if (_shouldThrow)
            {
                _shouldThrow = false;
                throw new TimeoutException("Transient infrastructure failure on first delivery.");
            }

            return _inner.CreateDraftAsync(
                @event, documentType, sourceDocumentType, correctsInvoiceId, cancellationToken);
        }
    }
}
