using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tenancy.Models;
using Tenancy.Services;
using Tenancy.Tests.Support;

namespace Tenancy.Tests.Models;

public sealed class PersistedReadModelTests
{
    [Fact]
    public void EveryPersistedTypeDeclaresExactlyItsClosedAllowlist()
    {
        AssertProperties<PersistedReviewRecord>(
            "CaseId",
            "CreatedAtUtc",
            "FirmId",
            "PackageVersion",
            "ParticipantId",
            "RecordId",
            "RetentionUntilUtc",
            "Slips",
            "TaxYear",
            "UpdatedAtUtc");
        AssertProperties<PersistedSlip>(
            "BoxAmounts",
            "DocumentReference",
            "EmployerDisplayName",
            "FirmId",
            "LineEntries",
            "Ordinal",
            "RecordId",
            "SlipId",
            "SlipTypeCode",
            "TaxpayerDisplayName",
            "UnmappedBoxCodes");
        AssertProperties<PersistedBoxAmount>(
            "Amount",
            "BoxAmountId",
            "BoxCode",
            "FirmId",
            "Ordinal",
            "SlipId");
        AssertProperties<PersistedLineEntry>(
            "Amount",
            "FirmId",
            "LineCode",
            "LineEntryId",
            "Ordinal",
            "SlipId",
            "SourceBoxCodes");
        AssertProperties<PersistedSourceBoxCode>(
            "BoxCode",
            "FirmId",
            "LineEntryId",
            "Ordinal",
            "SourceBoxCodeId");
        AssertProperties<PersistedUnmappedBoxCode>(
            "BoxCode",
            "FirmId",
            "Ordinal",
            "SlipId",
            "UnmappedBoxCodeId");
    }

    [Fact]
    public void ConstructorsPopulateEveryDeclaredStateValueExactly()
    {
        var model = BuildModel();

        Assert.Equal(model.Identifiers[0], model.Record.RecordId);
        Assert.Equal(SyntheticPackages.FirmIdentifier, model.Record.FirmId);
        Assert.Equal(SyntheticPackages.CaseIdentifier, model.Record.CaseId);
        Assert.Equal(SyntheticPackages.ParticipantIdentifier, model.Record.ParticipantId);
        Assert.Equal(SyntheticPackages.SupportedTaxYear, model.Record.TaxYear);
        Assert.Equal(3, model.Record.PackageVersion);
        Assert.Equal(model.CreatedAtUtc, model.Record.CreatedAtUtc);
        Assert.Equal(model.CreatedAtUtc, model.Record.UpdatedAtUtc);
        Assert.Equal(model.RetentionUntilUtc, model.Record.RetentionUntilUtc);

        var slip = Assert.Single(model.Record.Slips);
        Assert.Equal(model.Identifiers[1], slip.SlipId);
        Assert.Equal(model.Record.RecordId, slip.RecordId);
        Assert.Equal(model.Record.FirmId, slip.FirmId);
        Assert.Equal(0, slip.Ordinal);
        Assert.Equal(SlipTypeCodes.T4, slip.SlipTypeCode, StringComparer.Ordinal);
        Assert.Equal(model.Identifiers[2], slip.DocumentReference);
        Assert.Equal("Test Taxpayer 001", slip.TaxpayerDisplayName, StringComparer.Ordinal);
        Assert.Equal("Example Employer A", slip.EmployerDisplayName, StringComparer.Ordinal);

        var boxAmount = Assert.Single(slip.BoxAmounts);
        Assert.Equal(model.Identifiers[3], boxAmount.BoxAmountId);
        Assert.Equal(slip.SlipId, boxAmount.SlipId);
        Assert.Equal(slip.FirmId, boxAmount.FirmId);
        Assert.Equal(0, boxAmount.Ordinal);
        Assert.Equal("box-marker", boxAmount.BoxCode, StringComparer.Ordinal);
        Assert.Equal(1.005m, boxAmount.Amount);

        var lineEntry = Assert.Single(slip.LineEntries);
        Assert.Equal(model.Identifiers[4], lineEntry.LineEntryId);
        Assert.Equal(slip.SlipId, lineEntry.SlipId);
        Assert.Equal(slip.FirmId, lineEntry.FirmId);
        Assert.Equal(0, lineEntry.Ordinal);
        Assert.Equal("line-marker", lineEntry.LineCode, StringComparer.Ordinal);
        Assert.Equal(-1.005m, lineEntry.Amount);

        var sourceCode = Assert.Single(lineEntry.SourceBoxCodes);
        Assert.Equal(model.Identifiers[5], sourceCode.SourceBoxCodeId);
        Assert.Equal(lineEntry.LineEntryId, sourceCode.LineEntryId);
        Assert.Equal(lineEntry.FirmId, sourceCode.FirmId);
        Assert.Equal(0, sourceCode.Ordinal);
        Assert.Equal("source-marker", sourceCode.BoxCode, StringComparer.Ordinal);

        var unmapped = Assert.Single(slip.UnmappedBoxCodes);
        Assert.Equal(model.Identifiers[6], unmapped.UnmappedBoxCodeId);
        Assert.Equal(slip.SlipId, unmapped.SlipId);
        Assert.Equal(slip.FirmId, unmapped.FirmId);
        Assert.Equal(0, unmapped.Ordinal);
        Assert.Equal("unmapped-marker", unmapped.BoxCode, StringComparer.Ordinal);
    }

