using TaxLogicEngine.Models;
using Tenancy.Models;
using Tenancy.Services;
using Tenancy.Tests.Support;

namespace Tenancy.Tests.Services;

public sealed class ReviewPackageProjectionRejectionTests
{
    [Fact]
    public void Project_NullPackageAndIdentifierSource_FailsClosedWithoutThrowing()
    {
        ParseResult<PersistedReviewRecord>? result = null;

        var exception = Record.Exception(
            () => result = ReviewPackageProjection.Project(
                TrustedFirm(),
                null!,
                UtcInstant(),
                null!));

        Assert.Null(exception);
        Assert.NotNull(result);
        AssertFailure(
            result!,
            (TenancyFields.Package, TenancyErrors.PackageRequired),
            (TenancyFields.IdentifierSource, TenancyErrors.IdentifierSourceRequired));
    }

    [Fact]
    public void Project_NullTrustedFirm_FailsClosedBeforeGeneratingIdentifiers()
    {
        var identifiers = new SequenceIdentifierSource();

        var result = ReviewPackageProjection.Project(
            null!,
            SyntheticPackages.Package(),
            UtcInstant(),
            identifiers);

        AssertSingleFailure(
            result,
            TenancyFields.TrustedFirmId,
            TenancyErrors.TrustedFirmRequired);
        Assert.Equal(0, identifiers.CallCount);
    }

    [Fact]
    public void Project_NullIdentifierSource_FailsClosedWithoutThrowing()
    {
        ParseResult<PersistedReviewRecord>? result = null;

        var exception = Record.Exception(
            () => result = ReviewPackageProjection.Project(
                TrustedFirm(),
                SyntheticPackages.Package(),
                UtcInstant(),
                null!));

        Assert.Null(exception);
        AssertSingleFailure(
            result!,
            TenancyFields.IdentifierSource,
            TenancyErrors.IdentifierSourceRequired);
    }

    [Fact]
    public void Project_NonZeroOffset_FailsClosedWithoutNormalizing()
    {
        var identifiers = new SequenceIdentifierSource();
        var nonUtc = new DateTimeOffset(
            SyntheticPackages.SupportedTaxYear,
            7,
            1,
            12,
            30,
            0,
            TimeSpan.FromHours(-4));

        var result = ReviewPackageProjection.Project(
            TrustedFirm(),
            SyntheticPackages.Package(),
            nonUtc,
            identifiers);

        AssertSingleFailure(
            result,
            TenancyFields.EvaluationInstant,
            TenancyErrors.InstantNotUtc);
        Assert.Equal(0, identifiers.CallCount);
    }

    [Fact]
    public void Project_NonCanonicalPackageFirm_FailsClosedWithoutEchoingIt()
    {
        var unsafeFirm = SyntheticPackages.FirmIdentifier.ToString("D").ToUpperInvariant();
        var identifiers = new SequenceIdentifierSource();

        var result = ReviewPackageProjection.Project(
            TrustedFirm(),
            SyntheticPackages.Package(firmText: unsafeFirm),
            UtcInstant(),
            identifiers);

        var error = AssertSingleFailure(
            result,
            TenancyFields.FirmId,
            TenancyErrors.FirmIdNotCanonical);
        Assert.DoesNotContain(unsafeFirm, error.Field, StringComparison.Ordinal);
        Assert.DoesNotContain(unsafeFirm, error.Message, StringComparison.Ordinal);
        Assert.Equal(0, identifiers.CallCount);
    }

    [Fact]
    public void Project_PackageFirmMismatch_FailsClosedAndNeverStoresThePackageFirm()
    {
        var identifiers = new SequenceIdentifierSource();

        var result = ReviewPackageProjection.Project(
            TrustedFirm(),
            SyntheticPackages.Package(firmIdentifier: SyntheticPackages.OtherFirmIdentifier),
            UtcInstant(),
            identifiers);

        AssertSingleFailure(
            result,
            TenancyFields.FirmId,
            TenancyErrors.FirmMismatch);
        Assert.Equal(0, identifiers.CallCount);
    }

    [Theory]
    [InlineData(2024)]
    [InlineData(2026)]
    public void Project_UnsupportedTaxYear_FailsIdenticallyWithoutFallback(int taxYear)
    {
        var identifiers = new SequenceIdentifierSource();
        var slip = SyntheticPackages.Slip(taxYear: taxYear);

        var result = ReviewPackageProjection.Project(
            TrustedFirm(),
            SyntheticPackages.Package(taxYear: taxYear, reviewedSlips: [slip]),
            UtcInstant(),
            identifiers);

        AssertSingleFailure(
            result,
            TenancyFields.TaxYear,
            TenancyErrors.TaxYearNotSupported);
        Assert.Equal(0, identifiers.CallCount);
    }

