using Finance.Common.Results;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Audit.Models;
using Finance.Invoices.API.Consumers;
using Finance.Invoices.API.Services;
using Finance.Invoices.DBModel;
using Finance.ServiceModel.Events.Payments;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Finance.Invoices.API.Tests.Fixtures;

/// <summary>
/// Assembles the REAL <see cref="InvoiceSettlementService"/>, the REAL
/// <see cref="InvoiceSettlementStatusCalculator"/>, and the two REAL SDD-PAY-002 allocation-event consumers over
/// a SQLite in-memory <see cref="InvoicesDbContext"/> (SDD-INV-001 §6.7), with a mocked audit service capturing
/// recorded entries in call order.
/// <para>Because the production types are used, the tests prove the ORDERED mirror end to end: the stale-token
/// drop, the absolute assignment, the local status derivation, the ordering-token stamp, the audit row, and the
/// throw-for-retry paths all run through the real code the consumers are registered with.</para>
/// </summary>
public sealed class InvoiceSettlementTestHarness
{
    private readonly InvoicesDbContext _db;

    private InvoiceSettlementTestHarness(
        InvoicesDbContext db,
        InvoiceSettlementService service,
        Mock<IAuditService> auditMock,
        List<AuditEntry> recordedAudits)
    {
        _db = db;
        Service = service;
        AuditMock = auditMock;
        RecordedAudits = recordedAudits;
    }

    /// <summary>The system under test behind both consumers.</summary>
    public InvoiceSettlementService Service { get; }

    /// <summary>The mocked audit service capturing recorded audit entries in call order.</summary>
    public Mock<IAuditService> AuditMock { get; }

    /// <summary>The audit entries captured by <see cref="IAuditService.RecordAsync"/>, in call order.</summary>
    public List<AuditEntry> RecordedAudits { get; }

    /// <summary>Builds a harness over the supplied SQLite-backed context.</summary>
    /// <param name="db">The SQLite-backed invoices context.</param>
    /// <returns>A wired harness.</returns>
    public static InvoiceSettlementTestHarness Build(InvoicesDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        List<AuditEntry> recordedAudits = [];

        Mock<IAuditService> auditMock = new();
        auditMock
            .Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Callback<AuditEntry, CancellationToken, bool>((entry, _, _) => recordedAudits.Add(entry))
            .ReturnsAsync(Result.Success());

        InvoiceSettlementService service = new(
            db,
            new InvoiceSettlementStatusCalculator(),
            auditMock.Object,
            new StubCurrentUserAccessor(),
            NullLogger<InvoiceSettlementService>.Instance);

        return new InvoiceSettlementTestHarness(db, service, auditMock, recordedAudits);
    }

    /// <summary>Runs the real allocation consumer over the supplied event.</summary>
    /// <param name="event">The allocation event.</param>
    /// <returns>A task completing when consumption finishes.</returns>
    public Task ConsumeAsync(PaymentAllocatedEvent @event)
    {
        PaymentAllocatedEventConsumer consumer = new(
            Service, NullLogger<PaymentAllocatedEventConsumer>.Instance);
        return consumer.Consume(ContextFor(@event));
    }

    /// <summary>Runs the real deallocation consumer over the supplied event.</summary>
    /// <param name="event">The deallocation event.</param>
    /// <returns>A task completing when consumption finishes.</returns>
    public Task ConsumeAsync(PaymentDeallocatedEvent @event)
    {
        PaymentDeallocatedEventConsumer consumer = new(
            Service, NullLogger<PaymentDeallocatedEventConsumer>.Instance);
        return consumer.Consume(ContextFor(@event));
    }

    /// <summary>Detaches every tracked entity so the next read comes from the database.</summary>
    public void ClearTracker()
    {
        _db.ChangeTracker.Clear();
    }

    private static ConsumeContext<TEvent> ContextFor<TEvent>(TEvent message)
        where TEvent : class
    {
        Mock<ConsumeContext<TEvent>> context = new();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }
}
