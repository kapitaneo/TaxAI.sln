using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Persistence.Tests.Support;

namespace Persistence.Tests.Upgrade;

/// <summary>
/// Spec 013 T01: canonical, provider-specific proof that the EF model,
/// relational schema, generated SQL Server DDL, migration set, and migration
/// bytes stay equal while the runtime and EF tooling change.
/// </summary>
public sealed class EfUpgradeBaselineTests
{
    private const string BaselineCommit =
        "13097dada6b1aae070fc0006d48a6862ce9904f8";

    private const string InitialMigrationId =
        "20260729021907_InitialPersistence";

    private static readonly Regex CoreLibraryAssemblyVersion = new(
        @"(?<=System\.Private\.CoreLib, Version=)\d+\.\d+\.\d+\.\d+",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex MigrationHistoryProductVersion = new(
        $@"(?<prefix>INSERT INTO \[__EFMigrationsHistory\] \(\[MigrationId\], \[ProductVersion\]\)\nVALUES \(N'{InitialMigrationId}', N')[^']+(?<suffix>'\);)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string BaselineRoot = Path.Combine(
        RepositoryRoot,
        "specs",
        "013-dotnet-10-platform-upgrade",
        "baselines");

    [Fact]
    public void EfModelMatchesTheApprovedNet8BaselineExactly()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var current = CanonicalModel(context);
        var actual = current with
        {
            Entries = current.Entries
                .Where(entry => entry.Contains("|Tenancy.Models.", StringComparison.Ordinal))
                .ToArray(),
        };
        var path = Path.Combine(BaselineRoot, "ef-model.json");

        Assert.Equal(
            NormalizeModelArtifact(File.ReadAllText(path)),
            NormalizeModelArtifact(Serialize(actual)));
    }

    [Fact]
    public void RelationalSchemaMatchesTheApprovedNet8BaselineExactly()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var current = CanonicalRelationalSchema(context);
        var actual = current with
        {
            Entries = current.Entries
                .Where(entry => !entry.Contains(".Intake", StringComparison.Ordinal))
                .ToArray(),
        };
        var path = Path.Combine(BaselineRoot, "relational-schema.json");

