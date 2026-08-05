using Finance.Payments.API.Consumers;
using Finance.Payments.API.Services;
using Finance.Payments.DBModel;
using Finance.ServiceModel.Events.Invoices;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// Runs the FOUR invoice projection consumers over the REAL <see cref="InvoiceOpenItemProjection"/> and a SQLite
/// in-memory <see cref="PaymentsDbContext"/> (SDD-PAY-002 §6.4). Because the projection writer is the production
/// type, the tests prove the convergent-upsert rules — silent skip for a non-settleable document type, no
/// downgrade of an already-posted row, terminal statuses never left, the cancellation tombstone, and the
/// locally-owned settled amount never overwritten — rather than a re-implementation.
/// <para>The <see cref="ConsumeContext{T}"/> is a Moq'd shell, mirroring the shipped Invoices
/// <c>WarehouseConsumerTestHarness</c>; the shared <c>UseFinanceIdempotency()</c> transport filter is out of scope
/// for a unit test and is asserted by the integration suite.</para>
/// </summary>
public sealed class ProjectionConsumerTestHarness
{
    private ProjectionConsumerTestHarness(
        PaymentsDbContext db,
        InvoiceOpenItemProjection projection,
        FixedTimeProvider clock,
        RecordingLogger<InvoiceOpenItemProjection> projectionLogger)
    {
        Db = db;
        Projection = projection;
        Clock = clock;
        ProjectionLogger = projectionLogger;
    }

    /// <summary>The SQLite-backed payments context holding the projection table.</summary>
    public PaymentsDbContext Db { get; }

    /// <summary>The real projection writer the four consumers delegate to.</summary>
    public InvoiceOpenItemProjection Projection { get; }

    /// <summary>The settable clock stamping the last-applied timestamp.</summary>
    public FixedTimeProvider Clock { get; }

    /// <summary>The recording logger capturing the orphaned-settlement warning and the skip notices.</summary>
    public RecordingLogger<InvoiceOpenItemProjection> ProjectionLogger { get; }

    /// <summary>Builds a harness over the supplied context.</summary>
    /// <param name="db">The SQLite-backed payments context.</param>
    /// <returns>A wired harness.</returns>
    public static ProjectionConsumerTestHarness Build(PaymentsDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        FixedTimeProvider clock = new();
        RecordingLogger<InvoiceOpenItemProjection> logger = new();
        InvoiceOpenItemProjection projection = new(db, clock, logger);

        return new ProjectionConsumerTestHarness(db, projection, clock, logger);
    }

    /// <summary>Runs the confirmation consumer over the supplied event.</summary>
    /// <param name="message">The invoice confirmation event.</param>
    /// <returns>A task completing when consumption finishes.</returns>
    public Task ConsumeAsync(InvoiceConfirmedEvent message)
    {
        InvoiceConfirmedEventConsumer consumer = new(
            Projection, NullLogger<InvoiceConfirmedEventConsumer>.Instance);
        return consumer.Consume(ContextFor(message));
    }

    /// <summary>Runs the posting consumer over the supplied event.</summary>
    /// <param name="message">The invoice posting back-event.</param>
    /// <returns>A task completing when consumption finishes.</returns>
    public Task ConsumeAsync(InvoicePostedEvent message)
    {
        InvoicePostedEventConsumer consumer = new(
            Projection, NullLogger<InvoicePostedEventConsumer>.Instance);
        return consumer.Consume(ContextFor(message));
    }

    /// <summary>Runs the cancellation consumer over the supplied event.</summary>
    /// <param name="message">The invoice cancellation event.</param>
    /// <returns>A task completing when consumption finishes.</returns>
    public Task ConsumeAsync(InvoiceCancelledEvent message)
    {
        InvoiceCancelledEventConsumer consumer = new(
            Projection, NullLogger<InvoiceCancelledEventConsumer>.Instance);
        return consumer.Consume(ContextFor(message));
    }

    /// <summary>Runs the reversal consumer over the supplied event.</summary>
    /// <param name="message">The invoice reversal event.</param>
    /// <returns>A task completing when consumption finishes.</returns>
    public Task ConsumeAsync(InvoiceReversedEvent message)
    {
        InvoiceReversedEventConsumer consumer = new(
            Projection, NullLogger<InvoiceReversedEventConsumer>.Instance);
        return consumer.Consume(ContextFor(message));
    }

    private static ConsumeContext<TEvent> ContextFor<TEvent>(TEvent message)
        where TEvent : class
    {
        Mock<ConsumeContext<TEvent>> context = new();
        context.SetupGet(consume => consume.Message).Returns(message);
        context.SetupGet(consume => consume.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }
}
