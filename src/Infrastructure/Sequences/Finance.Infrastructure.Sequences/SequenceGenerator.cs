using System.Data;
using Finance.Common.ErrorCodes;
using Finance.Infrastructure.Sequences.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Finance.Infrastructure.Sequences;

/// <summary>
/// Gapless sequence generator (SDD-INFRA-003 §2.2). Resolves the registered definition, computes
/// the composite counter key by reset policy, increments the counter row under a serializable
/// transaction (with <c>UPDLOCK, HOLDLOCK</c> on SQL Server), and returns the value formatted by
/// the registered <see cref="IDocumentNumberFormatter"/>. It uses no caching layer.
/// </summary>
/// <typeparam name="TContext">The owning <see cref="DbContext"/> exposing the sequences table.</typeparam>
public sealed class SequenceGenerator<TContext> : ISequenceGenerator
    where TContext : DbContext
{
    private const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";

    private readonly TContext _db;
    private readonly IReadOnlyDictionary<string, SequenceDefinition> _definitions;
    private readonly IDocumentNumberFormatter _formatter;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes the generator with its context, the registered definitions, the formatter seam,
    /// and a time provider for deterministic period-segment computation.
    /// </summary>
    /// <param name="db">The owning <see cref="DbContext"/> mapping <see cref="SequenceCounter"/>.</param>
    /// <param name="definitions">The registered sequence definitions keyed by sequence key.</param>
    /// <param name="formatter">The document-number formatter seam.</param>
    /// <param name="timeProvider">The clock used to compute the period segment.</param>
    public SequenceGenerator(
        TContext db,
        IReadOnlyDictionary<string, SequenceDefinition> definitions,
        IDocumentNumberFormatter formatter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(formatter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _db = db;
        _definitions = definitions;
        _formatter = formatter;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<string> NextAsync(string sequenceKey, CancellationToken cancellationToken)
    {
        SequenceDefinition definition = ResolveDefinition(sequenceKey);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string periodSegment = SequenceKeyComposer.PeriodSegment(definition.ResetPolicy, now);
        string compositeKey = SequenceKeyComposer.CompositeKey(sequenceKey, definition.ResetPolicy, now);

        long counter = await AllocateNextCounterAsync(compositeKey, now, cancellationToken).ConfigureAwait(false);
        return _formatter.Format(sequenceKey, periodSegment, counter);
    }

    /// <inheritdoc />
    public async Task<long> NextValueAsync(string sequenceKey, CancellationToken cancellationToken)
    {
        SequenceDefinition definition = ResolveDefinition(sequenceKey);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string compositeKey = SequenceKeyComposer.CompositeKey(sequenceKey, definition.ResetPolicy, now);

        return await AllocateNextCounterAsync(compositeKey, now, cancellationToken).ConfigureAwait(false);
    }

    private SequenceDefinition ResolveDefinition(string sequenceKey)
    {
        if (string.IsNullOrWhiteSpace(sequenceKey))
        {
            throw new ArgumentException(SequenceErrorCodes.UNKNOWN_SEQUENCE_KEY, nameof(sequenceKey));
        }

        if (!_definitions.TryGetValue(sequenceKey, out SequenceDefinition? definition))
        {
            throw new ArgumentException(SequenceErrorCodes.UNKNOWN_SEQUENCE_KEY, nameof(sequenceKey));
        }

        SequenceDefinitions.ValidatePadding(definition);
        return definition;
    }

    private async Task<long> AllocateNextCounterAsync(
        string compositeKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_db.Database.CurrentTransaction is not null)
        {
            return await IncrementCounterAsync(compositeKey, now, cancellationToken).ConfigureAwait(false);
        }

        IDbContextTransaction transaction = await _db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        await using (transaction.ConfigureAwait(false))
        {
            long nextValue = await IncrementCounterAsync(compositeKey, now, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return nextValue;
        }
    }

    private async Task<long> IncrementCounterAsync(
        string compositeKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        SequenceCounter? counter = await LoadLockedCounterAsync(compositeKey, cancellationToken).ConfigureAwait(false);
        if (counter is null)
        {
            SequenceCounter created = new() { Key = compositeKey, CurrentValue = 1, ModifiedAt = now };
            _db.Set<SequenceCounter>().Add(created);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return created.CurrentValue;
        }

        counter.CurrentValue += 1;
        counter.ModifiedAt = now;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return counter.CurrentValue;
    }

    private async Task<SequenceCounter?> LoadLockedCounterAsync(
        string compositeKey, CancellationToken cancellationToken)
    {
        if (!IsSqlServer())
        {
            return await _db.Set<SequenceCounter>()
                .FirstOrDefaultAsync(row => row.Key == compositeKey, cancellationToken)
                .ConfigureAwait(false);
        }

        return await _db.Set<SequenceCounter>()
            .FromSqlRaw(
                "SELECT [Key], [CurrentValue], [ModifiedAt] FROM [infrastructure].[Sequences] " +
                "WITH (UPDLOCK, HOLDLOCK) WHERE [Key] = {0}",
                compositeKey)
            .AsTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private bool IsSqlServer()
    {
        return string.Equals(_db.Database.ProviderName, SqlServerProviderName, StringComparison.Ordinal);
    }
}
