using Finance.Accounts.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Accounts.API.Tests.Fixtures;

/// <summary>
/// Post-processes the built model so the <see cref="Account.RowVersion"/> concurrency token is
/// application-generated instead of store-generated. SQL Server auto-fills <c>rowversion</c>, but SQLite
/// cannot, so the production <c>IsRowVersion()</c> mapping is rewritten to a never-generated concurrency
/// token; <see cref="SqliteRowVersionInterceptor"/> then supplies the value on each write. This keeps the
/// optimistic-concurrency behavior (SDD-ACCT-001 §2.10) observable in offline SQLite unit tests without
/// touching production code.
/// </summary>
public sealed class SqliteRowVersionModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Creates the customizer with the supplied dependencies.</summary>
    /// <param name="dependencies">The model-customizer dependencies supplied by EF Core.</param>
    public SqliteRowVersionModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        IMutableProperty? rowVersion = modelBuilder.Model
            .FindEntityType(typeof(Account))?
            .FindProperty(nameof(Account.RowVersion));

        if (rowVersion is not null)
        {
            rowVersion.ValueGenerated = ValueGenerated.Never;
            rowVersion.IsConcurrencyToken = true;
        }
    }
}