    [Fact]
    public void Project_SlipTaxYearDivergence_FailsClosed()
    {
        var identifiers = new SequenceIdentifierSource();
        var divergentSlip = SyntheticPackages.Slip(
            taxYear: SyntheticPackages.SupportedTaxYear - 1);

        var result = ReviewPackageProjection.Project(
            TrustedFirm(),
            SyntheticPackages.Package(reviewedSlips: [divergentSlip]),
            UtcInstant(),
            identifiers);

        AssertSingleFailure(
            result,
            TenancyFields.SlipTaxYear,
            TenancyErrors.SlipTaxYearDiverges);
        Assert.Equal(0, identifiers.CallCount);
    }

    [Theory]
    [InlineData("", "The identifier is empty.")]
    [InlineData("case value", "The identifier contains a character outside the permitted set.")]
    public void Project_UnsafeCaseIdentifier_FailsClosed(
        string caseIdentifier,
        string expectedMessage)
    {
        var result = ReviewPackageProjection.Project(
            TrustedFirm(),
            SyntheticPackages.Package(caseIdentifier: caseIdentifier),
            UtcInstant(),
            new SequenceIdentifierSource());

        AssertSingleFailure(result, TenancyFields.CaseId, expectedMessage);
    }

    [Fact]
    public void Project_OverLengthCaseIdentifier_FailsClosedWithoutTruncating()
    {
        var unsafeValue = new string('C', PersistedSchema.CaseIdMaxLength + 1);

        var result = ReviewPackageProjection.Project(
            TrustedFirm(),
            SyntheticPackages.Package(caseIdentifier: unsafeValue),
            UtcInstant(),
            new SequenceIdentifierSource());

        var error = AssertSingleFailure(
            result,
            TenancyFields.CaseId,
            TenancyErrors.TokenTooLong);
        Assert.DoesNotContain(unsafeValue, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("participant value")]
    public void Project_UnsafeParticipantIdentifier_FailsClosed(string participantIdentifier)
    {
        var result = ReviewPackageProjection.Project(
            TrustedFirm(),
            SyntheticPackages.Package(participantIdentifier: participantIdentifier),
            UtcInstant(),
            new SequenceIdentifierSource());

        Assert.Equal(TenancyFields.ParticipantId, Assert.Single(result.Errors).Field);
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Project_OverLengthTaxpayerDisplayName_FailsWithoutTruncating()
    {
        var unsafeValue = new string('T', PersistedSchema.DisplayNameMaxLength + 1);
        var slip = SyntheticPackages.Slip(taxpayerDisplayName: unsafeValue);

        var result = ReviewPackageProjection.Project(
            TrustedFirm(),
            SyntheticPackages.Package(reviewedSlips: [slip]),
            UtcInstant(),
            new SequenceIdentifierSource());

        var error = AssertSingleFailure(
            result,
            TenancyFields.TaxpayerDisplayName,
            TenancyErrors.TextTooLong);
        Assert.DoesNotContain(unsafeValue, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_ConsecutiveDigitRunInEmployerDisplayName_FailsWithoutLeakingIt()
    {
        var unsafeValue = $"Example-{new string('7', 9)}";
        var slip = SyntheticPackages.Slip(employerDisplayName: unsafeValue);

        var result = ReviewPackageProjection.Project(
            TrustedFirm(),
            SyntheticPackages.Package(reviewedSlips: [slip]),
            UtcInstant(),
            new SequenceIdentifierSource());

        var error = AssertSingleFailure(
            result,
            TenancyFields.EmployerDisplayName,
            TenancyErrors.TextContainsDigitRun);
        Assert.DoesNotContain(unsafeValue, error.Field, StringComparison.Ordinal);
        Assert.DoesNotContain(unsafeValue, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_AmountThatRequiresAlteration_FailsForBoxAndLineValues()
    {
        var slip = SyntheticPackages.Slip(
            lineEntries: [SyntheticPackages.Line("line-alpha", 0.00001m)],
            boxAmounts: [SyntheticPackages.Box("box-alpha", 1.23000m)],
            unmappedBoxCodes: []);

        var result = ReviewPackageProjection.Project(
            TrustedFirm(),
            SyntheticPackages.Package(reviewedSlips: [slip]),
            UtcInstant(),
            new SequenceIdentifierSource());

        AssertFailure(
            result,
            (TenancyFields.Amount, TenancyErrors.AmountNotExactlyStorable),
            (TenancyFields.Amount, TenancyErrors.AmountNotExactlyStorable));
    }

    [Theory]
    [InlineData("box value")]
    [InlineData("box.value")]
    public void Project_UnsafeBoxCode_FailsClosed(string unsafeCode)
    {
        var slip = SyntheticPackages.Slip(
            boxAmounts: [SyntheticPackages.Box(unsafeCode, 1m)],
            lineEntries: [],
            unmappedBoxCodes: []);

        var result = ReviewPackageProjection.Project(
            TrustedFirm(),
            SyntheticPackages.Package(reviewedSlips: [slip]),
            UtcInstant(),
            new SequenceIdentifierSource());

        AssertSingleFailure(
            result,
            TenancyFields.BoxCode,
            TenancyErrors.TokenCharacterNotPermitted);
    }

    [Fact]
    public void Project_UnsafeLineAndSourceCodes_FailClosedInPackageOrder()
    {
        var slip = SyntheticPackages.Slip(
            lineEntries:
            [
                SyntheticPackages.Line("line value", 1m, ["source value"]),
            ],
            boxAmounts: [],
            unmappedBoxCodes: []);

        var result = ReviewPackageProjection.Project(
            TrustedFirm(),
            SyntheticPackages.Package(reviewedSlips: [slip]),
            UtcInstant(),
            new SequenceIdentifierSource());

        AssertFailure(
            result,
            (TenancyFields.LineCode, TenancyErrors.TokenCharacterNotPermitted),
            (TenancyFields.BoxCode, TenancyErrors.TokenCharacterNotPermitted));
    }

    [Fact]
    public void Project_UnsafeUnmappedCode_FailsClosed()
    {
        var slip = SyntheticPackages.Slip(
            lineEntries: [],
            boxAmounts: [],
            unmappedBoxCodes: ["unmapped value"]);

        var result = ReviewPackageProjection.Project(
            TrustedFirm(),
            SyntheticPackages.Package(reviewedSlips: [slip]),
            UtcInstant(),
            new SequenceIdentifierSource());

        AssertSingleFailure(
            result,
            TenancyFields.BoxCode,
            TenancyErrors.TokenCharacterNotPermitted);
    }

    [Fact]
    public void Project_IndependentFailuresAccumulateInTheDeclaredStageOrder()
    {
        var unsafeTaxpayerText = $"Taxpayer-{new string('6', 9)}";
        var slip = SyntheticPackages.Slip(
            taxYear: SyntheticPackages.SupportedTaxYear - 1,
            taxpayerDisplayName: unsafeTaxpayerText,
            employerDisplayName: new string('E', PersistedSchema.DisplayNameMaxLength + 1),
            lineEntries:
            [
                SyntheticPackages.Line("line value", 0.00001m, ["source value"]),
            ],
            boxAmounts:
            [
                SyntheticPackages.Box("box value", 1.23000m),
            ],
            unmappedBoxCodes: ["unmapped value"]);
        var identifiers = new SequenceIdentifierSource();
        var nonUtc = UtcInstant().ToOffset(TimeSpan.FromHours(2));

        var result = ReviewPackageProjection.Project(
            TrustedFirm(),
            SyntheticPackages.Package(
                firmText: "not-canonical",
                caseIdentifier: "",
                participantIdentifier: "participant value",
                reviewedSlips: [slip]),
            nonUtc,
            identifiers);

        AssertFailure(
            result,
            (TenancyFields.EvaluationInstant, TenancyErrors.InstantNotUtc),
            (TenancyFields.FirmId, TenancyErrors.FirmIdNotCanonical),
            (TenancyFields.SlipTaxYear, TenancyErrors.SlipTaxYearDiverges),
            (TenancyFields.CaseId, TenancyErrors.TokenEmpty),
            (TenancyFields.ParticipantId, TenancyErrors.TokenCharacterNotPermitted),
            (TenancyFields.TaxpayerDisplayName, TenancyErrors.TextContainsDigitRun),
            (TenancyFields.EmployerDisplayName, TenancyErrors.TextTooLong),
            (TenancyFields.BoxCode, TenancyErrors.TokenCharacterNotPermitted),
            (TenancyFields.Amount, TenancyErrors.AmountNotExactlyStorable),
            (TenancyFields.LineCode, TenancyErrors.TokenCharacterNotPermitted),
            (TenancyFields.Amount, TenancyErrors.AmountNotExactlyStorable),
            (TenancyFields.BoxCode, TenancyErrors.TokenCharacterNotPermitted),
            (TenancyFields.BoxCode, TenancyErrors.TokenCharacterNotPermitted));
        Assert.Equal(0, identifiers.CallCount);

        var renderedErrors = string.Join(
            "|",
            result.Errors.Select(error => $"{error.Field}:{error.Message}"));
        Assert.DoesNotContain(unsafeTaxpayerText, renderedErrors, StringComparison.Ordinal);
        Assert.DoesNotContain("not-canonical", renderedErrors, StringComparison.Ordinal);
        Assert.DoesNotContain("participant value", renderedErrors, StringComparison.Ordinal);
    }

    private static TrustedFirmId TrustedFirm() =>
        TrustedFirmId.FromGuid(SyntheticPackages.FirmIdentifier);

    private static DateTimeOffset UtcInstant() =>
        new(
            SyntheticPackages.SupportedTaxYear,
            7,
            1,
            12,
            30,
            0,
            TimeSpan.Zero);

    private static ValidationError AssertSingleFailure(
        ParseResult<PersistedReviewRecord> result,
        string field,
        string message)
    {
        AssertFailure(result, (field, message));
        return Assert.Single(result.Errors);
    }

    private static void AssertFailure(
        ParseResult<PersistedReviewRecord> result,
        params (string Field, string Message)[] expected)
    {
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(
            expected,
            result.Errors.Select(error => (error.Field, error.Message)).ToArray());
    }
}
