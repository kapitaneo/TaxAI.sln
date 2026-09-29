using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Persistence.Services;
using Persistence.Tests.Support;
using Tenancy.Models;

namespace Persistence.Tests.Upgrade;

/// <summary>
/// Focused disposition tests for the EF Core 10 and Microsoft.Data.Sqlite 10
/// changes that can affect this module.
/// </summary>
public sealed class EfCore10CompatibilityTests
{
    private const string ValidConnectionString =
        "Server=localhost;Database=TaxAiCompatibility;Encrypt=True;" +
        "TrustServerCertificate=False;Integrated Security=True";

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void InventoriedCollectionQueriesUseEf10MultipleScalarParameters()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var first = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var second = Guid.Parse("22222222-2222-4222-8222-222222222222");

        var expiredIds = new[] { first, second };
        var sessionSql = context.Set<AuthenticationSession>()
            .Where(session => expiredIds.Contains(session.SessionId))
            .ToQueryString();

        var requestedFirmIds = new[] { second, first };
        var firmSql = context.Set<Firm>()
            .Where(firm => requestedFirmIds.Contains(firm.FirmId))
            .OrderBy(firm => firm.FirmId)
            .ToQueryString();

        var eligibleIds = new[] { first, second };
        var recordSql = context.Set<PersistedReviewRecord>()
            .Where(record =>
                record.FirmId == first &&
                eligibleIds.Contains(record.RecordId))
            .ToQueryString();

        AssertMultipleScalarParameterSql(sessionSql, "expiredIds");
        AssertMultipleScalarParameterSql(firmSql, "requestedFirmIds");
        AssertMultipleScalarParameterSql(recordSql, "eligibleIds");

