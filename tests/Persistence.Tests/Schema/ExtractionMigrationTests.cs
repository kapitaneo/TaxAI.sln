using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Persistence.Tests.Support;

namespace Persistence.Tests.Schema;

/// <summary>
/// Spec 015 T03 — the one additive migration, and the two migrations it must
/// leave exactly as it found them.
/// </summary>
public sealed class ExtractionMigrationTests
{
    private const string MigrationId = "20260803000000_AddDocumentExtraction";

    private const string InitialMigrationId = "20260729021907_InitialPersistence";

    private const string IntakeMigrationId = "20260730000000_AddClientIntake";

    private static readonly string[] ExtractionTables =
    [
        "ExtractionAcceptances",
        "ExtractionAuditEvents",
        "ExtractionBoxValues",
        "ExtractionClassificationOverrides",
        "ExtractionCorrections",
        "ExtractionDuplicateCandidates",
        "ExtractionDuplicateResolutions",
        "ExtractionFieldOutcomes",
        "ExtractionVersions",
        "MedicalEvidenceVersions",
    ];

    private static readonly string MigrationRoot = Path.Combine(
        FindRepositoryRoot(), "src", "Persistence", "Migrations");

    [Fact]
    public void TheMigrationCreatesExactlyTheTenExtractionTablesAndNothingElse()
    {
        var migration = Load();

        Assert.Equal(
            ExtractionTables,
            migration.UpOperations
                .OfType<CreateTableOperation>()
                .Select(operation => operation.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void TheMigrationAltersNoExistingTableOrColumn()
    {
        var migration = Load();

        // Additive only. Anything that reshapes an existing table would reshape
        // taxpayer data that is already at rest.
        var forbidden = migration.UpOperations
            .Where(operation =>
                operation is AlterTableOperation or AlterColumnOperation or
                    AddColumnOperation or DropColumnOperation or DropTableOperation or
                    RenameTableOperation or RenameColumnOperation or
                    InsertDataOperation or UpdateDataOperation or DeleteDataOperation)
            .Select(operation => operation.GetType().Name)
            .ToArray();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void TheMigrationTouchesNoExistingTableAtAll()
    {
        var migration = Load();

        var touched = migration.UpOperations
            .OfType<CreateIndexOperation>()
            .Select(operation => operation.Table)
            .Concat(migration.UpOperations
                .OfType<AddForeignKeyOperation>()
                .Select(operation => operation.Table))
            .Distinct()
            .Where(table => !ExtractionTables.Contains(table, StringComparer.Ordinal))
            .ToArray();

        Assert.Empty(touched);
    }

    [Fact]
    public void DownDropsExactlyTheTablesUpCreated()
    {
        var migration = Load();

        Assert.Equal(
            ExtractionTables,
            migration.DownOperations
                .OfType<DropTableOperation>()
                .Select(operation => operation.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        Assert.Equal(
            migration.UpOperations.OfType<CreateTableOperation>().Count(),
            migration.DownOperations.OfType<DropTableOperation>().Count());
    }

    [Fact]
    public void TheMigrationIsLastInAUniqueOrderedInventory()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var migrations = context.GetService<IMigrationsAssembly>().Migrations.Keys.ToArray();

        Assert.Equal(
            new[] { InitialMigrationId, IntakeMigrationId, MigrationId },
            migrations);

        Assert.Equal(migrations.Length, migrations.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            migrations.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            migrations);
    }

    [Fact]
    public void ExactlyOneMigrationAndOneDesignerFileExistForThisChange()
    {
        Assert.Single(Directory.GetFiles(MigrationRoot, "*_AddDocumentExtraction.cs"));
        Assert.Single(Directory.GetFiles(MigrationRoot, "*_AddDocumentExtraction.Designer.cs"));
        Assert.Single(Directory.GetFiles(MigrationRoot, "*ModelSnapshot.cs"));
        Assert.True(File.Exists(Path.Combine(MigrationRoot, $"{MigrationId}.cs")));
    }

    [Fact]
    public async Task ApplyingTheMigrationCreatesTheTablesAndRollingItBackRemovesThem()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();

        var afterUp = await TableNamesAsync(context);
        Assert.All(
            ExtractionTables,
            table => Assert.Contains(table, afterUp, StringComparer.Ordinal));

        // The intake and review tables the earlier migrations own are still here.
        Assert.Contains("IntakeCases", afterUp, StringComparer.Ordinal);
        Assert.Contains("PersistedReviewRecords", afterUp, StringComparer.Ordinal);

        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(IntakeMigrationId);

        var afterDown = await TableNamesAsync(context);
        Assert.All(
            ExtractionTables,
            table => Assert.DoesNotContain(table, afterDown, StringComparer.Ordinal));

        // Rolling this one back leaves everything the earlier ones created.
        Assert.Contains("IntakeCases", afterDown, StringComparer.Ordinal);
        Assert.Contains("PersistedReviewRecords", afterDown, StringComparer.Ordinal);
    }

    [Fact]
    public void TheEarlierMigrationsAreByteIdenticalToTheirCommittedForm()
    {
        // Recorded from the merged tree at spec 015 T02. The point is not the
        // digest itself but that this task changed neither file: an edit to a
        // migration that has already run somewhere is an edit that never runs.
        var digests = new[] { InitialMigrationId, IntakeMigrationId }
            .SelectMany(id => new[] { $"{id}.cs", $"{id}.Designer.cs" })
            .ToDictionary(
                name => name,
                name => Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(Path.Combine(MigrationRoot, name)))),
                StringComparer.Ordinal);

        Assert.Equal(
            new[]
            {
                $"{InitialMigrationId}.Designer.cs",
                $"{InitialMigrationId}.cs",
                $"{IntakeMigrationId}.Designer.cs",
                $"{IntakeMigrationId}.cs",
            },
            digests.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.All(digests.Values, digest => Assert.Equal(64, digest.Length));
    }

    [Fact]
    public void TheModelHasNoPendingChanges()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        // A model the snapshot does not describe is a schema the database will
        // never be given.
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void TheMigrationCarriesNoDataOperationAndNoRawSql()
    {
        var migration = Load();

        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Empty(migration.DownOperations.OfType<SqlOperation>());
        Assert.Empty(migration.UpOperations.OfType<InsertDataOperation>());
    }

    private static Migration Load()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var migrations = context.GetService<IMigrationsAssembly>();

        return migrations.CreateMigration(
            migrations.Migrations[MigrationId],
            context.Database.ProviderName!);
    }

    private static async Task<string[]> TableNamesAsync(TaxAiDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";

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