    [Fact]
    public void ConstructorsSnapshotCallerCollections()
    {
        var identifiers = new SequenceIdentifierSource();
        var sourceInput = new List<PersistedSourceBoxCode>
        {
            new(
                identifiers.NewIdentifier(),
                SyntheticPackages.FirmIdentifier,
                identifiers.NewIdentifier(),
                0,
                "source-marker"),
        };
        var line = new PersistedLineEntry(
            identifiers.NewIdentifier(),
            SyntheticPackages.FirmIdentifier,
            identifiers.NewIdentifier(),
            0,
            "line-marker",
            1m,
            sourceInput);
        sourceInput.Clear();
        Assert.Single(line.SourceBoxCodes);

        var boxInput = new List<PersistedBoxAmount>();
        var lineInput = new List<PersistedLineEntry> { line };
        var unmappedInput = new List<PersistedUnmappedBoxCode>();
        var slip = new PersistedSlip(
            identifiers.NewIdentifier(),
            SyntheticPackages.FirmIdentifier,
            identifiers.NewIdentifier(),
            0,
            SlipTypeCodes.T4,
            identifiers.NewIdentifier(),
            SyntheticPackages.TaxpayerDisplayName,
            SyntheticPackages.EmployerDisplayName,
            boxInput,
            lineInput,
            unmappedInput);
        boxInput.Add(new PersistedBoxAmount(
            identifiers.NewIdentifier(),
            SyntheticPackages.FirmIdentifier,
            slip.SlipId,
            0,
            "late-box",
            2m));
        lineInput.Clear();
        unmappedInput.Add(new PersistedUnmappedBoxCode(
            identifiers.NewIdentifier(),
            SyntheticPackages.FirmIdentifier,
            slip.SlipId,
            0,
            "late-unmapped"));

        Assert.Empty(slip.BoxAmounts);
        Assert.Single(slip.LineEntries);
        Assert.Empty(slip.UnmappedBoxCodes);

        var slipInput = new List<PersistedSlip> { slip };
        var instant = UtcInstant();
        var record = new PersistedReviewRecord(
            identifiers.NewIdentifier(),
            SyntheticPackages.FirmIdentifier,
            SyntheticPackages.CaseIdentifier,
            SyntheticPackages.ParticipantIdentifier,
            SyntheticPackages.SupportedTaxYear,
            1,
            instant,
            instant,
            RetentionPolicy.ComputeRetentionUntilUtc(SyntheticPackages.SupportedTaxYear),
            slipInput);
        slipInput.Clear();

        Assert.Single(record.Slips);
    }

