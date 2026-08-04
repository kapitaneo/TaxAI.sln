using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Persistence.Tests.Support;

namespace Persistence.Tests.Schema;

public sealed class MigrationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string MigrationRoot =
        Path.Combine(RepositoryRoot, "src", "Persistence", "Migrations");

    private static readonly Regex MigrationIdAttribute = new(
        @"\[Migration\(""[^""]+""\)\]",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex NineConsecutiveDigits = new(
        @"\d{9}",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact] // T11 repository-owned migration
    public void ExactlyOneInitialAndOneAdditiveMigrationAndOneSnapshotExist()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var migrations = context.GetService<IMigrationsAssembly>();

        Assert.Equal(
            new[]
            {
                "20260729021907_InitialPersistence",
                "20260730000000_AddClientIntake",
                "20260803000000_AddDocumentExtraction",
            },
            migrations.Migrations.Keys);
        Assert.NotNull(migrations.ModelSnapshot);

        Assert.Single(Directory.GetFiles(MigrationRoot, "*_InitialPersistence.cs"));
        Assert.Single(Directory.GetFiles(MigrationRoot, "*_InitialPersistence.Designer.cs"));
        Assert.Single(Directory.GetFiles(MigrationRoot, "*_AddClientIntake.cs"));
        Assert.Single(Directory.GetFiles(MigrationRoot, "*_AddClientIntake.Designer.cs"));
        Assert.Single(Directory.GetFiles(MigrationRoot, "*_AddDocumentExtraction.cs"));
        Assert.Single(Directory.GetFiles(MigrationRoot, "*_AddDocumentExtraction.Designer.cs"));
        Assert.Single(Directory.GetFiles(MigrationRoot, "*ModelSnapshot.cs"));
    }

    [Fact] // rollback requirement
    public void DownDropsExactlyTheTablesUpCreatedIncludingSessions()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var migration = InitialMigration(context);

        var created = migration.UpOperations
            .OfType<CreateTableOperation>()
            .Select(operation => operation.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var dropped = migration.DownOperations
            .OfType<DropTableOperation>()
            .Select(operation => operation.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "AuthenticationSessions",
                "FirmMemberships",
                "Firms",
                "PersistedBoxAmounts",
                "PersistedLineEntries",
                "PersistedReviewRecords",
                "PersistedSlips",
                "PersistedSourceBoxCodes",
                "PersistedUnmappedBoxCodes",
                "PortalUsers",
            },
            created);
        Assert.Equal(created, dropped);
        Assert.Contains("AuthenticationSessions", dropped, StringComparer.Ordinal);
    }

    [Fact]
    public void AdditiveMigrationCreatesAndDropsOnlyTheIntakeTables()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var migration = MigrationById(context, "20260730000000_AddClientIntake");

        var created = migration.UpOperations
            .OfType<CreateTableOperation>()
            .Select(operation => operation.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var dropped = migration.DownOperations
            .OfType<DropTableOperation>()
            .Select(operation => operation.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "IntakeAnswerVersions",
                "IntakeAuditEvents",
                "IntakeCases",
                "IntakeClientSessions",
                "IntakeDocuments",
                "IntakeExpectedItems",
                "IntakeLinks",
                "IntakeReviewAssociations",
                "IntakeSubmissionAnswers",
                "IntakeSubmissionDocuments",
                "IntakeSubmissions",
                "IntakeUploadAttempts",
            },
            created);
        Assert.Equal(created, dropped);
    }

    [Fact] // AC-119, AC-120, AC-121, AC-122, AC-156
    public void GeneratedSqlServerSchemaCarriesEveryProviderSpecificFact()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var script = context.Database.GenerateCreateScript();

        Assert.Contains("[FirmId] uniqueidentifier NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("COLLATE Latin1_General_BIN2", script, StringComparison.Ordinal);
        Assert.Contains("decimal(19,4)", script, StringComparison.Ordinal);
        Assert.Contains(
            "[FirmId], [CaseId], [ParticipantId], [TaxYear], [PackageVersion]",
            script,
            StringComparison.Ordinal);
        Assert.Contains("Exclusive UTC retention boundary", script, StringComparison.Ordinal);
    }

    [Fact] // relational harness, migration Up and Down
    public async Task InitialMigrationAppliesAndRollsBackOnTheRelationalHarness()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();

        var tablesAfterUp = await UserTablesAsync(context);
        Assert.Contains("AuthenticationSessions", tablesAfterUp, StringComparer.Ordinal);
        Assert.Contains("PersistedReviewRecords", tablesAfterUp, StringComparer.Ordinal);

        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(Migration.InitialDatabase);

        var tablesAfterDown = await UserTablesAsync(context);
        Assert.Empty(tablesAfterDown);
    }

    [Fact] // migration scanner
    public void MigrationFilesContainNoNineDigitRunOutsideTheMigrationIdAttribute()
    {
        var offenders = Directory
            .EnumerateFiles(MigrationRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(path => new
            {
                Path = path,
                Sanitized = RemoveOnlyMigrationIdAttribute(File.ReadAllText(path)),
            })
            .Where(file => NineConsecutiveDigits.IsMatch(file.Sanitized))
            .Select(file => Path.GetFileName(file.Path))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact] // scanner guard
    public void MigrationScannerExcludesOnlyTheIdAndStillCatchesAPlantedRunInTheSameFile()
    {
        var migrationId = new string('2', 14) + "_SyntheticMigration";
        var plantedRun = new string('7', 9);
        var onlyAttribute = $"[Migration(\"{migrationId}\")]";
        var sameFileWithOffender =
            $"{onlyAttribute}{Environment.NewLine}var planted = \"{plantedRun}\";";

        Assert.DoesNotMatch(
            NineConsecutiveDigits,
            RemoveOnlyMigrationIdAttribute(onlyAttribute));
        Assert.Matches(
            NineConsecutiveDigits,
            RemoveOnlyMigrationIdAttribute(sameFileWithOffender));
    }

    [Fact] // AC-125, AC-187, AC-188
    public void InitialMigrationContainsNoSeedOrDataMutationOperation()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var migration = InitialMigration(context);

        var dataOperations = migration.UpOperations
            .Where(operation =>
                operation is InsertDataOperation or
                    UpdateDataOperation or
                    DeleteDataOperation or
                    SqlOperation)
            .ToArray();

        Assert.Empty(dataOperations);
    }

    private static Migration InitialMigration(TaxAiDbContext context)
    {
        return MigrationById(context, "20260729021907_InitialPersistence");
    }

    private static Migration MigrationById(TaxAiDbContext context, string migrationId)
    {
        var migrations = context.GetService<IMigrationsAssembly>();
        var migrationType = migrations.Migrations[migrationId];
        return migrations.CreateMigration(migrationType, context.Database.ProviderName!);
    }

    private static string RemoveOnlyMigrationIdAttribute(string source) =>
        MigrationIdAttribute.Replace(source, "[Migration(\"\")]");

    private static async Task<string[]> UserTablesAsync(TaxAiDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master " +
            "WHERE type = 'table' AND name NOT LIKE 'sqlite_%' " +
            "AND name <> '__EFMigrationsHistory' ORDER BY name;";

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables.ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find the repository root containing TaxAI.sln.");
    }
}
