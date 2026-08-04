using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentExtraction.Models;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using TaxLogicEngine.Models;

namespace ExtractionWorkflow.Tests.Models;

/// <summary>
/// Spec 015 T02 — the shapes this module hands to everything downstream.
/// <para>
/// Two kinds of assertion live here. The first is structural: every public
/// instance type is a sealed class with a hand-written <c>ToString()</c>, so no
/// compiler-generated record printer can put a taxpayer value into a log. The
/// second is behavioural: the invariants that make a rule unrepresentable
/// rather than merely enforced — a box value that cannot carry the SIN box, a
/// correction vocabulary with no SIN member, an override that cannot name an
/// unsupported path.
/// </para>
/// </summary>
public sealed class ResultShapeTests
{
    private static readonly Assembly ModuleAssembly = typeof(ExtractionVersion).Assembly;

    private static readonly DateTimeOffset Instant =
        new(2026, 8, 3, 14, 30, 0, TimeSpan.Zero);

    // ------------------------------------------------- shape of every type

    [Fact]
    public void EveryPublicInstanceTypeIsASealedClassWithAHandWrittenToString()
    {
        var offenders = new List<string>();

        foreach (var type in PublicInstanceTypes())
        {
            if (!type.IsSealed)
            {
                offenders.Add($"{type.Name} is not sealed");
            }

            // A record's generated ToString() prints every property value, which
            // is precisely the leak the omission contract forbids.
            if (type.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null ||
                type.GetProperty("EqualityContract", BindingFlags.Instance | BindingFlags.NonPublic) is not null)
            {
                offenders.Add($"{type.Name} is a record");
            }

            var toString = type.GetMethod(
                nameof(ToString), BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes);

            if (toString?.DeclaringType != type)
            {
                offenders.Add($"{type.Name} does not declare ToString()");
            }
        }

        Assert.True(offenders.Count == 0, string.Join("; ", offenders));
    }

