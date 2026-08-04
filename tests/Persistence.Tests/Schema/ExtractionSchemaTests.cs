using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Persistence.Entities;
using Persistence.Tests.Repositories;
using Persistence.Tests.Support;
using Tenancy.Models;

namespace Persistence.Tests.Schema;

/// <summary>
/// Spec 015 T03 — what the extraction tables may and may not hold.
/// <para>
/// These are schema-shape assertions rather than behavioural ones: a column that
/// cannot exist is a leak that cannot happen, however the code above it is later
/// rewritten. The store tests prove the behaviour; these prove the shape it is
/// written against.
/// </para>
/// </summary>
public sealed class ExtractionSchemaTests
{
    private static readonly Type[] ExtractionEntityTypes =
        SqlExtractionStoreTests.ExtractionEntityTypes;

    [Fact]
    public void EveryExtractionEntityIsMappedToItsApprovedTable()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        var mapped = ExtractionEntityTypes
            .Select(type => context.Model.FindEntityType(type)!.GetTableName())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
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
            },
            mapped);
    }

    // ------------------------------------------------------------- tenancy

    [Fact]
    public void FirmAndCaseLeadEveryExtractionKeyAndIndex()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        foreach (var type in ExtractionEntityTypes)
        {
            var entity = context.Model.FindEntityType(type)!;

            Assert.All(
                entity.GetKeys(),
                key =>
                {
                    Assert.Equal(nameof(Firm.FirmId), key.Properties[0].Name);
                    Assert.Equal("CaseId", key.Properties[1].Name);
                });

            // No bootstrap index here: extraction is never reached by a digest
            // lookup, so a firm-omitting index would have no purpose and would
            // be an enumeration path.
            Assert.All(
                entity.GetIndexes(),
                index => Assert.Equal(nameof(Firm.FirmId), index.Properties[0].Name));
        }
    }

    [Fact]
    public void EveryExtractionRowIsOwnedByAStoredIntakeCase()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        foreach (var type in ExtractionEntityTypes)
        {
            var entity = context.Model.FindEntityType(type)!;
            var foreignKeys = entity.GetForeignKeys().ToArray();

            Assert.NotEmpty(foreignKeys);
            Assert.All(
                foreignKeys,
                key =>
                {
                    Assert.Equal(nameof(Firm.FirmId), key.Properties[0].Name);
                    Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior);
                });
        }
    }

    [Fact]
    public void EveryExtractionRowCarriesTheRetentionBoundary()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        foreach (var type in ExtractionEntityTypes)
        {
            var property = context.Model.FindEntityType(type)!
                .FindProperty("RetentionUntilUtc");

            Assert.NotNull(property);
            Assert.False(property!.IsNullable);
            Assert.Equal("datetimeoffset", property.GetColumnType());
        }
    }

    [Fact]
    public void NoExtractionTableDeclaresASecondRetentionAuthority()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        var offenders = ExtractionEntityTypes
            .SelectMany(type => context.Model.FindEntityType(type)!.GetProperties())
            .Select(property => property.Name)
            .Where(name =>
                name.Contains("Retention", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(name, "RetentionUntilUtc", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offenders);
    }

    // ------------------------------------------------------------ tax year

    [Fact]
    public void NoExtractionTableDeclaresATaxYearColumnOrConstraint()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        foreach (var type in ExtractionEntityTypes)
        {
            var entity = model.FindEntityType(type)!;

            // Year isolation belongs to the modules that own it. A year column
            // or a year check constraint here would be a second policy, and the
            // two would drift.
            Assert.DoesNotContain(
                entity.GetProperties(),
                property => property.Name.Contains("Year", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(entity.GetCheckConstraints());
        }
    }

    // ----------------------------------------------------------------- PII

    [Theory]
    [InlineData("Sin")]
    [InlineData("SocialInsurance")]
    [InlineData("Address")]
    [InlineData("SourceReference")]
    [InlineData("ContentKey")]
    [InlineData("Fingerprint")]
    [InlineData("FileName")]
    [InlineData("Prompt")]
    [InlineData("Payload")]
    [InlineData("Token")]
    [InlineData("Claim")]
    [InlineData("Presence")]
    [InlineData("Note")]
    [InlineData("Comment")]
    [InlineData("Message")]
    [InlineData("Diagnos")]
    [InlineData("Medication")]
    [InlineData("Insurance")]
    [InlineData("HealthNumber")]
    [InlineData("Card")]
    public void NoExtractionColumnCanHoldAForbiddenValue(string forbidden)
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        var propertyNames = ExtractionEntityTypes
            .SelectMany(type => context.Model.FindEntityType(type)!.GetProperties())
            .Select(property => property.Name)
            .ToArray();

        Assert.NotEmpty(propertyNames);
        Assert.DoesNotContain(
            propertyNames,
            name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheAuditTableCarriesOnlyStructuralColumns()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        Assert.Equal(
            new[]
            {
                "ActorKind",
                "ActorReference",
                "CaseId",
                "EventId",
                "FirmId",
                "Kind",
                "OccurredAtUtc",
                "RetentionUntilUtc",
                "Sequence",
            },
            context.Model.FindEntityType(typeof(ExtractionAuditEventEntity))!
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void NoExtractionColumnStoresBinaryContent()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        // Source bytes stay behind the write-only content store. A byte column
        // here would be a second place they could land.
        Assert.DoesNotContain(
            ExtractionEntityTypes
                .SelectMany(type => context.Model.FindEntityType(type)!.GetProperties()),
            property => property.ClrType == typeof(byte[]));
    }

    [Fact]
    public void TheMedicalTableCarriesExactlyTheApprovedFieldSet()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        Assert.Equal(
            new[]
            {
                "AcceptanceState",
                "CaseId",
                "Confidence",
                "CurrencyCodeAsPrinted",
                "CurrencyIsKnown",
                "DocumentId",
                "FirmId",
                "GrossAmountAsPrinted",
                "MedicalEvidenceVersionId",
                "ParticipantId",
                "PatientDisplayName",
                "PrecedingVersionId",
                "ProviderDisplayName",
                "ProviderLabel",
                "ReceiptReference",
                "RecordedAtUtc",
                "RetentionUntilUtc",
                "ServiceDateAsPrinted",
                "Version",
            },
            context.Model.FindEntityType(typeof(MedicalEvidenceVersionEntity))!
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void TheCurrencyIsTwoExplicitScalarColumnsRatherThanAConvertedValue()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var entity = context.Model.FindEntityType(typeof(MedicalEvidenceVersionEntity))!;

        var known = entity.FindProperty("CurrencyIsKnown")!;
        var code = entity.FindProperty("CurrencyCodeAsPrinted")!;

        Assert.Equal(typeof(bool), known.ClrType);
        Assert.False(known.IsNullable);
        Assert.Null(known.GetValueConverter());
        Assert.Equal(typeof(string), code.ClrType);
        Assert.True(code.IsNullable);
        Assert.Null(code.GetValueConverter());
    }

    [Fact]
    public void TheDateAndAmountAreStoredAsPrintedText()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var entity = context.Model.FindEntityType(typeof(MedicalEvidenceVersionEntity))!;

        // A parsed date or a decimal amount would be this feature deciding
        // something about a receipt, and it decides nothing.
        foreach (var name in new[] { "ServiceDateAsPrinted", "GrossAmountAsPrinted" })
        {
            Assert.Equal(typeof(string), entity.FindProperty(name)!.ClrType);
        }
    }

    // ------------------------------------------------------------- mapping

    [Fact]
    public void EveryExtractionEnumIsStoredAsAnInteger()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        var enumProperties = ExtractionEntityTypes
            .SelectMany(type => context.Model.FindEntityType(type)!.GetProperties())
            .Where(property =>
                (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType).IsEnum)
            .ToArray();

        Assert.NotEmpty(enumProperties);

        // Asserted on the column the provider will actually create. EF maps an
        // enum to its underlying integer without materialising a converter, so a
        // converter-shaped assertion would prove nothing about the column.
        Assert.All(
            enumProperties,
            property =>
            {
                Assert.Equal("int", property.GetColumnType());
                Assert.Equal(
                    typeof(int),
                    property.GetProviderClrType() ??
                    Enum.GetUnderlyingType(
                        Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType));
            });
    }

    [Fact]
    public void EveryExtractionEntityIsDeclaredByThisModuleAndNotByAnUpstreamOne()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        Assert.All(
            ExtractionEntityTypes,
            type => Assert.Equal(
                "Persistence",
                context.Model.FindEntityType(type)!.ClrType.Assembly.GetName().Name));
    }

    [Fact]
    public void ConfidenceDeclaresItsOwnPrecisionRatherThanTheMonetaryOne()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        foreach (var type in new[]
                 {
                     typeof(ExtractionVersionEntity),
                     typeof(MedicalEvidenceVersionEntity),
                 })
        {
            var confidence = context.Model.FindEntityType(type)!.FindProperty("Confidence")!;

            Assert.True(confidence.IsNullable);
            Assert.NotEqual(PersistedSchema.MonetaryPrecision, confidence.GetPrecision());
            Assert.NotEqual(PersistedSchema.MonetaryScale, confidence.GetScale());
        }
    }
}
