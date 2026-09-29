using System.Security.Cryptography;
using ClientIntake.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Persistence.Entities;
using Persistence.Repositories;
using Persistence.Tests.Support;

namespace Persistence.Tests.Lifecycle;

/// <summary>
/// Spec 016 T04 — the one additive source-registry migration.
/// <para>
/// It creates exactly one firm-scoped table, touches no existing table except
/// to reference the case and document it belongs to, carries no data operation
/// or raw SQL, and applies over — and rolls back to — the prior schema without
/// changing any existing row. The earlier migrations are unchanged.
/// </para>
/// </summary>
public sealed class SourceRegistryMigrationTests
{
    private const string MigrationId = "20260927000000_AddSourceRegistry";
    private const string PriorMigrationId = "20260803000000_AddDocumentExtraction";

    private static readonly string MigrationRoot = Path.Combine(FindRepositoryRoot(), "src", "Persistence", "Migrations");

    private static Migration Load()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var migrations = context.GetService<IMigrationsAssembly>();
        return migrations.CreateMigration(migrations.Migrations[MigrationId], context.Database.ProviderName!);
    }

    [Fact]
    public void TheMigrationIsLastAndCreatesExactlyTheSourceRegistryTable()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        Assert.Equal(MigrationId, context.GetService<IMigrationsAssembly>().Migrations.Keys.Last());

        var create = Assert.Single(Load().UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("SourceRegistrations", create.Name);
        Assert.Equal(
            new[] { "BoundAtUtc", "CaseId", "ContentKey", "DocumentId", "FirmId", "PurgeRecordedAtUtc", "ResourceId", "StagedAtUtc", "State" },
            create.Columns.Select(column => column.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(new[] { "FirmId", "CaseId", "ResourceId" }, create.PrimaryKey!.Columns);
    }

    [Fact] // No column for source bytes, key material, file names, provider or taxpayer values.
    public void TheTableHasOnlyStructuralColumns()
    {
        var create = Assert.Single(Load().UpOperations.OfType<CreateTableOperation>());

        Assert.DoesNotContain(create.Columns, column =>
            column.ClrType == typeof(byte[]) ||
            column.Name.Contains("Name", StringComparison.OrdinalIgnoreCase) ||
            column.Name.Contains("Sin", StringComparison.Ordinal) ||
            column.Name.Contains("Url", StringComparison.OrdinalIgnoreCase) ||
            column.Name.Contains("Sas", StringComparison.OrdinalIgnoreCase) ||
            column.Name.Contains("Wrapped", StringComparison.OrdinalIgnoreCase) ||
            column.Name.Contains("Provider", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EveryIndexAndForeignKeyLeadsWithTheFirm()
    {
        var migration = Load();

        Assert.All(migration.UpOperations.OfType<CreateIndexOperation>(), index => Assert.Equal("FirmId", index.Columns[0]));
        var create = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.All(create.ForeignKeys, key => Assert.Equal("FirmId", key.Columns[0]));
        Assert.Equal(
            new[] { "IntakeCases", "IntakeDocuments" },
            create.ForeignKeys.Select(key => key.PrincipalTable).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(create.ForeignKeys, key => Assert.Equal(ReferentialAction.Restrict, key.OnDelete));
    }

    [Fact] // Additive only.
    public void TheMigrationAltersNoExistingTableAndCarriesNoDataOrRawSql()
    {
        var migration = Load();

        Assert.Empty(migration.UpOperations.Where(operation =>
            operation is AlterTableOperation or AlterColumnOperation or AddColumnOperation or DropColumnOperation or
                DropTableOperation or RenameTableOperation or RenameColumnOperation or SqlOperation or
                InsertDataOperation or UpdateDataOperation or DeleteDataOperation));
        Assert.All(
            migration.UpOperations.OfType<CreateIndexOperation>(),
            index => Assert.Equal("SourceRegistrations", index.Table));
        Assert.Equal(new[] { "SourceRegistrations" }, migration.DownOperations.OfType<DropTableOperation>().Select(operation => operation.Name));
        Assert.Single(migration.DownOperations);
    }

    [Fact] // Old schema: apply over existing rows and roll back without touching them.
    public async Task TheMigrationAppliesOverTheOldSchemaAndRollsBackWithoutChangingExistingRows()
    {
        await using var database = await RelationalTestContext.CreateAsync(applyMigrations: false);
        await using (var context = database.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(PriorMigrationId);
            context.Add(SyntheticFixtures.Firm());
            await context.SaveChangesAsync();
            Assert.Equal(IntakeStoreWriteCode.Success, await new SqlIntakeStore(context).CreateAsync(SyntheticFixtures.IntakeCase(), CancellationToken.None));
            Assert.DoesNotContain("SourceRegistrations", await TablesAsync(context));
        }

        await using (var context = database.CreateContext())
        {
            await context.Database.MigrateAsync();
            Assert.Contains("SourceRegistrations", await TablesAsync(context));
            Assert.Equal(1, await context.Set<IntakeCaseEntity>().CountAsync());
            Assert.Equal(0, await context.Set<SourceRegistrationEntity>().CountAsync());
        }

        await using (var context = database.CreateContext())
        {
            Assert.Equal(
                SourceRegistryWriteCode.Success,
                await new SqlSourceRegistryStore(context).RegisterStagedAsync(
                    Tenancy.Models.TrustedFirmId.FromGuid(SyntheticFixtures.FirmId),
                    Guid.Parse("12121212-1212-4212-8212-121212121212"),
                    Guid.NewGuid(),
                    new string('A', 43),
                    SyntheticFixtures.CreatedAtUtc,
                    CancellationToken.None));
        }

        await using (var context = database.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(PriorMigrationId);
            var tables = await TablesAsync(context);
            Assert.DoesNotContain("SourceRegistrations", tables);
            Assert.Contains("IntakeCases", tables);
            Assert.Contains("ExtractionVersions", tables);
            Assert.Equal(1, await context.Set<IntakeCaseEntity>().CountAsync());
        }
    }

    [Fact]
    public void ExactlyOneMigrationAndDesignerFileExistForThisChangeAndEarlierOnesAreIntact()
    {
        Assert.Single(Directory.GetFiles(MigrationRoot, "*_AddSourceRegistry.cs"));
        Assert.Single(Directory.GetFiles(MigrationRoot, "*_AddSourceRegistry.Designer.cs"));
        Assert.True(File.Exists(Path.Combine(MigrationRoot, $"{MigrationId}.cs")));

        foreach (var earlier in new[] { "20260729021907_InitialPersistence", "20260730000000_AddClientIntake", PriorMigrationId })
        {
            var text = File.ReadAllText(Path.Combine(MigrationRoot, $"{earlier}.cs"));
            Assert.DoesNotContain("SourceRegistrations", text, StringComparison.Ordinal);
            Assert.Equal(64, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(MigrationRoot, $"{earlier}.cs")))).Length);
        }
    }

    [Fact]
    public void TheModelHasNoPendingChanges()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void TheGeneratedSqlServerSchemaCarriesTheScopedConstraints()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var script = context.Database.GenerateCreateScript();
        var table = script[script.IndexOf("CREATE TABLE [SourceRegistrations]", StringComparison.Ordinal)..];
        table = table[..table.IndexOf("GO", StringComparison.Ordinal)];

        Assert.Contains("[ContentKey] nvarchar(128) COLLATE Latin1_General_BIN2 NOT NULL", table, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT [PK_SourceRegistrations] PRIMARY KEY ([FirmId], [CaseId], [ResourceId])", table, StringComparison.Ordinal);
        Assert.Contains("CK_SourceRegistrations_State", table, StringComparison.Ordinal);
        Assert.Contains("CK_SourceRegistrations_Binding", table, StringComparison.Ordinal);
        Assert.Contains("CK_SourceRegistrations_BoundState", table, StringComparison.Ordinal);
        Assert.Contains("CK_SourceRegistrations_PurgeState", table, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX [IX_SourceRegistrations_FirmId_CaseId_ContentKey]", script, StringComparison.Ordinal);
    }

    private static async Task<string[]> TablesAsync(TaxAiDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names.ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
