using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Infrastructure.Audit;
using Finance.Infrastructure.Audit.Entities;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Audit.Models;
using Finance.Infrastructure.Stateful.Tests.Audit.Builders;
using Finance.Infrastructure.Stateful.Tests.Audit.Fixtures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Finance.Infrastructure.Stateful.Tests.Audit;

/// <summary>
/// Unit tests for <see cref="AuditService{TContext}"/> over a SQLite in-memory
/// <see cref="IAuditDbContext"/> (SDD-AUDIT-001 §2.4, §3, §6). They verify the write path adds an
/// <see cref="OperationsEvent"/> to the ambient context, honours the caller-owns-transaction default
/// (no implicit <c>SaveChanges</c>), allows null <see cref="AuditEntry.BeforeJson"/> on create, and
/// returns <see cref="AuditErrorCodes.AUDIT_REASON_REQUIRED"/> for a reasonless sensitive event.
/// Real SQL Server transaction ordering and DENY grants are <c>[Category("Integration")]</c> and
/// excluded from the default run.
/// </summary>
[TestFixture]
[Category("SDD-AUDIT-001")]
public sealed class AuditServiceTests
{
    private SqliteConnection _connection = null!;
    private TestAuditDbContext _context = null!;
    private IAuditService _service = null!;

    /// <summary>Opens an in-memory SQLite context and builds a fresh audit service before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<TestAuditDbContext> options =
            new DbContextOptionsBuilder<TestAuditDbContext>()
                .UseSqlite(_connection)
                .Options;