        AssertInventory(
            "Repositories/SqlAuthenticationSessionStore.cs",
            "expiredIds.Contains(session.SessionId)");
        AssertInventory(
            "Repositories/SqlIdentityDirectory.cs",
            "requestedFirmIds.Contains(firm.FirmId)");
        AssertInventory(
            "Services/SqlReviewRecordPurgeService.cs",
            "eligibleIds.Contains(record.RecordId)");
    }

    [Fact]
    public void SqlServerConfigurationKeepsTheExistingProviderAndSchemaDisposition()
    {
        var registration = ReadProductionSource("PersistenceRegistration.cs");
        var productionSource = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(RepositoryRoot, "src", "Persistence"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));

        Assert.Contains(
            "UseSqlServer(validatedConnectionString)",
            registration,
            StringComparison.Ordinal);
        Assert.DoesNotContain("UseAzureSql", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UseCompatibilityLevel", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UseParameterizedCollectionMode", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TranslateParameterizedCollections", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HasColumnType(\"json\")", productionSource, StringComparison.Ordinal);

        using var context = RelationalTestContext.CreateSqlServerContext();
        var columns = context.GetService<IDesignTimeModel>()
            .Model
            .GetRelationalModel()
            .Tables
            .SelectMany(table => table.Columns)
            .ToArray();

        Assert.DoesNotContain(
            columns,
            column => string.Equals(
                column.StoreType,
                "json",
                StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("TaxAI.Portal")]
    public void Ef10ApplicationNameInjectionCannotBypassTheGuard(
        string? explicitApplicationName)
    {
        var connectionString = explicitApplicationName is null
            ? ValidConnectionString
            : $"{ValidConnectionString};Application Name={explicitApplicationName}";

        Assert.Same(
            connectionString,
            SqlConnectionStringGuard.RequireValid(connectionString));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTaxAiPersistence(connectionString);

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        using var scope = provider.CreateScope();
        using var context =
            scope.ServiceProvider.GetRequiredService<TaxAiDbContext>();

        var effective = context.Database.GetDbConnection().ConnectionString;
        var effectiveBuilder = new SqlConnectionStringBuilder(effective);

        Assert.True(effectiveBuilder.Encrypt);
        Assert.False(effectiveBuilder.TrustServerCertificate);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);

        if (explicitApplicationName is null)
        {
            Assert.DoesNotContain(
                "Application Name",
                connectionString,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "Application Name",
                effective,
                StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(effectiveBuilder.ApplicationName));
        }
        else
        {
            Assert.Equal(explicitApplicationName, effectiveBuilder.ApplicationName);
            Assert.Equal(connectionString, effective);
        }
    }

    [Fact]
    public void ApplicationNameFailuresKeepDiagnosticsFixedAndSecretFree()
    {
        const string secretApplicationName = "SecretApplicationSentinel";
        var unsafeConnectionString =
            "Server=localhost;Database=TaxAiCompatibility;Encrypt=False;" +
            $"Application Name={secretApplicationName}";

        var exception = Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(unsafeConnectionString));

        Assert.Equal("connectionString", exception.ParamName);
        Assert.DoesNotContain(
            secretApplicationName,
            exception.ToString(),
            StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Sqlite10PreservesOffsetsUtcInstantsAndDeclaredAffinities()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var create = connection.CreateCommand())
        {
            create.CommandText =
                "CREATE TABLE TimeValues (" +
                "ExplicitText TEXT NOT NULL, " +
                "OffsetlessText TEXT NOT NULL, " +
                "RealInstant REAL NOT NULL);";
            create.ExecuteNonQuery();
        }

        var explicitOffset = new DateTimeOffset(
            2034,
            7,
            1,
            8,
            30,
            0,
            TimeSpan.FromHours(-4));

        using (var insert = connection.CreateCommand())
        {
            insert.CommandText =
                "INSERT INTO TimeValues " +
                "(ExplicitText, OffsetlessText, RealInstant) " +
                "VALUES ($explicitText, '2034-07-01 12:30:00', $realInstant);";
            insert.Parameters.AddWithValue("$explicitText", explicitOffset);
            var real = insert.Parameters.Add("$realInstant", SqliteType.Real);
            real.Value = explicitOffset;
            Assert.Equal(1, insert.ExecuteNonQuery());
        }

        using var read = connection.CreateCommand();
        read.CommandText =
            "SELECT ExplicitText, OffsetlessText, RealInstant, " +
            "typeof(ExplicitText), typeof(OffsetlessText), typeof(RealInstant) " +
            "FROM TimeValues;";
        using var reader = read.ExecuteReader();

        Assert.True(reader.Read());
        var explicitRoundTrip = reader.GetDateTimeOffset(0);
        var offsetlessRoundTrip = reader.GetDateTimeOffset(1);
        var realRoundTrip = reader.GetDateTimeOffset(2);

        Assert.Equal(explicitOffset, explicitRoundTrip);
        Assert.Equal(explicitOffset.UtcDateTime, explicitRoundTrip.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, offsetlessRoundTrip.Offset);
        Assert.Equal(
            new DateTimeOffset(2034, 7, 1, 12, 30, 0, TimeSpan.Zero),
            offsetlessRoundTrip);
        Assert.Equal(explicitOffset.UtcDateTime, realRoundTrip.UtcDateTime);
        Assert.Equal("text", reader.GetString(3));
        Assert.Equal("text", reader.GetString(4));
        Assert.Equal("real", reader.GetString(5));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Pre10SqliteTimeHandlingSwitchIsNotEnabledOrConfigured()
    {
        Assert.False(
            AppContext.TryGetSwitch(
                "Microsoft.Data.Sqlite.Pre10TimeZoneHandling",
                out var enabled) &&
            enabled);

        var productionFiles = Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src"),
                "*",
                SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            productionFiles,
            path => File.ReadAllText(path).Contains(
                "Pre10TimeZoneHandling",
                StringComparison.Ordinal));
    }

    [Fact]
    public void CurrentSqlServerModelHasNoPendingChanges()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact] // Spec 016 T04: the source registry keeps the existing SQL Server dispositions.
    public void TheSourceRegistryUsesTheExistingSqlServerColumnDispositions()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var entity = context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.FindEntityType(typeof(Persistence.Entities.SourceRegistrationEntity))!;

        Assert.Equal("SourceRegistrations", entity.GetTableName());
        Assert.Equal("uniqueidentifier", entity.FindProperty("FirmId")!.GetColumnType());
        Assert.Equal("uniqueidentifier", entity.FindProperty("ResourceId")!.GetColumnType());
        Assert.Equal("datetimeoffset", entity.FindProperty("StagedAtUtc")!.GetColumnType());
        Assert.Equal("datetimeoffset", entity.FindProperty("PurgeRecordedAtUtc")!.GetColumnType());
        Assert.Equal("Latin1_General_BIN2", entity.FindProperty("ContentKey")!.GetCollation());
        Assert.Equal("int", entity.FindProperty("State")!.GetColumnType());
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
    }

    private static void AssertMultipleScalarParameterSql(
        string sql,
        string parameterStem)
    {
        Assert.Contains($"@{parameterStem}1", sql, StringComparison.Ordinal);
        Assert.Contains($"@{parameterStem}2", sql, StringComparison.Ordinal);
        Assert.Contains(" IN (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("OPENJSON", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertInventory(string relativePath, string expression)
    {
        var source = ReadProductionSource(relativePath);

        Assert.Equal(1, CountOccurrences(source, expression));
        Assert.DoesNotContain("EF.Constant(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EF.Parameter(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EF.MultipleParameters(", source, StringComparison.Ordinal);
    }

    private static string ReadProductionSource(string relativePath) =>
        File.ReadAllText(
            Path.Combine(
                RepositoryRoot,
                "src",
                "Persistence",
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static int CountOccurrences(string value, string expected)
    {
        var count = 0;
        var offset = 0;

        while ((offset = value.IndexOf(
                   expected,
                   offset,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += expected.Length;
        }

        return count;
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
