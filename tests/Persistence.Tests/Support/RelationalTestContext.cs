using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
            await context.Database.MigrateAsync();
        }

        return relational;
    }

    public TaxAiDbContext CreateContext()
    {
        var connection = OpenConnection(_connectionString);
        var options = new DbContextOptionsBuilder<TaxAiDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
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
}