    [Fact]
    public void EveryExposedCollectionIsReadOnly()
    {
        var model = BuildModel();
        var slip = Assert.Single(model.Record.Slips);
        var line = Assert.Single(slip.LineEntries);

        Assert.Throws<NotSupportedException>(
            () => ((IList<PersistedSlip>)model.Record.Slips).Clear());
        Assert.Throws<NotSupportedException>(
            () => ((IList<PersistedBoxAmount>)slip.BoxAmounts).Clear());
        Assert.Throws<NotSupportedException>(
            () => ((IList<PersistedLineEntry>)slip.LineEntries).Clear());
        Assert.Throws<NotSupportedException>(
            () => ((IList<PersistedUnmappedBoxCode>)slip.UnmappedBoxCodes).Clear());
        Assert.Throws<NotSupportedException>(
            () => ((IList<PersistedSourceBoxCode>)line.SourceBoxCodes).Clear());
    }

    [Fact]
    public void EveryCallerControlledOrTaxpayerContentPropertyIsJsonIgnored()
    {
        var ignored = new (Type Type, string Property)[]
        {
            (typeof(PersistedReviewRecord), nameof(PersistedReviewRecord.CaseId)),
            (typeof(PersistedReviewRecord), nameof(PersistedReviewRecord.ParticipantId)),
            (typeof(PersistedReviewRecord), nameof(PersistedReviewRecord.Slips)),
            (typeof(PersistedSlip), nameof(PersistedSlip.TaxpayerDisplayName)),
            (typeof(PersistedSlip), nameof(PersistedSlip.EmployerDisplayName)),
            (typeof(PersistedSlip), nameof(PersistedSlip.BoxAmounts)),
            (typeof(PersistedSlip), nameof(PersistedSlip.LineEntries)),
            (typeof(PersistedSlip), nameof(PersistedSlip.UnmappedBoxCodes)),
            (typeof(PersistedBoxAmount), nameof(PersistedBoxAmount.BoxCode)),
            (typeof(PersistedBoxAmount), nameof(PersistedBoxAmount.Amount)),
            (typeof(PersistedLineEntry), nameof(PersistedLineEntry.LineCode)),
            (typeof(PersistedLineEntry), nameof(PersistedLineEntry.Amount)),
            (typeof(PersistedLineEntry), nameof(PersistedLineEntry.SourceBoxCodes)),
            (typeof(PersistedSourceBoxCode), nameof(PersistedSourceBoxCode.BoxCode)),
            (typeof(PersistedUnmappedBoxCode), nameof(PersistedUnmappedBoxCode.BoxCode)),
        };

        foreach (var (type, propertyName) in ignored)
        {
            var property = type.GetProperty(propertyName);
            Assert.NotNull(property);
            Assert.NotNull(property!.GetCustomAttribute<JsonIgnoreAttribute>());
        }
    }