        _context = new TestAuditDbContext(options);
        _context.Database.EnsureCreated();
        _service = new AuditService<TestAuditDbContext>(_context, NullLogger<AuditService<TestAuditDbContext>>.Instance);
    }

    /// <summary>Disposes the context and connection after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>RecordAsync adds an OperationsEvent carrying the audit-entry data into the ambient context.</summary>
    [Test]
    public async Task RecordAsync_PersistsOperationsEventIntoAuditDbContext()
    {
        // Arrange
        AuditEntry entry = new AuditEntryBuilder()
            .WithEventType("AccountCreated")
            .Build();

        // Act
        Result result = await _service.RecordAsync(entry, CancellationToken.None, saveChanges: true);

        // Assert
        OperationsEvent? persisted = await _context.OperationsEvents.SingleOrDefaultAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(persisted, Is.Not.Null);
            Assert.That(persisted!.EventType, Is.EqualTo("AccountCreated"));
            Assert.That(persisted.EntityType, Is.EqualTo("Account"));
            Assert.That(persisted.AfterJson, Is.EqualTo("{\"name\":\"Cash\"}"));
        });
    }

    /// <summary>With the default saveChanges=false the row is tracked but not committed (caller owns the transaction).</summary>
    [Test]
    public async Task RecordAsync_DoesNotCallSaveChanges_WhenCallerOwnsTransaction()
    {
        // Arrange
        AuditEntry entry = new AuditEntryBuilder().Build();

        // Act
        Result result = await _service.RecordAsync(entry, CancellationToken.None);

        // Assert
        int committedCount = await _context.OperationsEvents.CountAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_context.ChangeTracker.Entries<OperationsEvent>().Count(), Is.EqualTo(1));
            Assert.That(committedCount, Is.EqualTo(0));
        });
    }

    /// <summary>When the caller later commits, the tracked audit row is persisted (atomic-with-change pattern).</summary>
    [Test]
    public async Task RecordAsync_TrackedRow_IsPersistedWhenCallerSaves()
    {
        // Arrange
        AuditEntry entry = new AuditEntryBuilder().Build();
        await _service.RecordAsync(entry, CancellationToken.None);

        // Act
        await _context.SaveChangesAsync(CancellationToken.None);

        // Assert
        int committedCount = await _context.OperationsEvents.CountAsync(CancellationToken.None);
        Assert.That(committedCount, Is.EqualTo(1));
    }

    /// <summary>A create event with null BeforeJson is accepted and persisted with null BeforeJson.</summary>
    [Test]
    public async Task RecordAsync_AllowsNullBeforeJson_OnCreate()
    {
        // Arrange
        AuditEntry entry = new AuditEntryBuilder()
            .WithEventType("AccountCreated")
            .WithBeforeJson(null)
            .Build();

        // Act
        Result result = await _service.RecordAsync(entry, CancellationToken.None, saveChanges: true);

        // Assert
        OperationsEvent persisted = await _context.OperationsEvents.SingleAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(persisted.BeforeJson, Is.Null);
        });
    }

    /// <summary>A high-sensitivity event without a reason returns failure with AUDIT_REASON_REQUIRED and persists nothing.</summary>
    [Test]
    public async Task RecordAsync_ReturnsFailure_WhenReasonMissing_ForSensitiveOp()
    {
        // Arrange
        AuditEntry entry = new AuditEntryBuilder()
            .WithEventType(SensitiveAuditEventTypes.PeriodClosed)
            .WithOperation(AuditOperation.StateChange)
            .WithBeforeJson("{\"state\":\"Open\"}")
            .WithReason(null)
            .Build();

        // Act
        Result result = await _service.RecordAsync(entry, CancellationToken.None, saveChanges: true);

        // Assert
        int trackedCount = _context.ChangeTracker.Entries<OperationsEvent>().Count();
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(AuditErrorCodes.AUDIT_REASON_REQUIRED));
            Assert.That(trackedCount, Is.EqualTo(0));
        });
    }

    /// <summary>A high-sensitivity event with a supplied reason is recorded successfully.</summary>
    [Test]
    public async Task RecordAsync_PersistsSensitiveOp_WhenReasonSupplied()
    {
        // Arrange
        AuditEntry entry = new AuditEntryBuilder()
            .WithEventType(SensitiveAuditEventTypes.JournalEntryReversed)
            .WithOperation(AuditOperation.StateChange)
            .WithBeforeJson("{\"status\":\"Posted\"}")
            .WithReason("Correcting a misposting.")
            .Build();

        // Act
        Result result = await _service.RecordAsync(entry, CancellationToken.None, saveChanges: true);

        // Assert
        OperationsEvent persisted = await _context.OperationsEvents.SingleAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(persisted.Reason, Is.EqualTo("Correcting a misposting."));
        });
    }

    /// <summary>A whitespace-only reason on a sensitive event is treated as missing and rejected.</summary>
    [Test]
    public async Task RecordAsync_ReturnsFailure_WhenSensitiveReasonIsWhitespace()
    {
        // Arrange
        AuditEntry entry = new AuditEntryBuilder()
            .WithEventType(SensitiveAuditEventTypes.AccountDeactivated)
            .WithOperation(AuditOperation.StateChange)
            .WithBeforeJson("{\"active\":true}")
            .WithReason("   ")
            .Build();

        // Act
        Result result = await _service.RecordAsync(entry, CancellationToken.None, saveChanges: true);

        // Assert
        Assert.That(result.ErrorCode, Is.EqualTo(AuditErrorCodes.AUDIT_REASON_REQUIRED));
    }

    /// <summary>
    /// The Periods fiscal-period close and reopen event types are on the mandatory-reason list (Fix #5c,
    /// SDD-FIN-004 §2.4-§2.5, SDD-AUDIT-001 §3): recording either without a reason fails with
    /// AUDIT_REASON_REQUIRED and persists nothing.
    /// </summary>
    [TestCase(SensitiveAuditEventTypes.FiscalPeriodClosed)]
    [TestCase(SensitiveAuditEventTypes.FiscalPeriodReopened)]
    public async Task RecordAsync_ReturnsFailure_WhenReasonMissing_ForFiscalPeriodEvent(string eventType)
    {
        // Arrange
        AuditEntry entry = new AuditEntryBuilder()
            .WithEventType(eventType)
            .WithOperation(AuditOperation.StateChange)
            .WithBeforeJson("{\"state\":\"Open\"}")
            .WithReason(null)
            .Build();

        // Act
        Result result = await _service.RecordAsync(entry, CancellationToken.None, saveChanges: true);

        // Assert
        int trackedCount = _context.ChangeTracker.Entries<OperationsEvent>().Count();
        Assert.Multiple(() =>
        {
            Assert.That(SensitiveAuditEventTypes.RequiresReason(eventType), Is.True);
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(AuditErrorCodes.AUDIT_REASON_REQUIRED));
            Assert.That(trackedCount, Is.EqualTo(0));
        });
    }

    /// <summary>A null audit entry is rejected with an ArgumentNullException.</summary>
    [Test]
    public void RecordAsync_NullEntry_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.That(
            async () => await _service.RecordAsync(null!, CancellationToken.None),
            Throws.TypeOf<ArgumentNullException>());
    }
}
