using Finance.EventLog.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Finance.EventLog.API.Tests.Fixtures;

/// <summary>
/// Post-processes the built model for the offline SQLite unit run (SDD-EVTLOG-001 §6). It removes the
/// SQL-Server-specific <c>SYSDATETIMEOFFSET()</c> default on <see cref="EventLogEntry.ReceivedAt"/> (the
/// consumers always supply <c>ReceivedAt</c> explicitly per §2.2) and converts both
/// <see cref="DateTimeOffset"/> columns to UTC ticks, because SQLite cannot order or filter on a native
/// <c>DateTimeOffset</c>. Ticks preserve chronological ordering, so the default <c>OccurredAt DESC</c> sort
/// (§2.4), range filtering (§3), and the retention purge (§2.7) all behave as on SQL Server. Production is
/// unaffected — it uses the real SQL Server <c>datetimeoffset</c> mapping.
/// </summary>
public sealed class SqliteEventLogModelCustomizer : RelationalModelCustomizer
{
    private static readonly ValueConverter<DateTimeOffset, long> TicksConverter =
        new(value => value.UtcDateTime.Ticks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    /// <summary>Creates the customizer with the supplied dependencies.</summary>
    /// <param name="dependencies">The model-customizer dependencies supplied by EF Core.</param>
    public SqliteEventLogModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        IMutableEntityType? entity = modelBuilder.Model.FindEntityType(typeof(EventLogEntry));
        if (entity is null)
        {
            return;
        }

        ConfigureReceivedAt(entity);
        ConvertToTicks(entity, nameof(EventLogEntry.OccurredAt));
        ConvertToTicks(entity, nameof(EventLogEntry.ReceivedAt));
    }

    private static void ConfigureReceivedAt(IMutableEntityType entity)
    {
        IMutableProperty? receivedAt = entity.FindProperty(nameof(EventLogEntry.ReceivedAt));
        if (receivedAt is not null)
        {
            receivedAt.SetDefaultValueSql(null);
            receivedAt.ValueGenerated = ValueGenerated.Never;
        }
    }

    private static void ConvertToTicks(IMutableEntityType entity, string propertyName)
    {
        IMutableProperty? property = entity.FindProperty(propertyName);
        property?.SetValueConverter(TicksConverter);
    }
}
