using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tenancy.Models;

namespace Persistence.Tests.Support;

/// <summary>
/// A real relational database shared by independently-created contexts.
/// <para>
/// SQLite is used only by the test project. SQL-Server-specific schema facts are
/// asserted against the generated SQL Server model and migration.
/// </para>
/// </summary>
internal sealed class RelationalTestContext : IAsyncDisposable
{
    private readonly SqliteConnection _anchorConnection;
    private readonly string _connectionString;

    private RelationalTestContext(
        SqliteConnection anchorConnection,
        string connectionString)
    {
        _anchorConnection = anchorConnection;
        _connectionString = connectionString;
    }

    public static async Task<RelationalTestContext> CreateAsync(
        bool applyMigrations = true)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = $"taxai-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        };

        var connectionString = builder.ToString();
        var anchor = OpenConnection(connectionString);
        var relational = new RelationalTestContext(anchor, connectionString);

        if (applyMigrations)
        {
            await using var context = relational.CreateContext();
            var targetMigration = context.Database.GetMigrations().Single();
            await context.Database.MigrateAsync(targetMigration);
        }

        return relational;
    }

    public TaxAiDbContext CreateContext()
    {
        var connection = OpenConnection(_connectionString);
        var options = new DbContextOptionsBuilder<TaxAiDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .AddInterceptors(SqliteMigrationLockCleanupInterceptor.Instance)
            .EnableDetailedErrors()
            .Options;

        return new TaxAiDbContext(options);
    }

    public static TaxAiDbContext CreateSqlServerContext()
    {
        var options = new DbContextOptionsBuilder<TaxAiDbContext>()
            .UseSqlServer()
            .EnableDetailedErrors()
            .Options;

        return new TaxAiDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        await _anchorConnection.DisposeAsync();
    }

    private static SqliteConnection OpenConnection(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        connection.CreateCollation(
            PersistedSchema.IdentifierCollation,
            static (left, right) => StringComparer.Ordinal.Compare(left, right));
        return connection;
    }

    /// <summary>
    /// EF9 added a provider-owned SQLite migration-lock table. The relational
    /// harness has no concurrent migrator and historically asserts that rolling
    /// the one migration back leaves no user tables, so remove the empty lock
    /// table immediately after the provider releases it.
    /// </summary>
    private sealed class SqliteMigrationLockCleanupInterceptor :
        DbCommandInterceptor
    {
        internal static readonly SqliteMigrationLockCleanupInterceptor Instance =
            new();

        public override int NonQueryExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result)
        {
            if (IsMigrationLockRelease(command))
            {
                using var cleanup = command.Connection!.CreateCommand();
                cleanup.CommandText =
                    "DROP TABLE IF EXISTS \"__EFMigrationsLock\";";
                cleanup.ExecuteNonQuery();
            }

            return result;
        }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (IsMigrationLockRelease(command))
            {
                await using var cleanup = command.Connection!.CreateCommand();
                cleanup.CommandText =
                    "DROP TABLE IF EXISTS \"__EFMigrationsLock\";";
                await cleanup.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }

        public override object? ScalarExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            object? result)
        {
            if (IsMigrationLockRelease(command))
            {
                using var cleanup = command.Connection!.CreateCommand();
                cleanup.CommandText =
                    "DROP TABLE IF EXISTS \"__EFMigrationsLock\";";
                cleanup.ExecuteNonQuery();
            }

            return result;
        }

        public override async ValueTask<object?> ScalarExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            object? result,
            CancellationToken cancellationToken = default)
        {
            if (IsMigrationLockRelease(command))
            {
                await using var cleanup = command.Connection!.CreateCommand();
                cleanup.CommandText =
                    "DROP TABLE IF EXISTS \"__EFMigrationsLock\";";
                await cleanup.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }

        private static bool IsMigrationLockRelease(DbCommand command) =>
            command.CommandText.StartsWith(
                "DELETE FROM \"__EFMigrationsLock\"",
                StringComparison.Ordinal);
    }
}
