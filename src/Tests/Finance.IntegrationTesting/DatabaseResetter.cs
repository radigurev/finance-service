using Microsoft.Data.SqlClient;
using Respawn;
using Respawn.Graph;

namespace Finance.IntegrationTesting;

/// <summary>
/// Resets a test database to an empty state between tests using Respawn, deleting all rows while
/// preserving the schema and the EF migrations history. Create one per fixture with the service's
/// test connection string and call <see cref="ResetAsync"/> in the per-test setup.
/// </summary>
public sealed class DatabaseResetter
{
    private readonly string _connectionString;
    private Respawner? _respawner;

    /// <summary>Initializes the resetter for the given database connection string.</summary>
    public DatabaseResetter(string connectionString) => _connectionString = connectionString;

    /// <summary>
    /// Deletes all data from the database, preserving schema and the migrations history table. Retries
    /// on SQL deadlock (1205), which can occur when Respawn's bulk delete races the background
    /// MassTransit bus-outbox delivery service.
    /// </summary>
    public async Task ResetAsync()
    {
        const int maxAttempts = 5;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await ResetOnceAsync();
                return;
            }
            catch (SqlException ex) when (ex.Number == 1205 && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt));
            }
        }
    }

    private async Task ResetOnceAsync()
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        _respawner ??= await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            TablesToIgnore = [new Table("__EFMigrationsHistory")]
        });

        await _respawner.ResetAsync(connection);
    }
}