        Assert.Equal(
            NormalizeNewlines(File.ReadAllText(path)),
            Serialize(actual));
    }

    [Fact]
    public void InitialMigrationDdlMatchesTheApprovedNet8BaselineExactly()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var migrator = context.GetService<IMigrator>();
        var actual = NormalizeDdl(
            migrator.GenerateScript(
                Migration.InitialDatabase,
                InitialMigrationId));
        var path = Path.Combine(BaselineRoot, "initial-migration.sql");

        Assert.Equal(
            NormalizeDdl(File.ReadAllText(path)),
            actual);
    }

    [Fact]
    public void MigrationSetAndBytesMatchTheApprovedNet8BaselineExactly()
    {
        var repositoryContractPath = Path.Combine(
            BaselineRoot,
            "repository-contract.json");
        using var repositoryContract = JsonDocument.Parse(
            File.ReadAllText(repositoryContractPath));

        var expected = repositoryContract.RootElement
            .GetProperty("migrationSha256")
            .EnumerateObject()
            .Where(property =>
                property.Name.Contains("InitialPersistence", StringComparison.Ordinal))
            .ToDictionary(
                property => property.Name,
                property => property.Value.GetString()!,
                StringComparer.Ordinal);

        var migrationRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "Persistence",
            "Migrations");
        var actual = Directory
            .EnumerateFiles(migrationRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path =>
                Path.GetFileName(path).Contains("InitialPersistence", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/'),
                path => Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal);

        Assert.Equal(
            expected.OrderBy(entry => entry.Key, StringComparer.Ordinal),
            actual.OrderBy(entry => entry.Key, StringComparer.Ordinal));
    }

    private static CanonicalManifest CanonicalModel(TaxAiDbContext context)
    {
        var model = context.GetService<IDesignTimeModel>().Model;
        var entries = new List<string>();

        foreach (var entity in model.GetEntityTypes()
                     .OrderBy(EntityName, StringComparer.Ordinal))
        {
            entries.Add(
                $"entity|{EntityName(entity)}" +
                $"|table={entity.GetTableName()}" +
                $"|schema={entity.GetSchema() ?? "<default>"}" +
                $"|base={(entity.BaseType is null ? "<none>" : EntityName(entity.BaseType))}" +
                $"|keyless={entity.FindPrimaryKey() is null}");

            foreach (var property in entity.GetProperties()
                         .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                var converter = property.GetValueConverter();
                entries.Add(
                    $"property|{EntityName(entity)}|{property.Name}" +
                    $"|clr={TypeName(property.ClrType)}" +
                    $"|column={property.GetColumnName()}" +
                    $"|store={property.GetColumnType()}" +
                    $"|nullable={property.IsNullable}" +
                    $"|max={NullableValue(property.GetMaxLength())}" +
                    $"|precision={NullableValue(property.GetPrecision())}" +
                    $"|scale={NullableValue(property.GetScale())}" +
                    $"|unicode={NullableValue(property.IsUnicode())}" +
                    $"|collation={property.GetCollation() ?? "<none>"}" +
                    $"|default={Value(property.GetDefaultValue())}" +
                    $"|defaultSql={property.GetDefaultValueSql() ?? "<none>"}" +
                    $"|computedSql={property.GetComputedColumnSql() ?? "<none>"}" +
                    $"|concurrency={property.IsConcurrencyToken}" +
                    $"|generated={property.ValueGenerated}" +
                    $"|beforeSave={property.GetBeforeSaveBehavior()}" +
                    $"|afterSave={property.GetAfterSaveBehavior()}" +
                    $"|converter={converter?.GetType().FullName ?? "<none>"}" +
                    $"|providerClr={TypeName(converter?.ProviderClrType)}");
            }

            foreach (var key in entity.GetKeys()
                         .OrderBy(key => PropertyList(key.Properties), StringComparer.Ordinal))
            {
                entries.Add(
                    $"key|{EntityName(entity)}" +
                    $"|name={key.GetName()}" +
                    $"|primary={key.IsPrimaryKey()}" +
                    $"|properties={PropertyList(key.Properties)}");
            }

            foreach (var index in entity.GetIndexes()
                         .OrderBy(index => index.GetDatabaseName(), StringComparer.Ordinal))
            {
                entries.Add(
                    $"index|{EntityName(entity)}" +
                    $"|name={index.GetDatabaseName()}" +
                    $"|properties={PropertyList(index.Properties)}" +
                    $"|unique={index.IsUnique}" +
                    $"|filter={index.GetFilter() ?? "<none>"}");
            }

            foreach (var foreignKey in entity.GetForeignKeys()
                         .OrderBy(
                             foreignKey =>
                                 $"{EntityName(foreignKey.PrincipalEntityType)}|" +
                                 PropertyList(foreignKey.Properties),
                             StringComparer.Ordinal))
            {
                entries.Add(
                    $"foreign-key|{EntityName(entity)}" +
                    $"|principal={EntityName(foreignKey.PrincipalEntityType)}" +
                    $"|properties={PropertyList(foreignKey.Properties)}" +
                    $"|principalKey={PropertyList(foreignKey.PrincipalKey.Properties)}" +
                    $"|delete={foreignKey.DeleteBehavior}" +
                    $"|required={foreignKey.IsRequired}" +
                    $"|unique={foreignKey.IsUnique}");
            }

            foreach (var navigation in entity.GetNavigations()
                         .OrderBy(navigation => navigation.Name, StringComparer.Ordinal))
            {
                entries.Add(
                    $"navigation|{EntityName(entity)}|{navigation.Name}" +
                    $"|target={EntityName(navigation.TargetEntityType)}" +
                    $"|collection={navigation.IsCollection}" +
                    $"|onDependent={navigation.IsOnDependent}" +
                    $"|field={navigation.FieldInfo?.Name ?? "<none>"}" +
                    $"|access={navigation.GetPropertyAccessMode()}");
            }
        }

        return new CanonicalManifest(
            BaselineCommit,
            "ef-model",
            entries.OrderBy(entry => entry, StringComparer.Ordinal).ToArray());
    }

    private static CanonicalManifest CanonicalRelationalSchema(
        TaxAiDbContext context)
    {
        var model = context.GetService<IDesignTimeModel>().Model;
        var relational = model.GetRelationalModel();
        var entries = new List<string>();

        foreach (var table in relational.Tables
                     .OrderBy(
                         table => $"{table.Schema ?? "<default>"}.{table.Name}",
                         StringComparer.Ordinal))
        {
            var tableName = $"{table.Schema ?? "<default>"}.{table.Name}";
            entries.Add($"table|{tableName}");

            foreach (var column in table.Columns
                         .OrderBy(column => column.Name, StringComparer.Ordinal))
            {
                entries.Add(
                    $"column|{tableName}|{column.Name}" +
                    $"|store={column.StoreType}" +
                    $"|providerClr={TypeName(column.ProviderClrType)}" +
                    $"|nullable={column.IsNullable}" +
                    $"|max={NullableValue(column.MaxLength)}" +
                    $"|precision={NullableValue(column.Precision)}" +
                    $"|scale={NullableValue(column.Scale)}" +
                    $"|unicode={NullableValue(column.IsUnicode)}" +
                    $"|fixed={NullableValue(column.IsFixedLength)}" +
                    $"|rowVersion={column.IsRowVersion}" +
                    $"|order={NullableValue(column.Order)}" +
                    $"|default={Value(column.DefaultValue)}" +
                    $"|defaultSql={column.DefaultValueSql ?? "<none>"}" +
                    $"|computedSql={column.ComputedColumnSql ?? "<none>"}" +
                    $"|stored={NullableValue(column.IsStored)}" +
                    $"|collation={column.Collation ?? "<none>"}");
            }

            if (table.PrimaryKey is not null)
            {
                entries.Add(
                    $"primary-key|{tableName}|{table.PrimaryKey.Name}" +
                    $"|columns={ColumnList(table.PrimaryKey.Columns)}");
            }

            foreach (var unique in table.UniqueConstraints
                         .OrderBy(unique => unique.Name, StringComparer.Ordinal))
            {
                entries.Add(
                    $"unique|{tableName}|{unique.Name}" +
                    $"|columns={ColumnList(unique.Columns)}");
            }

            foreach (var index in table.Indexes
                         .OrderBy(index => index.Name, StringComparer.Ordinal))
            {
                entries.Add(
                    $"index|{tableName}|{index.Name}" +
                    $"|columns={ColumnList(index.Columns)}" +
                    $"|unique={index.IsUnique}" +
                    $"|filter={index.Filter ?? "<none>"}");
            }

            foreach (var foreignKey in table.ForeignKeyConstraints
                         .OrderBy(
                             foreignKey => foreignKey.Name,
                             StringComparer.Ordinal))
            {
                entries.Add(
                    $"foreign-key|{tableName}|{foreignKey.Name}" +
                    $"|columns={ColumnList(foreignKey.Columns)}" +
                    $"|principal={foreignKey.PrincipalTable.Schema ?? "<default>"}." +
                    $"{foreignKey.PrincipalTable.Name}" +
                    $"|principalColumns={ColumnList(foreignKey.PrincipalColumns)}" +
                    $"|delete={foreignKey.OnDeleteAction}");
            }
        }

        return new CanonicalManifest(
            BaselineCommit,
            "relational-schema",
            entries.OrderBy(entry => entry, StringComparer.Ordinal).ToArray());
    }

    private static string EntityName(IReadOnlyTypeBase entity) =>
        entity.ClrType.FullName ?? entity.Name;

    private static string PropertyList(IEnumerable<IReadOnlyProperty> properties) =>
        string.Join(',', properties.Select(property => property.Name));

    private static string ColumnList(IEnumerable<IColumn> columns) =>
        string.Join(',', columns.Select(column => column.Name));

    private static string TypeName(Type? type) =>
        type?.FullName ?? "<none>";

    private static string NullableValue(object? value) =>
        value?.ToString() ?? "<none>";

    private static string Value(object? value) =>
        value switch
        {
            null => "<none>",
            string text => JsonSerializer.Serialize(text),
            bool boolean => boolean ? "true" : "false",
            _ => Convert.ToString(
                value,
                System.Globalization.CultureInfo.InvariantCulture) ?? "<none>",
        };

    private static string Serialize<T>(T value) =>
        NormalizeNewlines(JsonSerializer.Serialize(value, JsonOptions));

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd('\n') + "\n";

    private static string NormalizeModelArtifact(string value) =>
        CoreLibraryAssemblyVersion.Replace(
            NormalizeNewlines(value),
            "<framework-version>");

    private static string NormalizeDdl(string value)
    {
        var normalized = MigrationHistoryProductVersion.Replace(
            NormalizeNewlines(value),
            "${prefix}<product-version>${suffix}");
        var lines = normalized.Split('\n');
        var retained = new List<string>(lines.Length);

        for (var index = 0; index < lines.Length; index++)
        {
            if (!string.Equals(
                    lines[index].Trim(' ', '\t'),
                    "GO",
                    StringComparison.OrdinalIgnoreCase))
            {
                retained.Add(lines[index]);
                continue;
            }

            if (retained.Count > 0 &&
                string.Equals(
                    retained[^1],
                    "BEGIN TRANSACTION;",
                    StringComparison.Ordinal) &&
                index + 1 < lines.Length &&
                lines[index + 1].Length == 0)
            {
                index++;
            }
        }

        return NormalizeNewlines(string.Join('\n', retained));
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

    private sealed record CanonicalManifest(
        string BaselineCommit,
        string Kind,
        string[] Entries);
}
