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
/// Unit tests for the SDD-AUDIT-001 §3 <c>BeforeJson</c> invariant enforced by
/// <see cref="AuditService{TContext}"/> over a SQLite in-memory <see cref="IAuditDbContext"/>. A
/// <see cref="AuditOperation.Create"/> entry MUST carry a <c>null</c> <see cref="AuditEntry.BeforeJson"/>,
/// while <see cref="AuditOperation.Update"/>, <see cref="AuditOperation.Delete"/>, and
/// <see cref="AuditOperation.StateChange"/> entries MUST carry a non-empty one; violations throw before
/// any row is written.
/// </summary>
[TestFixture]
[Category("SDD-AUDIT-001")]
public sealed class AuditServiceBeforeJsonInvariantTests
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

    /// <summary>A Create entry carrying a non-null BeforeJson violates the invariant and throws ArgumentException.</summary>
    [Test]
    public void RecordAsync_CreateWithNonNullBeforeJson_ThrowsArgumentException()
    {
        // Arrange
        AuditEntry entry = new AuditEntryBuilder()
            .WithOperation(AuditOperation.Create)
            .WithBeforeJson("{\"name\":\"Old\"}")
            .Build();

        // Act & Assert
        Assert.That(
            async () => await _service.RecordAsync(entry, CancellationToken.None),
            Throws.TypeOf<ArgumentException>());
    }

    /// <summary>An Update, Delete, or StateChange entry without a BeforeJson violates the invariant and throws.</summary>
    [TestCase(AuditOperation.Update)]
    [TestCase(AuditOperation.Delete)]
    [TestCase(AuditOperation.StateChange)]
    public void RecordAsync_RequiresBeforeJson_OnUpdateOrStateChange(AuditOperation operation)
    {
        // Arrange
        AuditEntry entry = new AuditEntryBuilder()
            .WithOperation(operation)
            .WithBeforeJson(null)
            .Build();

        // Act & Assert
        Assert.That(
            async () => await _service.RecordAsync(entry, CancellationToken.None),
            Throws.TypeOf<ArgumentException>());
    }

    /// <summary>An Update, Delete, or StateChange entry with an empty BeforeJson is treated as missing and throws.</summary>
    [TestCase(AuditOperation.Update)]
    [TestCase(AuditOperation.Delete)]
    [TestCase(AuditOperation.StateChange)]
    public void RecordAsync_EmptyBeforeJson_OnUpdateOrStateChange_ThrowsArgumentException(AuditOperation operation)
    {
        // Arrange
        AuditEntry entry = new AuditEntryBuilder()
            .WithOperation(operation)
            .WithBeforeJson(string.Empty)
            .Build();

        // Act & Assert
        Assert.That(
            async () => await _service.RecordAsync(entry, CancellationToken.None),
            Throws.TypeOf<ArgumentException>());
    }

    /// <summary>A Create entry with a null BeforeJson satisfies the invariant and is recorded successfully.</summary>
    [Test]
    public async Task RecordAsync_CreateWithNullBeforeJson_Succeeds()
    {
        // Arrange
        AuditEntry entry = new AuditEntryBuilder()
            .WithOperation(AuditOperation.Create)
            .WithBeforeJson(null)
            .Build();

        // Act
        Common.Results.Result result = await _service.RecordAsync(entry, CancellationToken.None, saveChanges: true);

        // Assert
        OperationsEvent persisted = await _context.OperationsEvents.SingleAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(persisted.BeforeJson, Is.Null);
        });
    }

    /// <summary>An Update entry carrying a non-empty BeforeJson satisfies the invariant and is recorded.</summary>
    [Test]
    public async Task RecordAsync_UpdateWithBeforeJson_Succeeds()
    {
        // Arrange
        AuditEntry entry = new AuditEntryBuilder()
            .WithEventType("AccountRenamed")
            .WithOperation(AuditOperation.Update)
            .WithBeforeJson("{\"name\":\"Cash\"}")
            .Build();

        // Act
        Common.Results.Result result = await _service.RecordAsync(entry, CancellationToken.None, saveChanges: true);

        // Assert
        OperationsEvent persisted = await _context.OperationsEvents.SingleAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(persisted.BeforeJson, Is.EqualTo("{\"name\":\"Cash\"}"));
        });
    }
}
