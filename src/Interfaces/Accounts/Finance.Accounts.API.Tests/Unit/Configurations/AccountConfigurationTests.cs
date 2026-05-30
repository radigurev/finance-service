using Finance.Accounts.DBModel;
using Finance.Accounts.DBModel.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NUnit.Framework;

namespace Finance.Accounts.API.Tests.Unit.Configurations;

/// <summary>
/// Unit tests for the production <c>AccountConfiguration</c> Fluent API mapping (SDD-ACCT-001 §2.6, §2.10).
/// Builds the real <see cref="AccountsDbContext"/> model (no test customizations) and inspects metadata.
/// </summary>
[TestFixture]
[Category("SDD-ACCT-001")]
public sealed class AccountConfigurationTests
{
    private SqliteConnection _connection = null!;
    private AccountsDbContext _context = null!;

    /// <summary>Builds the real model over an in-memory SQLite connection before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<AccountsDbContext> options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AccountsDbContext(options);
    }

    /// <summary>Disposes the context and connection after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>The account maps to the Accounts table in the accounts schema.</summary>
    [Test]
    public void AccountConfiguration_MapsToAccountsTableInAccountsSchema()
    {
        // Arrange
        IEntityType entityType = _context.Model.FindEntityType(typeof(Account))!;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(entityType.GetTableName(), Is.EqualTo("Accounts"));
            Assert.That(entityType.GetSchema(), Is.EqualTo("accounts"));
        });
    }

    /// <summary>There is a unique index over (CountryCode, Code) enforcing per-country code uniqueness.</summary>
    [Test]
    public void AccountConfiguration_HasUniqueIndexOnCountryAndCode()
    {
        // Arrange
        IEntityType entityType = _context.Model.FindEntityType(typeof(Account))!;

        // Act
        IIndex? compositeIndex = entityType.GetIndexes().FirstOrDefault(index =>
            index.Properties.Count == 2
            && index.Properties[0].Name == nameof(Account.CountryCode)
            && index.Properties[1].Name == nameof(Account.Code));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(compositeIndex, Is.Not.Null);
            Assert.That(compositeIndex!.IsUnique, Is.True);
        });
    }

    /// <summary>RowVersion is configured as a store-generated concurrency token (SDD-ACCT-001 §2.10).</summary>
    [Test]
    public void AccountConfiguration_ConfiguresRowVersionConcurrencyToken()
    {
        // Arrange
        IEntityType entityType = _context.Model.FindEntityType(typeof(Account))!;
        IProperty rowVersion = entityType.FindProperty(nameof(Account.RowVersion))!;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(rowVersion.IsConcurrencyToken, Is.True);
            Assert.That(rowVersion.ValueGenerated, Is.EqualTo(ValueGenerated.OnAddOrUpdate));
        });
    }
}
