using Finance.Infrastructure.Audit.Entities;
using Finance.Infrastructure.Sequences;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel;
using Finance.Payments.DBModel.Models;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Persistence;

/// <summary>
/// Unit tests over the PRODUCTION <see cref="PaymentsDbContext"/> model (SDD-PAY-001 §2.16/§6.6,
/// SDD-PAY-002 §2.12/§6.5). They read <see cref="IModel"/> metadata only — no connection is opened — and they
/// deliberately bypass the SQLite test customizer so the real <c>decimal(18,2)</c>/<c>decimal(18,6)</c> column
/// types, the UNIQUE FILTERED indexes, and the <c>rowversion</c> concurrency tokens are asserted as configured.
/// </summary>
[TestFixture]
public sealed class PaymentsModelConfigurationTests
{
    private PaymentsDbContext _context = null!;
    private IModel _model = null!;

    /// <summary>Builds the production model before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _context = PaymentsModelFactory.CreateContext();
        _model = _context.Model;
    }

    /// <summary>Disposes the model-only context after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    [Category("SDD-PAY-001")]
    public void PaymentConfiguration_HasUniqueFilteredIndexOnDocumentNumber()
    {
        // Arrange
        IEntityType payment = EntityTypeOf<Payment>();

        // Act
        IIndex index = IndexNamed(payment, "IX_Payments_DocumentNumber");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(index.IsUnique, Is.True);
            Assert.That(index.GetFilter(), Is.EqualTo("[DocumentNumber] IS NOT NULL"));
            Assert.That(
                index.Properties.Select(property => property.Name),
                Is.EqualTo(new[] { nameof(Payment.DocumentNumber) }));
        });
    }

    [Test]
    [Category("SDD-PAY-001")]
    public void PaymentConfiguration_HasUniqueFilteredIndexOnJournalEntryId()
    {
        // Arrange
        IEntityType payment = EntityTypeOf<Payment>();

        // Act
        IIndex index = IndexNamed(payment, "IX_Payments_JournalEntryId");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(index.IsUnique, Is.True);
            Assert.That(index.GetFilter(), Is.EqualTo("[JournalEntryId] IS NOT NULL"));
            Assert.That(
                index.Properties.Select(property => property.Name),
                Is.EqualTo(new[] { nameof(Payment.JournalEntryId) }));
        });
    }

    [Test]
    [Category("SDD-PAY-001")]
    public void PaymentConfiguration_ConfiguresRowVersionConcurrencyToken()
    {
        // Arrange
        IEntityType payment = EntityTypeOf<Payment>();

        // Act
        IProperty rowVersion = payment.FindProperty(nameof(Payment.RowVersion))!;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(rowVersion.IsConcurrencyToken, Is.True);
            Assert.That(rowVersion.ValueGenerated, Is.EqualTo(ValueGenerated.OnAddOrUpdate));
        });
    }

    [Test]
    [Category("SDD-PAY-001")]
    public void PaymentConfiguration_StoresEnumsAsStrings_AndAmountsAsDecimal18x2()
    {
        // Arrange
        IEntityType payment = EntityTypeOf<Payment>();

        // Act
        IProperty status = payment.FindProperty(nameof(Payment.Status))!;
        IProperty documentType = payment.FindProperty(nameof(Payment.DocumentType))!;
        IProperty direction = payment.FindProperty(nameof(Payment.Direction))!;
        IProperty method = payment.FindProperty(nameof(Payment.Method))!;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(status.GetProviderClrType(), Is.EqualTo(typeof(string)));
            Assert.That(status.GetMaxLength(), Is.EqualTo(20));
            Assert.That(documentType.GetProviderClrType(), Is.EqualTo(typeof(string)));
            Assert.That(documentType.GetMaxLength(), Is.EqualTo(30));
            Assert.That(direction.GetProviderClrType(), Is.EqualTo(typeof(string)));
            Assert.That(direction.GetMaxLength(), Is.EqualTo(2));
            Assert.That(method.GetProviderClrType(), Is.EqualTo(typeof(string)));
            Assert.That(method.GetMaxLength(), Is.EqualTo(20));
            Assert.That(ColumnTypeOf(payment, nameof(Payment.Amount)), Is.EqualTo("decimal(18,2)"));
            Assert.That(ColumnTypeOf(payment, nameof(Payment.BaseAmount)), Is.EqualTo("decimal(18,2)"));
            Assert.That(ColumnTypeOf(payment, nameof(Payment.AllocatedAmount)), Is.EqualTo("decimal(18,2)"));
            Assert.That(ColumnTypeOf(payment, nameof(Payment.ExchangeRate)), Is.EqualTo("decimal(18,6)"));
        });
    }

    [Test]
    [Category("SDD-PAY-001")]
    public void PaymentConfiguration_IgnoresUnallocatedAmount()
    {
        // Arrange
        IEntityType payment = EntityTypeOf<Payment>();

        // Act
        IProperty? unallocated = payment.FindProperty(nameof(Payment.UnallocatedAmount));

        // Assert
        Assert.That(unallocated, Is.Null, "UnallocatedAmount is computed on read, never stored");
    }

    [Test]
    [Category("SDD-PAY-001")]
    public void PaymentStatusHistoryConfiguration_CascadesFromPayment_AndIsNotAutoIncluded()
    {
        // Arrange
        IEntityType payment = EntityTypeOf<Payment>();
        IEntityType history = EntityTypeOf<PaymentStatusHistory>();

        // Act
        INavigation navigation = payment.FindNavigation(nameof(Payment.StatusHistory))!;
        IForeignKey foreignKey = history.GetForeignKeys().Single();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(foreignKey.DeleteBehavior, Is.EqualTo(DeleteBehavior.Cascade));
            Assert.That(foreignKey.GetConstraintName(), Is.EqualTo("FK_PaymentStatusHistory_Payments"));
            Assert.That(
                navigation.FindAnnotation("EagerLoaded")?.Value,
                Is.Not.EqualTo(true),
                "the history collection must NOT be AutoInclude()d onto every payment read");
            Assert.That(history.GetTableName(), Is.EqualTo("PaymentStatusHistory"));
            Assert.That(history.GetSchema(), Is.EqualTo("payments"));
        });
    }

    [Test]
    [Category("SDD-PAY-001")]
    public void PaymentsDbContext_AppliesAuditSequenceAndOutboxConfigurations()
    {
        // Arrange
        IReadOnlyList<Type> expected =
        [
            typeof(OperationsEvent),
            typeof(SequenceCounter),
            typeof(InboxState),
            typeof(OutboxMessage),
            typeof(OutboxState)
        ];

        // Act
        IReadOnlyList<Type> mapped = [.. _model.GetEntityTypes().Select(entity => entity.ClrType)];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(mapped, Is.SupersetOf(expected));
            Assert.That(EntityTypeOf<OperationsEvent>().GetSchema(), Is.EqualTo("audit"));
            Assert.That(EntityTypeOf<SequenceCounter>().GetSchema(), Is.EqualTo("infrastructure"));
            Assert.That(_model.GetDefaultSchema(), Is.EqualTo("payments"));
        });
    }

    [Test]
    [Category("SDD-PAY-002")]
    public void PaymentAllocationConfiguration_HasUniqueIndexOnPaymentIdAndInvoiceId()
    {
        // Arrange
        IEntityType allocation = EntityTypeOf<PaymentAllocation>();

        // Act
        IIndex index = IndexNamed(allocation, "IX_PaymentAllocations_PaymentInvoice");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(index.IsUnique, Is.True);
            Assert.That(index.GetFilter(), Is.Null, "both columns are NOT NULL, so the index is unfiltered");
            Assert.That(
                index.Properties.Select(property => property.Name),
                Is.EqualTo(new[] { nameof(PaymentAllocation.PaymentId), nameof(PaymentAllocation.InvoiceId) }));
            Assert.That(
                allocation.GetForeignKeys().Single().DeleteBehavior,
                Is.EqualTo(DeleteBehavior.Cascade));
        });
    }

    [Test]
    [Category("SDD-PAY-002")]
    public void PaymentAllocationConfiguration_ConfiguresRowVersionConcurrencyToken()
    {
        // Arrange
        IEntityType allocation = EntityTypeOf<PaymentAllocation>();

        // Act
        IProperty rowVersion = allocation.FindProperty(nameof(PaymentAllocation.RowVersion))!;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(rowVersion.IsConcurrencyToken, Is.True);
            Assert.That(rowVersion.ValueGenerated, Is.EqualTo(ValueGenerated.OnAddOrUpdate));
            Assert.That(
                ColumnTypeOf(allocation, nameof(PaymentAllocation.AllocatedAmount)),
                Is.EqualTo("decimal(18,2)"));
            Assert.That(
                ColumnTypeOf(allocation, nameof(PaymentAllocation.RealizedFxDifference)),
                Is.EqualTo("decimal(18,2)"));
        });
    }

    [Test]
    [Category("SDD-PAY-002")]
    public void InvoiceOpenItemConfiguration_UsesInvoiceIdAsPrimaryKey_NeverGenerated()
    {
        // Arrange
        IEntityType openItem = EntityTypeOf<InvoiceOpenItem>();

        // Act
        IKey primaryKey = openItem.FindPrimaryKey()!;
        IProperty invoiceId = openItem.FindProperty(nameof(InvoiceOpenItem.InvoiceId))!;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(
                primaryKey.Properties.Select(property => property.Name),
                Is.EqualTo(new[] { nameof(InvoiceOpenItem.InvoiceId) }));
            Assert.That(primaryKey.GetName(), Is.EqualTo("PK_InvoiceOpenItems"));
            Assert.That(invoiceId.ValueGenerated, Is.EqualTo(ValueGenerated.Never));
            Assert.That(
                openItem.FindProperty(nameof(InvoiceOpenItem.Outstanding)),
                Is.Null,
                "Outstanding is computed on read, never stored");
            Assert.That(
                openItem.FindProperty(nameof(InvoiceOpenItem.RowVersion))!.IsConcurrencyToken,
                Is.True,
                "the projection row version serializes two payments allocating against the same invoice");
            Assert.That(
                ColumnTypeOf(openItem, nameof(InvoiceOpenItem.BookingExchangeRate)),
                Is.EqualTo("decimal(18,6)"));
        });
    }

    [Test]
    [Category("SDD-PAY-002")]
    public void InvoiceOpenItemConfiguration_HasTheThreeDocumentedIndexes()
    {
        // Arrange
        IEntityType openItem = EntityTypeOf<InvoiceOpenItem>();

        // Act
        IReadOnlyList<string?> indexNames = [.. openItem.GetIndexes().Select(index => index.GetDatabaseName())];

        // Assert
        Assert.That(indexNames, Is.EquivalentTo(new[]
        {
            "IX_InvoiceOpenItems_CounterpartyId",
            "IX_InvoiceOpenItems_DueDate",
            "IX_InvoiceOpenItems_Direction_InvoiceStatus_CounterpartyId_DueDate"
        }));
    }

    /// <summary>Resolves the mapped entity type for a CLR type.</summary>
    /// <typeparam name="TEntity">The CLR entity type.</typeparam>
    /// <returns>The mapped entity type.</returns>
    private IEntityType EntityTypeOf<TEntity>() => _model.FindEntityType(typeof(TEntity))!;

    /// <summary>Resolves an index by its configured database name.</summary>
    /// <param name="entityType">The owning entity type.</param>
    /// <param name="databaseName">The configured index name.</param>
    /// <returns>The matching index.</returns>
    private static IIndex IndexNamed(IEntityType entityType, string databaseName) => entityType
        .GetIndexes()
        .Single(index => index.GetDatabaseName() == databaseName);

    /// <summary>Reads the configured store column type of a property.</summary>
    /// <param name="entityType">The owning entity type.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The configured column type.</returns>
    private static string? ColumnTypeOf(IEntityType entityType, string propertyName) => entityType
        .FindProperty(propertyName)!
        .GetColumnType();
}