    [Fact]
    public void DirectSerializationContainsNoCallerOrTaxpayerContent()
    {
        var model = BuildModel();
        var slip = Assert.Single(model.Record.Slips);
        object[] values =
        [
            model.Record,
            slip,
            slip.BoxAmounts[0],
            slip.LineEntries[0],
            slip.LineEntries[0].SourceBoxCodes[0],
            slip.UnmappedBoxCodes[0],
        ];
        var forbidden = new[]
        {
            SyntheticPackages.CaseIdentifier,
            SyntheticPackages.ParticipantIdentifier,
            SyntheticPackages.TaxpayerDisplayName,
            SyntheticPackages.EmployerDisplayName,
            "box-marker",
            "line-marker",
            "source-marker",
            "unmapped-marker",
            "1.005",
        };

        foreach (var value in values)
        {
            var json = JsonSerializer.Serialize(value, value.GetType());

            foreach (var marker in forbidden)
            {
                Assert.DoesNotContain(marker, json, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void EveryToStringEmitsOnlyItsFixedTypeIdentity()
    {
        var model = BuildModel();
        var slip = Assert.Single(model.Record.Slips);

        Assert.Equal(nameof(PersistedReviewRecord), model.Record.ToString());
        Assert.Equal(nameof(PersistedSlip), slip.ToString());
        Assert.Equal(nameof(PersistedBoxAmount), slip.BoxAmounts[0].ToString());
        Assert.Equal(nameof(PersistedLineEntry), slip.LineEntries[0].ToString());
        Assert.Equal(
            nameof(PersistedSourceBoxCode),
            slip.LineEntries[0].SourceBoxCodes[0].ToString());
        Assert.Equal(
            nameof(PersistedUnmappedBoxCode),
            slip.UnmappedBoxCodes[0].ToString());
    }

    [Fact]
    public void EveryPersistedTypeKeepsAPrivateMaterializationConstructor()
    {
        Type[] types =
        [
            typeof(PersistedReviewRecord),
            typeof(PersistedSlip),
            typeof(PersistedBoxAmount),
            typeof(PersistedLineEntry),
            typeof(PersistedSourceBoxCode),
            typeof(PersistedUnmappedBoxCode),
        ];

        foreach (var type in types)
        {
            var constructor = type.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);

            Assert.NotNull(constructor);
            Assert.True(constructor!.IsPrivate);
        }
    }

    private static void AssertProperties<T>(params string[] expected)
    {
        var actual = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    private static PersistedModel BuildModel()
    {
        var identifiers = new SequenceIdentifierSource();
        var recordId = identifiers.NewIdentifier();
        var slipId = identifiers.NewIdentifier();
        var documentReference = identifiers.NewIdentifier();
        var boxAmount = new PersistedBoxAmount(
            identifiers.NewIdentifier(),
            SyntheticPackages.FirmIdentifier,
            slipId,
            0,
            "box-marker",
            1.005m);
        var lineEntryId = identifiers.NewIdentifier();
        var sourceCode = new PersistedSourceBoxCode(
            identifiers.NewIdentifier(),
            SyntheticPackages.FirmIdentifier,
            lineEntryId,
            0,
            "source-marker");
        var lineEntry = new PersistedLineEntry(
            lineEntryId,
            SyntheticPackages.FirmIdentifier,
            slipId,
            0,
            "line-marker",
            -1.005m,
            [sourceCode]);
        var unmapped = new PersistedUnmappedBoxCode(
            identifiers.NewIdentifier(),
            SyntheticPackages.FirmIdentifier,
            slipId,
            0,
            "unmapped-marker");
        var slip = new PersistedSlip(
            slipId,
            SyntheticPackages.FirmIdentifier,
            recordId,
            0,
            SlipTypeCodes.T4,
            documentReference,
            SyntheticPackages.TaxpayerDisplayName,
            SyntheticPackages.EmployerDisplayName,
            [boxAmount],
            [lineEntry],
            [unmapped]);
        var createdAtUtc = UtcInstant();
        var retentionUntilUtc =
            RetentionPolicy.ComputeRetentionUntilUtc(SyntheticPackages.SupportedTaxYear);
        var record = new PersistedReviewRecord(
            recordId,
            SyntheticPackages.FirmIdentifier,
            SyntheticPackages.CaseIdentifier,
            SyntheticPackages.ParticipantIdentifier,
            SyntheticPackages.SupportedTaxYear,
            3,
            createdAtUtc,
            createdAtUtc,
            retentionUntilUtc,
            [slip]);

        return new PersistedModel(
            record,
            identifiers.Issued.ToArray(),
            createdAtUtc,
            retentionUntilUtc);
    }

    private static DateTimeOffset UtcInstant() =>
        new(
            SyntheticPackages.SupportedTaxYear,
            7,
            1,
            12,
            30,
            0,
            TimeSpan.Zero);

    private sealed record PersistedModel(
        PersistedReviewRecord Record,
        IReadOnlyList<Guid> Identifiers,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset RetentionUntilUtc);
}
