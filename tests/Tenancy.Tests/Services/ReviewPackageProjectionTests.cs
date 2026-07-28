using System.Text.RegularExpressions;
using Tenancy.Models;
using Tenancy.Services;
using Tenancy.Tests.Support;

namespace Tenancy.Tests.Services;

public sealed class ReviewPackageProjectionTests
{
    [Fact]
    public void Project_ValidPackage_CopiesTheClosedAllowlistInOrder()
    {
        var package = SyntheticPackages.Package();
        var identifiers = new SequenceIdentifierSource();
        var trustedFirm = TrustedFirmId.FromGuid(SyntheticPackages.FirmIdentifier);

        var result = ReviewPackageProjection.Project(
            trustedFirm,
            package,
            UtcInstant(),
            identifiers);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);

        var record = Assert.IsType<PersistedReviewRecord>(result.Value);
        Assert.Equal(identifiers.Issued[0], record.RecordId);
        Assert.Equal(trustedFirm.Value, record.FirmId);
        Assert.Equal(package.CaseId, record.CaseId, StringComparer.Ordinal);
        Assert.Equal(package.ParticipantId, record.ParticipantId, StringComparer.Ordinal);
        Assert.Equal(package.TaxYear, record.TaxYear);
        Assert.Equal(1, record.PackageVersion);
        Assert.Equal(UtcInstant(), record.CreatedAtUtc);
        Assert.Equal(UtcInstant(), record.UpdatedAtUtc);
        Assert.Equal(
            RetentionPolicy.ComputeRetentionUntilUtc(package.TaxYear),
            record.RetentionUntilUtc);

        var sourceSlip = Assert.Single(package.ReviewedSlips);
        var persistedSlip = Assert.Single(record.Slips);
        Assert.Equal(0, persistedSlip.Ordinal);
        Assert.Equal(identifiers.Issued[1], persistedSlip.SlipId);
        Assert.Equal(identifiers.Issued[2], persistedSlip.DocumentReference);
        Assert.Equal(record.RecordId, persistedSlip.RecordId);
        Assert.Equal(record.FirmId, persistedSlip.FirmId);
        Assert.Equal(SlipTypeCodes.T4, persistedSlip.SlipTypeCode, StringComparer.Ordinal);
        Assert.Equal(
            sourceSlip.Slip.EmployeeName,
            persistedSlip.TaxpayerDisplayName,
            StringComparer.Ordinal);
        Assert.Equal(
            sourceSlip.Slip.EmployerName,
            persistedSlip.EmployerDisplayName,
            StringComparer.Ordinal);

        Assert.Equal(
            sourceSlip.BoxAmounts.Select(item => item.BoxCode),
            persistedSlip.BoxAmounts.Select(item => item.BoxCode));
        Assert.Equal(
            sourceSlip.BoxAmounts.Select(item => item.Amount),
            persistedSlip.BoxAmounts.Select(item => item.Amount));
        Assert.Equal(
            sourceSlip.LineEntries.Select(item => item.LineNumber),
            persistedSlip.LineEntries.Select(item => item.LineCode));
        Assert.Equal(
            sourceSlip.LineEntries.Select(item => item.Amount),
            persistedSlip.LineEntries.Select(item => item.Amount));
        Assert.Equal(
            sourceSlip.UnmappedBoxCodes,
            persistedSlip.UnmappedBoxCodes.Select(item => item.BoxCode));

        for (var lineOrdinal = 0; lineOrdinal < sourceSlip.LineEntries.Count; lineOrdinal++)
        {
            Assert.Equal(
                sourceSlip.LineEntries[lineOrdinal].SourceBoxCodes,
                persistedSlip.LineEntries[lineOrdinal]
                    .SourceBoxCodes
                    .Select(item => item.BoxCode));
        }