    [Fact]
    public void ThePublicInstanceTypeInventoryIsTheOneThisTaskShips()
    {
        // Without this, the sweep above would pass for free if the walk broke.
        Assert.Equal(
            new[]
            {
                nameof(ClassificationOutcome),
                nameof(CurrencyDeclaration),
                nameof(ExtractionBoxValue),
                nameof(ExtractionSourceLease),
                nameof(ExtractionVersion),
                nameof(FieldValidationOutcome),
                nameof(MedicalEvidenceVersion),
                nameof(ProviderBoxValue),
                nameof(ProviderClassificationResult),
                nameof(ProviderMedicalResult),
                nameof(ProviderT4Result),
            },
            PublicInstanceTypes().Select(type => type.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData(typeof(ExtractionVersion), "ExtractionVersionId", "DocumentId", "PrecedingVersionId", "EmployerDisplayName", "EmployeeDisplayName", "EmployerAddress", "EmployeeAddress")]
    [InlineData(typeof(MedicalEvidenceVersion), "MedicalEvidenceVersionId", "DocumentId", "PrecedingVersionId", "ServiceDateAsPrinted", "GrossAmountAsPrinted", "ProviderDisplayName", "PatientDisplayName", "ReceiptReference")]
    [InlineData(typeof(ExtractionBoxValue), "Code", "Value")]
    [InlineData(typeof(ProviderBoxValue), "Code", "Value")]
    [InlineData(typeof(ProviderT4Result), "EmployerName", "EmployerAddress", "EmployeeName", "EmployeeAddress", "Boxes")]
    [InlineData(typeof(ProviderMedicalResult), "ServiceDateAsPrinted", "GrossAmountAsPrinted", "CurrencyCodeAsPrinted", "ProviderDisplayName", "PatientDisplayName", "ReceiptReference")]
    [InlineData(typeof(ClassificationOutcome), "OverrideActorReference")]
    [InlineData(typeof(CurrencyDeclaration), "CodeAsPrinted")]
    [InlineData(typeof(FieldValidationOutcome), "Field")]
    public void EveryIdentifierAndTaxpayerTextPropertyIsOmittedFromSerialization(
        Type type,
        params string[] names)
    {
        foreach (var name in names)
        {
            var property = type.GetProperty(name);
            Assert.NotNull(property);
            Assert.NotNull(property!.GetCustomAttribute<JsonIgnoreAttribute>());
        }
    }

    // ------------------------------------------------- closed vocabularies

    [Fact]
    public void DocumentClassificationIsTheThreeMemberVocabularyAndDefaultsToUnsupported()
    {
        Assert.Equal(
            new[] { "MedicalExpenseEvidence", "T4Slip", "Unsupported" },
            Enum.GetNames<DocumentClassification>().OrderBy(name => name, StringComparer.Ordinal).ToArray());

        // A forgotten assignment must produce no tax input, not a slip.
        Assert.Equal(DocumentClassification.Unsupported, default);
    }

    [Fact]
    public void AcceptanceDefaultsToTheUnacceptedState()
    {
        Assert.Equal(ExtractionAcceptanceState.AwaitingAccountantDecision, default);

        Assert.Equal(
            new[] { "AcceptedByAccountant", "AwaitingAccountantDecision" },
            Enum.GetNames<ExtractionAcceptanceState>().OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void TheCorrectionVocabularyIsExactlyTheApprovedSet()
    {
        Assert.Equal(
            new[]
            {
                "AddT4Box",
                "EmployeeAddress",
                "EmployeeDisplayName",
                "EmployerAddress",
                "EmployerDisplayName",
                "MedicalCurrency",
                "MedicalGrossAmount",
                "MedicalPatientDisplayName",
                "MedicalProviderDisplayName",
                "MedicalReceiptReference",
                "MedicalServiceDate",
                "RemoveT4Box",
                "T4BoxCode",
                "T4BoxValue",
            },
            Enum.GetNames<CorrectionField>().OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("Sin")]
    [InlineData("SocialInsurance")]
    [InlineData("Box12")]
    [InlineData("TaxYear")]
    [InlineData("Year")]
    [InlineData("Firm")]
    [InlineData("Case")]
    [InlineData("Participant")]
    [InlineData("Document")]
    [InlineData("ContentKey")]
    [InlineData("CraLine")]
    [InlineData("Line")]
    [InlineData("Validation")]
    public void TheCorrectionVocabularyCannotNameAProhibitedTarget(string forbidden)
    {
        // A correction of the SIN, the server-derived year, the scope, a CRA
        // line, or a validation result is unrepresentable rather than refused
        // somewhere downstream.
        Assert.DoesNotContain(
            Enum.GetNames<CorrectionField>(),
            name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------- the box value invariant

    [Theory]
    [InlineData("12")]
    [InlineData(" 12")]
    [InlineData("12 ")]
    [InlineData("  12  ")]
    public void ABoxValueRefusesTheSocialInsuranceNumberBoxAtConstruction(string code)
    {
        Assert.False(ExtractionBoxValue.TryCreate(code, "123456789", out var refused));
        Assert.Null(refused);
        Assert.True(ExtractionBoxValue.IsSocialInsuranceNumberBox(code));
    }

    [Fact]
    public void TheRefusedBoxCodeIsTheEnginesOwnSocialInsuranceNumberConstant()
    {
        // Named from the engine rather than restated, so the two cannot drift.
        Assert.True(ExtractionBoxValue.IsSocialInsuranceNumberBox(
            T4BoxCodes.SocialInsuranceNumber));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABoxValueRefusesAnAbsentCode(string? code)
    {
        Assert.False(ExtractionBoxValue.TryCreate(code, "1000.00", out var refused));
        Assert.Null(refused);
    }

    [Fact]
    public void ABoxValueKeepsTheCodeAndValueExactlyAsExtracted()
    {
        Assert.True(ExtractionBoxValue.TryCreate(" 16A ", " 1,234.50 ", out var boxValue));

        // Refusal normalizes; acceptance does not. Nothing extracted is rewritten.
        Assert.Equal(" 16A ", boxValue!.Code);
        Assert.Equal(" 1,234.50 ", boxValue.Value);
    }

    [Fact]
    public void ABoxValueAcceptsAnEmptyBox()
    {
        Assert.True(ExtractionBoxValue.TryCreate("14", null, out var boxValue));
        Assert.Null(boxValue!.Value);
        Assert.Equal("ExtractionBoxValue(HasValue: False)", boxValue.ToString());
    }

    [Fact]
    public void NoPublicApiCanProduceABoxValueCarryingTheSocialInsuranceNumberBox()
    {
        // The invariant must hold for every way in, not just the one the tests
        // above happen to use.
        Assert.Empty(typeof(ExtractionBoxValue).GetConstructors(BindingFlags.Instance | BindingFlags.Public));

        var factories = typeof(ExtractionBoxValue)
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(method => method.ReturnType == typeof(ExtractionBoxValue))
            .ToArray();

        Assert.Empty(factories);
    }

    // ------------------------------------------------ classification outcome

    [Fact]
    public void AnAutomatedClassificationIsAcceptedOnlyWhenItIsADefinedValue()
    {
        Assert.True(ClassificationOutcome.TryAutomated(DocumentClassification.T4Slip, out var accepted));
        Assert.Equal(DocumentClassification.T4Slip, accepted!.AutomatedClassification);
        Assert.Equal(DocumentClassification.T4Slip, accepted.EffectiveClassification);
        Assert.False(accepted.IsOverridden);
        Assert.Null(accepted.OverriddenClassification);
        Assert.Null(accepted.OverrideActorReference);
        Assert.Null(accepted.OverriddenAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(99)]
    [InlineData(-1)]
    public void AnAbsentOrUndefinedClassificationIsRefusedRatherThanMapped(int? claimed)
    {
        Assert.False(
            ClassificationOutcome.TryAutomated((DocumentClassification?)claimed, out var refused));
        Assert.Null(refused);
    }

    [Fact]
    public void AnUnsupportedDocumentStillClassifiesRatherThanFailing()
    {
        Assert.True(
            ClassificationOutcome.TryAutomated(DocumentClassification.Unsupported, out var outcome));
        Assert.Equal(DocumentClassification.Unsupported, outcome!.EffectiveClassification);
    }

    [Theory]
    [InlineData(DocumentClassification.T4Slip)]
    [InlineData(DocumentClassification.MedicalExpenseEvidence)]
    public void AnOverrideRetainsTheOriginalAndDoesNotMutateTheReceiver(
        DocumentClassification target)
    {
        Assert.True(
            ClassificationOutcome.TryAutomated(DocumentClassification.Unsupported, out var automated));
        var actor = Guid.NewGuid();

        Assert.True(automated!.TryOverride(target, actor, Instant, out var overridden));

        Assert.Equal(DocumentClassification.Unsupported, overridden!.AutomatedClassification);
        Assert.Equal(target, overridden.OverriddenClassification);
        Assert.Equal(target, overridden.EffectiveClassification);
        Assert.True(overridden.IsOverridden);
        Assert.Equal(actor, overridden.OverrideActorReference);
        Assert.Equal(Instant, overridden.OverriddenAtUtc);

        // Append-only: the instance someone else already holds still reads the same.
        Assert.False(automated.IsOverridden);
        Assert.Null(automated.OverriddenClassification);
    }

    [Fact]
    public void AnOverrideCannotNameAnUnsupportedOrUndefinedTarget()
    {
        Assert.True(
            ClassificationOutcome.TryAutomated(DocumentClassification.T4Slip, out var automated));

        Assert.False(automated!.TryOverride(
            DocumentClassification.Unsupported, Guid.NewGuid(), Instant, out var unsupported));
        Assert.Null(unsupported);

        Assert.False(automated.TryOverride(
            (DocumentClassification)77, Guid.NewGuid(), Instant, out var undefined));
        Assert.Null(undefined);
    }

    [Fact]
    public void AnOverrideRequiresAnActorReferenceAndAUtcInstant()
    {
        Assert.True(
            ClassificationOutcome.TryAutomated(DocumentClassification.T4Slip, out var automated));

        Assert.False(automated!.TryOverride(
            DocumentClassification.MedicalExpenseEvidence, Guid.Empty, Instant, out var noActor));
        Assert.Null(noActor);

        Assert.False(automated.TryOverride(
            DocumentClassification.MedicalExpenseEvidence,
            Guid.NewGuid(),
            new DateTimeOffset(2026, 8, 3, 14, 30, 0, TimeSpan.FromHours(-4)),
            out var notUtc));
        Assert.Null(notUtc);
    }

    // -------------------------------------------------- field outcome shape

    [Fact]
    public void AFieldOutcomeCarriesAFieldAndAKindAndNoMessage()
    {
        Assert.True(FieldValidationOutcome.TryCreate(
            ExtractionWorkflowFields.MedicalServiceDate,
            FieldOutcomeKind.Missing,
            out var outcome));

        Assert.Equal(ExtractionWorkflowFields.MedicalServiceDate, outcome!.Field);
        Assert.Equal(FieldOutcomeKind.Missing, outcome.Kind);

        Assert.Equal(
            new[] { "Field", "Kind" },
            typeof(FieldValidationOutcome)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void AFieldOutcomeCarriesAnEngineFieldKeyVerbatim()
    {
        // The correspondence between an engine error and a field outcome is
        // one-to-one, so the engine's own field key is what is recorded.
        Assert.True(FieldValidationOutcome.TryCreate(
            "Box 45", FieldOutcomeKind.EngineRejected, out var outcome));

        Assert.Equal("Box 45", outcome!.Field);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AFieldOutcomeRefusesAnAbsentField(string? field)
    {
        Assert.False(FieldValidationOutcome.TryCreate(field, FieldOutcomeKind.Missing, out var refused));
        Assert.Null(refused);
    }

    [Fact]
    public void AFieldOutcomeRefusesAnUndefinedKind()
    {
        Assert.False(FieldValidationOutcome.TryCreate(
            ExtractionWorkflowFields.BoxCode, (FieldOutcomeKind)42, out var refused));
        Assert.Null(refused);
    }

    [Fact]
    public void AFieldOutcomeNeverPrintsItsFieldKey()
    {
        // Engine field keys are built from the box code the document showed.
        Assert.True(FieldValidationOutcome.TryCreate(
            "Box 123456789", FieldOutcomeKind.EngineRejected, out var outcome));

        Assert.Equal("FieldValidationOutcome(Kind: EngineRejected)", outcome!.ToString());
    }

    // --------------------------------------------------------- currency

    [Fact]
    public void AnAbsentCurrencyIsExplicitlyUnknown()
    {
        foreach (var absent in new[] { null, string.Empty, "   " })
        {
            var declaration = CurrencyDeclaration.FromPrintedCode(absent);

            Assert.False(declaration.IsKnown);
            Assert.Null(declaration.CodeAsPrinted);
        }

        Assert.False(CurrencyDeclaration.Unknown.IsKnown);
        Assert.Equal("CurrencyDeclaration(IsKnown: False)", CurrencyDeclaration.Unknown.ToString());
    }

    [Fact]
    public void APrintedCurrencyIsKeptExactlyAsPrinted()
    {
        var declaration = CurrencyDeclaration.FromPrintedCode(" cad ");

        Assert.True(declaration.IsKnown);
        Assert.Equal(" cad ", declaration.CodeAsPrinted);
    }

    // -------------------------------------------------- the T4 version shape

    [Fact]
    public void AnExtractionVersionCarriesWhatTheArchitectureSaysAndNothingIsMutable()
    {
        var version = SampleExtractionVersion();

        Assert.Equal(1, version.Version);
        Assert.Equal(DocumentClassification.T4Slip, version.Classification.EffectiveClassification);
        Assert.Single(version.BoxValues);
        Assert.Single(version.FieldOutcomes);
        Assert.Equal(ExtractionAcceptanceState.AwaitingAccountantDecision, version.AcceptanceState);
        Assert.Equal(Instant, version.RecordedAtUtc);

        Assert.All(
            typeof(ExtractionVersion).GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void AnExtractionVersionCopiesItsCollections()
    {
        Assert.True(ExtractionBoxValue.TryCreate("14", "50000.00", out var boxValue));
        var boxes = new List<ExtractionBoxValue> { boxValue! };

        var version = SampleExtractionVersion(boxValues: boxes);

        boxes.Clear();

        // What was validated stays what was validated.
        Assert.Single(version.BoxValues);
    }

    [Fact]
    public void AnExtractionVersionRefusesAnAbsentIdentifierVersionOrInstant()
    {
        Assert.Throws<ArgumentException>(() =>
            SampleExtractionVersion(extractionVersionId: Guid.Empty));

        Assert.Throws<ArgumentException>(() => SampleExtractionVersion(documentId: Guid.Empty));

        Assert.Throws<ArgumentOutOfRangeException>(() => SampleExtractionVersion(version: 0));

        Assert.Throws<ArgumentException>(() => SampleExtractionVersion(
            recordedAtUtc: new DateTimeOffset(2026, 8, 3, 14, 30, 0, TimeSpan.FromHours(2))));
    }

    [Fact]
    public void AnExtractionVersionRefusesAPrecedingReferenceThatIsNotDistinct()
    {
        var identifier = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => SampleExtractionVersion(
            extractionVersionId: identifier, precedingVersionId: identifier));

        Assert.Throws<ArgumentException>(() =>
            SampleExtractionVersion(precedingVersionId: Guid.Empty));
    }

    [Fact]
    public void AnExtractionVersionRefusesAnAbsentCollectionEntry()
    {
        Assert.Throws<ArgumentException>(() =>
            SampleExtractionVersion(boxValues: new ExtractionBoxValue[] { null! }));
    }

    [Fact]
    public void AnExtractionVersionPrintsStructuralFactsOnly()
    {
        Assert.Equal(
            "ExtractionVersion(Version: 1, Classification: T4Slip, BoxCount: 1, OutcomeCount: 1, Acceptance: AwaitingAccountantDecision, HasPreceding: False, HasProvenance: True)",
            SampleExtractionVersion().ToString());
    }

    [Fact]
    public void AnExtractionVersionKeepsAnAbsentAddressAbsent()
    {
        var version = SampleExtractionVersion(employerAddress: null, employeeAddress: null);

        // Nothing substitutes, infers, or copies a value in place of one the
        // document did not show.
        Assert.Null(version.EmployerAddress);
        Assert.Null(version.EmployeeAddress);
    }

    // ---------------------------------------------- the medical version shape

    [Fact]
    public void TheMedicalModelIsExactlyTheApprovedClosedFieldSet()
    {
        Assert.Equal(
            new[]
            {
                "AcceptanceState",
                "Currency",
                "DocumentId",
                "FieldOutcomes",
                "GrossAmountAsPrinted",
                "MedicalEvidenceVersionId",
                "PatientDisplayName",
                "PrecedingVersionId",
                "Provenance",
                "ProviderDisplayName",
                "ReceiptReference",
                "RecordedAtUtc",
                "ServiceDateAsPrinted",
                "Version",
            },
            typeof(MedicalEvidenceVersion)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Theory]
    [InlineData("Diagnos")]
    [InlineData("Treatment")]
    [InlineData("Medication")]
    [InlineData("Drug")]
    [InlineData("Prescription")]
    [InlineData("ServiceDescription")]
    [InlineData("Narrative")]
    [InlineData("Card")]
    [InlineData("Insurance")]
    [InlineData("HealthNumber")]
    [InlineData("Eligib")]
    [InlineData("Deduct")]
    [InlineData("Claim")]
    [InlineData("Reimburse")]
    [InlineData("CraLine")]
    [InlineData("Threshold")]
    public void TheMedicalModelCannotRepresentProhibitedContentOrATaxJudgement(string forbidden)
    {
        var members = typeof(MedicalEvidenceVersion)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Concat(typeof(ProviderMedicalResult)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name))
            .ToArray();

        Assert.DoesNotContain(
            members,
            name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AMedicalVersionKeepsTheDateAndAmountExactlyAsPrinted()
    {
        var version = SampleMedicalVersion(
            serviceDateAsPrinted: "14 mars 2025",
            grossAmountAsPrinted: "$1,120.50 CAD");

        Assert.Equal("14 mars 2025", version.ServiceDateAsPrinted);
        Assert.Equal("$1,120.50 CAD", version.GrossAmountAsPrinted);
    }

    [Fact]
    public void AMedicalVersionSurfacesAnAbsentFieldWithoutInventingOne()
    {
        var version = SampleMedicalVersion(
            serviceDateAsPrinted: null,
            grossAmountAsPrinted: null,
            providerDisplayName: null,
            patientDisplayName: null,
            receiptReference: null,
            currency: CurrencyDeclaration.Unknown);

        Assert.Null(version.ServiceDateAsPrinted);
        Assert.Null(version.GrossAmountAsPrinted);
        Assert.Null(version.ProviderDisplayName);
        Assert.Null(version.PatientDisplayName);
        Assert.Null(version.ReceiptReference);
        Assert.False(version.Currency.IsKnown);
    }

    [Fact]
    public void AMedicalVersionRefusesAnAbsentIdentifierVersionOrInstant()
    {
        Assert.Throws<ArgumentException>(() =>
            SampleMedicalVersion(medicalEvidenceVersionId: Guid.Empty));

        Assert.Throws<ArgumentException>(() => SampleMedicalVersion(documentId: Guid.Empty));

        Assert.Throws<ArgumentOutOfRangeException>(() => SampleMedicalVersion(version: 0));

        Assert.Throws<ArgumentException>(() => SampleMedicalVersion(
            recordedAtUtc: new DateTimeOffset(2026, 8, 3, 14, 30, 0, TimeSpan.FromHours(-7))));
    }

    [Fact]
    public void AMedicalVersionRequiresAnExplicitCurrencyDeclaration()
    {
        // "Unknown" must be stated, not implied by a null a later reader could
        // quietly treat as the local currency.
        Assert.Throws<ArgumentNullException>(() => new MedicalEvidenceVersion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "2025-03-14",
            "120.50",
            null!,
            null,
            null,
            null,
            null,
            Array.Empty<FieldValidationOutcome>(),
            ExtractionAcceptanceState.AwaitingAccountantDecision,
            null,
            Instant));
    }

    [Fact]
    public void AMedicalVersionPrintsStructuralFactsOnly()
    {
        Assert.Equal(
            "MedicalEvidenceVersion(Version: 1, HasServiceDate: True, HasGrossAmount: True, CurrencyKnown: True, HasProviderName: True, HasPatientName: True, HasReceiptReference: True, OutcomeCount: 1, Acceptance: AwaitingAccountantDecision, HasPreceding: False)",
            SampleMedicalVersion().ToString());
    }

    [Fact]
    public void ASuccessorVersionLeavesItsPredecessorUnchanged()
    {
        var first = SampleExtractionVersion();
        var second = SampleExtractionVersion(
            version: 2,
            precedingVersionId: first.ExtractionVersionId,
            acceptanceState: ExtractionAcceptanceState.AcceptedByAccountant);

        Assert.Equal(1, first.Version);
        Assert.Equal(ExtractionAcceptanceState.AwaitingAccountantDecision, first.AcceptanceState);
        Assert.Null(first.PrecedingVersionId);
        Assert.Equal(first.ExtractionVersionId, second.PrecedingVersionId);
    }

    // -------------------------------------------------------- serialization

    [Fact]
    public void SerializingAVersionEmitsNoTaxpayerValue()
    {
        var version = SampleExtractionVersion();
        var json = JsonSerializer.Serialize(version);

        foreach (var absent in new[] { "50000.00", "Acme", "Casey", "12 Main" })
        {
            Assert.DoesNotContain(absent, json, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------- helpers

    private static IEnumerable<Type> PublicInstanceTypes() =>
        ModuleAssembly
            .GetExportedTypes()
            .Where(type => type.IsClass && !(type.IsAbstract && type.IsSealed))
            .OrderBy(type => type.Name, StringComparer.Ordinal);

    internal static ExtractionVersion SampleExtractionVersion(
        Guid? extractionVersionId = null,
        Guid? documentId = null,
        int version = 1,
        IReadOnlyList<ExtractionBoxValue>? boxValues = null,
        string? employerAddress = "12 Main Street",
        string? employeeAddress = "9 Bay Road",
        ExtractionAcceptanceState acceptanceState = ExtractionAcceptanceState.AwaitingAccountantDecision,
        Guid? precedingVersionId = null,
        DateTimeOffset? recordedAtUtc = null)
    {
        Assert.True(ClassificationOutcome.TryAutomated(
            DocumentClassification.T4Slip, out var classification));
        Assert.True(FieldValidationOutcome.TryCreate(
            ExtractionWorkflowFields.BoxCode, FieldOutcomeKind.Extracted, out var outcome));

        if (boxValues is null)
        {
            Assert.True(ExtractionBoxValue.TryCreate("14", "50000.00", out var boxValue));
            boxValues = new[] { boxValue! };
        }

        return new ExtractionVersion(
            extractionVersionId ?? Guid.NewGuid(),
            documentId ?? Guid.NewGuid(),
            version,
            classification!,
            new ExtractionProvenance { SourceReference = "source", Confidence = 0.5m },
            "Acme Manufacturing",
            "Casey Taxpayer",
            employerAddress,
            employeeAddress,
            boxValues,
            new[] { outcome! },
            acceptanceState,
            precedingVersionId,
            recordedAtUtc ?? Instant);
    }

    internal static MedicalEvidenceVersion SampleMedicalVersion(
        Guid? medicalEvidenceVersionId = null,
        Guid? documentId = null,
        int version = 1,
        string? serviceDateAsPrinted = "2025-03-14",
        string? grossAmountAsPrinted = "$120.50",
        CurrencyDeclaration? currency = null,
        string? providerDisplayName = "Bay Street Dental",
        string? patientDisplayName = "Casey Taxpayer",
        string? receiptReference = "R-4471",
        DateTimeOffset? recordedAtUtc = null)
    {
        Assert.True(FieldValidationOutcome.TryCreate(
            ExtractionWorkflowFields.MedicalGrossAmount,
            FieldOutcomeKind.Extracted,
            out var outcome));

        return new MedicalEvidenceVersion(
            medicalEvidenceVersionId ?? Guid.NewGuid(),
            documentId ?? Guid.NewGuid(),
            version,
            serviceDateAsPrinted,
            grossAmountAsPrinted,
            currency ?? CurrencyDeclaration.FromPrintedCode("CAD"),
            providerDisplayName,
            patientDisplayName,
            receiptReference,
            new ExtractionProvenance { SourceReference = "source" },
            new[] { outcome! },
            ExtractionAcceptanceState.AwaitingAccountantDecision,
            null,
            recordedAtUtc ?? Instant);
    }
}
