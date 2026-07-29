using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using CaseReview.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Persistence.Repositories;
using Persistence.Tests.Repositories;
using Persistence.Tests.Support;
using TaxLogicEngine.Models;
using Tenancy.Models;

namespace Persistence.Tests.EndToEnd;

/// <summary>
/// Spec 011 AC-89 — follows a SIN-bearing upstream slip through the complete
/// projection and relational save, then reads every persisted string and
/// identifier column from the six affected tables.
/// </summary>
public sealed class PiiAtRestTests
{
    private static readonly Regex NineDigitRun = new(
        @"\d{9}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Type[] ReviewEntityTypes =
    [
        typeof(PersistedReviewRecord),
        typeof(PersistedSlip),
        typeof(PersistedBoxAmount),
        typeof(PersistedLineEntry),
        typeof(PersistedSourceBoxCode),
        typeof(PersistedUnmappedBoxCode),
    ];

    [Fact]
    public async Task AFullSavePersistsNoNineDigitRunFromTheUpstreamSin()
    {
        // Split deliberately: the test exercises a nine-digit value at runtime
        // without adding a realistic SIN-like literal to a tracked fixture.
        var upstreamSin = string.Concat("123", "456", "789");
        var package = ReviewRecordRepositoryTestData.Package(
            reviewedSlips:
            [
                SinBearingSlip(upstreamSin),
            ]);
        var projected = ReviewRecordRepositoryTestData.Project(
            package,
            identifiers: new PiiSafeIdentifierSource());

        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new SqlReviewRecordRepository(context);

        var saved = await repository.SaveAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            projected,
            CancellationToken.None);

        Assert.True(saved.IsSuccess);

        var found = await repository.FindAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            saved.Value!.RecordId,
            CancellationToken.None);

        Assert.NotNull(found);

        var inspected = await ReadPersistedStringsAndIdentifiersAsync(context);

        Assert.Equal(ReviewEntityTypes.Length, inspected.TablesWithRows);
        Assert.True(inspected.Values.Count > ReviewEntityTypes.Length);

        foreach (var value in inspected.Values)
        {
            Assert.DoesNotContain(upstreamSin, value, StringComparison.Ordinal);
            Assert.False(
                NineDigitRun.IsMatch(value),
                $"A persisted string or identifier contains a nine-digit run: {value}");
        }
    }

    private static ReviewedSlip SinBearingSlip(string upstreamSin)
    {
        var slip = new T4Slip
        {
            Year = ReviewRecordRepositoryTestData.SupportedTaxYear,
            EmployeeName = "Test Taxpayer Pii Omission",
            EmployerName = "Example Employer Pii Omission",
            Sin = new RecipientIdentifier(upstreamSin),
            DentalBenefitCode = "1",
        };

        return Construct<ReviewedSlip>(
            "synthetic_document",
            ReviewRecordRepositoryTestData.SupportedTaxYear,
            "synthetic_source",
            slip,
            new[]
            {
                ReviewRecordRepositoryTestData.Line(
                    "line_alpha",
                    2.005m,
                    ["source_alpha"]),
            },
            new[]
            {
                ReviewRecordRepositoryTestData.Box("box_alpha", 1.005m),
            },
            new[]
            {
                "unmapped_alpha",
            });
    }

    private static async Task<PersistedInspection> ReadPersistedStringsAndIdentifiersAsync(
        TaxAiDbContext context)
    {
        await context.Database.OpenConnectionAsync();

        var values = new List<string>();
        var tablesWithRows = 0;

        foreach (var clrType in ReviewEntityTypes)
        {
            var entity = context.Model.FindEntityType(clrType);
            Assert.NotNull(entity);

            var tableName = entity.GetTableName();
            Assert.False(string.IsNullOrWhiteSpace(tableName));

            var store = StoreObjectIdentifier.Table(tableName!, entity.GetSchema());
            var properties = entity
                .GetProperties()
                .Where(property =>
                    property.ClrType == typeof(string) ||
                    property.ClrType == typeof(Guid) ||
                    property.ClrType == typeof(Guid?))
                .Select(property => new
                {
                    Property = property,
                    Column = property.GetColumnName(store),
                })
                .ToArray();

            Assert.NotEmpty(properties);
            Assert.All(properties, property => Assert.False(string.IsNullOrWhiteSpace(property.Column)));

            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText =
                $"SELECT {string.Join(", ", properties.Select(property => Quote(property.Column!)))} "
                + $"FROM {Quote(tableName!)}";

            await using var reader = await command.ExecuteReaderAsync();
            var rowCount = 0;

            while (await reader.ReadAsync())
            {
                rowCount++;

                for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                {
                    if (!reader.IsDBNull(ordinal))
                    {
                        values.Add(RenderDatabaseValue(reader.GetValue(ordinal)));
                    }
                }
            }

            Assert.True(rowCount > 0, $"{tableName} was not populated by the full save.");
            tablesWithRows++;
        }

        return new PersistedInspection(tablesWithRows, values);
    }

    private static string RenderDatabaseValue(object value) =>
        value switch
        {
            Guid identifier => identifier.ToString("D", CultureInfo.InvariantCulture),
            byte[] bytes when bytes.Length == 16 =>
                new Guid(bytes).ToString("D", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

    private static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static T Construct<T>(params object?[] arguments)
    {
        var constructor = typeof(T)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == arguments.Length);

        return (T)constructor.Invoke(arguments);
    }

    private sealed record PersistedInspection(
        int TablesWithRows,
        IReadOnlyList<string> Values);

    private sealed class PiiSafeIdentifierSource : Tenancy.Contracts.IOpaqueIdentifierSource
    {
        private readonly Queue<Guid> _identifiers = new(
        [
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1"),
            Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2"),
            Guid.Parse("cccccccc-cccc-4ccc-8ccc-ccccccccccc3"),
            Guid.Parse("dddddddd-dddd-4ddd-8ddd-ddddddddddd4"),
            Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee5"),
            Guid.Parse("ffffffff-ffff-4fff-8fff-fffffffffff6"),
            Guid.Parse("abababab-abab-4aba-8aba-abababababa7"),
        ]);

        public Guid NewIdentifier() => _identifiers.Dequeue();
    }
}