        Assert.Equal(11, identifiers.CallCount);
    }

    [Fact]
    public void Project_SeveralSlips_PreservesEveryCollectionAndExplicitOrdinal()
    {
        var first = SyntheticPackages.Slip(
            lineEntries:
            [
                SyntheticPackages.Line("same-line", 1m, ["box-zeta"]),
                SyntheticPackages.Line("same-line", 2m, ["box-alpha"]),
            ],
            boxAmounts:
            [
                SyntheticPackages.Box("box-zeta", 3m),
                SyntheticPackages.Box("box-alpha", 4m),
            ],
            unmappedBoxCodes: ["unmapped-zeta", "unmapped-alpha"]);
        var second = SyntheticPackages.Slip(
            lineEntries: [SyntheticPackages.Line("line-second", 5m)],
            boxAmounts: [SyntheticPackages.Box("box-second", 6m)],
            unmappedBoxCodes: []);
        var package = SyntheticPackages.Package(reviewedSlips: [first, second]);

        var result = ReviewPackageProjection.Project(
            TrustedFirmId.FromGuid(SyntheticPackages.FirmIdentifier),
            package,
            UtcInstant(),
            new SequenceIdentifierSource());

        Assert.True(result.IsSuccess);
        var record = result.Value!;
        Assert.Equal(new[] { 0, 1 }, record.Slips.Select(item => item.Ordinal));

        Assert.Equal(
            new[] { "same-line", "same-line" },
            record.Slips[0].LineEntries.Select(item => item.LineCode));
        Assert.Equal(new[] { 0, 1 }, record.Slips[0].LineEntries.Select(item => item.Ordinal));
        Assert.Equal(
            new[] { "box-zeta", "box-alpha" },
            record.Slips[0].BoxAmounts.Select(item => item.BoxCode));
        Assert.Equal(new[] { 0, 1 }, record.Slips[0].BoxAmounts.Select(item => item.Ordinal));
        Assert.Equal(
            new[] { "unmapped-zeta", "unmapped-alpha" },
            record.Slips[0].UnmappedBoxCodes.Select(item => item.BoxCode));
        Assert.Equal(
            new[] { 0, 1 },
            record.Slips[0].UnmappedBoxCodes.Select(item => item.Ordinal));
        Assert.Equal(
            new[] { "box-zeta" },
            record.Slips[0].LineEntries[0].SourceBoxCodes.Select(item => item.BoxCode));
        Assert.Equal(
            new[] { "box-alpha" },
            record.Slips[0].LineEntries[1].SourceBoxCodes.Select(item => item.BoxCode));
    }

    [Fact]
    public void Project_EmptyPackage_SucceedsWithoutInventingASlip()
    {
        var identifiers = new SequenceIdentifierSource();
        var package = SyntheticPackages.Package(reviewedSlips: []);

        var result = ReviewPackageProjection.Project(
            TrustedFirmId.FromGuid(SyntheticPackages.FirmIdentifier),
            package,
            UtcInstant(),
            identifiers);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Slips);
        Assert.Equal(1, identifiers.CallCount);
    }

    [Fact]
    public void Project_EmptyNestedCollections_RemainEmptyAndDistinct()
    {
        var slip = SyntheticPackages.Slip(
            lineEntries: [],
            boxAmounts: [],
            unmappedBoxCodes: []);
        var package = SyntheticPackages.Package(reviewedSlips: [slip]);

        var result = ReviewPackageProjection.Project(
            TrustedFirmId.FromGuid(SyntheticPackages.FirmIdentifier),
            package,
            UtcInstant(),
            new SequenceIdentifierSource());

        Assert.True(result.IsSuccess);
        var persistedSlip = Assert.Single(result.Value!.Slips);
        Assert.Empty(persistedSlip.LineEntries);
        Assert.Empty(persistedSlip.BoxAmounts);
        Assert.Empty(persistedSlip.UnmappedBoxCodes);
    }

    [Fact]
    public void Project_AmountsAreCopiedBitForBitWithoutRescaling()
    {
        var sourceAmounts = new[] { 0m, 1.005m, -1.005m };
        var slip = SyntheticPackages.Slip(
            lineEntries:
            [
                SyntheticPackages.Line("line-zero", sourceAmounts[0]),
            ],
            boxAmounts:
            [
                SyntheticPackages.Box("box-positive", sourceAmounts[1]),
                SyntheticPackages.Box("box-negative", sourceAmounts[2]),
            ],
            unmappedBoxCodes: []);

        var result = ReviewPackageProjection.Project(
            TrustedFirmId.FromGuid(SyntheticPackages.FirmIdentifier),
            SyntheticPackages.Package(reviewedSlips: [slip]),
            UtcInstant(),
            new SequenceIdentifierSource());

        Assert.True(result.IsSuccess);
        var persisted = Assert.Single(result.Value!.Slips);
        Assert.Equal(
            decimal.GetBits(sourceAmounts[0]),
            decimal.GetBits(Assert.Single(persisted.LineEntries).Amount));
        Assert.Equal(
            decimal.GetBits(sourceAmounts[1]),
            decimal.GetBits(persisted.BoxAmounts[0].Amount));
        Assert.Equal(
            decimal.GetBits(sourceAmounts[2]),
            decimal.GetBits(persisted.BoxAmounts[1].Amount));
    }

    [Fact]
    public void Project_UsesGeneratedDocumentReferencesAndNeverConsumesCallerSourceMarkers()
    {
        const string documentMarker = "unsafe-document-marker";
        const string sourceMarker = "unsafe-source-marker";
        var slip = SyntheticPackages.Slip(
            documentMarker: documentMarker,
            sourceMarker: sourceMarker);
        var identifiers = new SequenceIdentifierSource();

        var result = ReviewPackageProjection.Project(
            TrustedFirmId.FromGuid(SyntheticPackages.FirmIdentifier),
            SyntheticPackages.Package(reviewedSlips: [slip]),
            UtcInstant(),
            identifiers);

        Assert.True(result.IsSuccess);
        var persisted = Assert.Single(result.Value!.Slips);
        Assert.Equal(identifiers.Issued[2], persisted.DocumentReference);

        var publicValues = PublicPropertyValues(result.Value!)
            .Concat(PublicPropertyValues(persisted))
            .OfType<string>()
            .ToArray();
        Assert.DoesNotContain(documentMarker, publicValues, StringComparer.Ordinal);
        Assert.DoesNotContain(sourceMarker, publicValues, StringComparer.Ordinal);
    }

    [Fact]
    public void Project_DoesNotMutateThePackageOrAnyUpstreamSequence()
    {
        var package = SyntheticPackages.Package();
        var beforeSlips = package.ReviewedSlips.ToArray();
        var beforeBoxes = package.ReviewedSlips[0].BoxAmounts.ToArray();
        var beforeLines = package.ReviewedSlips[0].LineEntries.ToArray();
        var beforeUnmapped = package.ReviewedSlips[0].UnmappedBoxCodes.ToArray();

        var result = ReviewPackageProjection.Project(
            TrustedFirmId.FromGuid(SyntheticPackages.FirmIdentifier),
            package,
            UtcInstant(),
            new SequenceIdentifierSource());

        Assert.True(result.IsSuccess);
        Assert.Equal(beforeSlips, package.ReviewedSlips);
        Assert.Equal(beforeBoxes, package.ReviewedSlips[0].BoxAmounts);
        Assert.Equal(beforeLines, package.ReviewedSlips[0].LineEntries);
        Assert.Equal(beforeUnmapped, package.ReviewedSlips[0].UnmappedBoxCodes);
    }

    [Fact]
    public void Implementation_IsAnExplicitProjectionWithoutUnsafeSourceReadsOrCollectionTransforms()
    {
        var executable = ExecutableProjectionSource();

        Assert.Contains("SupportedReviewTaxYears.IsSupported", executable, StringComparison.Ordinal);
        Assert.Contains("SlipTypeCodes.IsRecognized", executable, StringComparison.Ordinal);
        Assert.Contains("validatedTrustedFirm.Value", executable, StringComparison.Ordinal);
        Assert.DoesNotContain(".Sin", executable, StringComparison.Ordinal);
        Assert.DoesNotContain("RecipientIdentifier", executable, StringComparison.Ordinal);
        Assert.DoesNotContain(".DocumentId", executable, StringComparison.Ordinal);
        Assert.DoesNotContain(".SourceReference", executable, StringComparison.Ordinal);
        Assert.DoesNotContain(".SignOff", executable, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", executable, StringComparison.Ordinal);
        Assert.DoesNotContain(".OrderBy", executable, StringComparison.Ordinal);
        Assert.DoesNotContain(".GroupBy", executable, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where", executable, StringComparison.Ordinal);
        Assert.DoesNotContain(".Distinct", executable, StringComparison.Ordinal);
        Assert.DoesNotContain(".Aggregate", executable, StringComparison.Ordinal);
        Assert.DoesNotContain(".Sum", executable, StringComparison.Ordinal);
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

    private static IEnumerable<object?> PublicPropertyValues(object value) =>
        value.GetType().GetProperties().Select(property => property.GetValue(value));

    private static string ExecutableProjectionSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Tenancy",
                "Services",
                "ReviewPackageProjection.cs");

            if (File.Exists(candidate))
            {
                return Regex.Replace(
                    File.ReadAllText(candidate),
                    @"//.*?$|/\*.*?\*/",
                    string.Empty,
                    RegexOptions.Multiline | RegexOptions.Singleline,
                    TimeSpan.FromSeconds(1));
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the projection source file.");
    }
}
