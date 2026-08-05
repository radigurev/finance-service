using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// Counts the database commands EF Core executes, so the SDD-PAY-003 §2.6 "one grouped round trip" requirement
/// is assertable: the aging aggregation MUST NOT issue one query per counterparty or per bucket.
/// </summary>
public sealed class SqlitePaymentsCommandCounter : DbCommandInterceptor
{
    /// <summary>The number of commands executed since the last <see cref="Reset"/>.</summary>
    public int CommandCount { get; private set; }

    /// <summary>Resets the counter so a single service call can be measured in isolation.</summary>
    public void Reset() => CommandCount = 0;

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        CommandCount++;
        return base.ReaderExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        CommandCount++;
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        CommandCount++;
        return base.ScalarExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        CommandCount++;
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}
