using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using CaseProcessing.Models;
using CaseProcessing.Services;
using DocumentExtraction.Models;
using DocumentExtraction.Services;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace CaseProcessing.Tests.Services;

/// <summary>
/// T03 — the eight-stage pipeline. Covers AC-1, AC-3–AC-6, AC-8–AC-28, and
/// AC-34–AC-37.
/// <para>
/// The load-bearing idea of this suite is <b>anti-drift</b>: wherever a tax fact
/// is involved, the expectation is the engine's own output for the same input,
/// obtained by invoking <see cref="T4SlipParser"/> or <see cref="T4LineMapper"/>
/// directly. No test names a CRA line number, and no test restates a parser rule
/// — if this module ever started deciding something itself, the comparison
/// against the engine is what would fail.
/// </para>
/// </summary>
public sealed class T4CaseProcessorTests
{
    private static readonly EmbeddedT4LineMapProvider Embedded = new();

    // Box 16 is populated but deliberately unmapped by the 2025 config (its
    // companion Box 26 likewise), so it exercises the UnmappedBoxCodes path.
    private const string UnmappedButPopulatedBox = T4BoxCodes.CppContributions;

    // ============================================================== happy path

    [Fact]
    public void Process_ValidExtraction_Succeeds() // AC-1
    {
        var result = new T4CaseProcessor(Embedded).Process(ValidExtraction());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Value);
        Assert.NotNull(result.Value!.Slip);
        Assert.NotNull(result.Value.Mapping);
    }

    [Fact]
    public void Process_Slip_IsExactlyTheParsersOwn() // AC-3
    {
        var payload = ValidPayload(new T4RawBox(T4BoxCodes.EmploymentIncome, "52000.00"));
        var extraction = Validated(payload);

        var processed = new T4CaseProcessor(Embedded).Process(extraction);
        var parsedDirectly = new T4SlipParser().Parse(payload);

        Assert.True(processed.IsSuccess);
        Assert.True(parsedDirectly.IsSuccess);
        AssertSlipsEqual(parsedDirectly.Value!, processed.Value!.Slip);
    }

    [Fact]
    public void Process_Mapping_IsExactlyTheMappersOwn() // AC-4
    {
        var payload = ValidPayload(
            new T4RawBox(T4BoxCodes.EmploymentIncome, "52000.00"),
            new T4RawBox(T4BoxCodes.IncomeTaxDeducted, "8000.00"));
        var extraction = Validated(payload);

        var processed = new T4CaseProcessor(Embedded).Process(extraction);
        var slip = new T4SlipParser().Parse(payload).Value!;
        var mappedDirectly = new T4LineMapper(Embedded).Map(new[] { slip });

        Assert.True(processed.IsSuccess);
        Assert.True(mappedDirectly.IsSuccess);
        AssertMappingsEqual(mappedDirectly.Value!, processed.Value!.Mapping);
        Assert.NotEmpty(processed.Value.Mapping.Lines);
    }

    [Fact]
    public void Process_EchoesAttributionExactly() // AC-5
    {
        // Deliberately untrimmed and mixed-case: these are opaque caller strings,
        // so anything other than an ordinal echo is a defect.
        const string documentId = "  Doc-MARKER-alpha  ";
        const string firmId = "\tFirm-MARKER-beta ";
        const string caseId = " Case-MARKER-gamma\t";
        const string participantId = " Participant-MARKER-delta ";
        const string sourceReference = "  blob://Source-MARKER-epsilon  ";

        var extraction = Validated(
            ValidPayload(),
            documentId: documentId,
            firmId: firmId,
            caseId: caseId,
            participantId: participantId,
            sourceReference: sourceReference);

        var result = new T4CaseProcessor(Embedded).Process(extraction);

        Assert.True(result.IsSuccess);
        Assert.Equal(documentId, result.Value!.DocumentId, StringComparer.Ordinal);
        Assert.Equal(firmId, result.Value.FirmId, StringComparer.Ordinal);
        Assert.Equal(caseId, result.Value.CaseId, StringComparer.Ordinal);
        Assert.Equal(participantId, result.Value.ParticipantId, StringComparer.Ordinal);
        Assert.Equal(sourceReference, result.Value.SourceReference, StringComparer.Ordinal);
        Assert.Equal(extraction.TaxYear, result.Value.TaxYear);
    }

    [Fact]
    public void Process_UnmappedBoxCodes_PassThroughUnchangedAndNeverReject() // AC-6
    {
        var payload = ValidPayload(
            new T4RawBox(T4BoxCodes.EmploymentIncome, "52000.00"),
            new T4RawBox(UnmappedButPopulatedBox, "3500.00"),
            new T4RawBox(T4BoxCodes.CppQppPensionableEarnings, "68500.00"));

        var processed = new T4CaseProcessor(Embedded).Process(Validated(payload));
        var slip = new T4SlipParser().Parse(payload).Value!;
        var mappedDirectly = new T4LineMapper(Embedded).Map(new[] { slip }).Value!;

        Assert.True(processed.IsSuccess);
        Assert.NotEmpty(mappedDirectly.UnmappedBoxCodes);
        Assert.Equal(mappedDirectly.UnmappedBoxCodes, processed.Value!.Mapping.UnmappedBoxCodes);
        Assert.Contains(UnmappedButPopulatedBox, processed.Value.Mapping.UnmappedBoxCodes);
    }

    [Fact]
    public void Process_Success_YearsCohere() // AC-8
    {
        var extraction = ValidExtraction();

        var result = new T4CaseProcessor(Embedded).Process(extraction);

        Assert.True(result.IsSuccess);
        Assert.Equal(extraction.TaxYear, result.Value!.TaxYear);
        Assert.Equal(result.Value.Slip.Year, result.Value.TaxYear);
    }

    // ============================================================== boundaries

    [Fact]
    public void Process_EmptyBoxList_CarriesTheParsersRequiredBoxErrors() // AC-24
    {
        // Structurally traversable, so the guard stays silent and the parser's own
        // required-box rules are what reject it.
        var payload = Payload(boxes: Array.Empty<T4RawBox>());
        var recording = new RecordingProvider();

        var result = new T4CaseProcessor(recording).Process(Validated(payload));

        AssertCarriesParserErrorsVerbatim(result, payload);
        Assert.Empty(recording.RequestedYears);
    }

    [Fact]
    public void Process_ZeroAmounts_SucceedsAndEqualsTheEngine() // AC-25
    {
        var payload = ValidPayload(new T4RawBox(T4BoxCodes.EmploymentIncome, "0"));

        var processed = new T4CaseProcessor(Embedded).Process(Validated(payload));
        var slip = new T4SlipParser().Parse(payload).Value!;
        var mappedDirectly = new T4LineMapper(Embedded).Map(new[] { slip }).Value!;

        Assert.True(processed.IsSuccess);
        Assert.Equal(0m, processed.Value!.Slip.EmploymentIncome);
        AssertSlipsEqual(slip, processed.Value.Slip);
        AssertMappingsEqual(mappedDirectly, processed.Value.Mapping);
    }

    [Fact]
    public void Process_OnlyTheRequiredBoxes_SucceedsAndEqualsTheEngine() // AC-26
    {
        var payload = ValidPayload();

        var processed = new T4CaseProcessor(Embedded).Process(Validated(payload));
        var slip = new T4SlipParser().Parse(payload).Value!;
        var mappedDirectly = new T4LineMapper(Embedded).Map(new[] { slip }).Value!;

        Assert.True(processed.IsSuccess);
        AssertSlipsEqual(slip, processed.Value!.Slip);
        AssertMappingsEqual(mappedDirectly, processed.Value.Mapping);
    }

    [Theory]
    [InlineData("100.005", "100.01")]
    [InlineData("100.015", "100.02")]
    public void Process_HalfCentBoundary_IsTheEnginesRoundingUnchanged(string raw, string expected) // AC-27
    {
        // Half-up (MidpointRounding.AwayFromZero), applied by the engine. This
        // module neither re-rounds nor alters midpoint behaviour; the negative
        // direction is unreachable because the parser rejects negatives (AC-19).
        var amount = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        var expectedCents = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        var payload = ValidPayload(new T4RawBox(T4BoxCodes.EmploymentIncome, raw));

        var result = new T4CaseProcessor(Embedded).Process(Validated(payload));

        Assert.True(result.IsSuccess);
        Assert.Equal(MoneyRounding.ToCents(amount), result.Value!.Slip.EmploymentIncome);
        Assert.Equal(expectedCents, result.Value.Slip.EmploymentIncome);

        var fedByBox14 = result.Value.Mapping.Lines
            .Where(line => line.SourceBoxCodes.Contains(T4BoxCodes.EmploymentIncome, StringComparer.Ordinal))
            .ToArray();

        Assert.NotEmpty(fedByBox14);
        Assert.All(fedByBox14, line => Assert.Equal(expectedCents, line.Amount));
    }

    [Fact]
    public void Module_PerformsNoArithmetic() // AC-28
    {
        // No MoneyRounding call, no Math.Round, no MidpointRounding, no binary
        // floating point — and no `decimal` token at all in executable code, which
        // is the strongest available proxy for "no arithmetic on a monetary
        // value": a module that never names the money type cannot do money math.
        var forbidden = new Regex(
            @"\bMoneyRounding\b|\bMath\.Round\b|\bMidpointRounding\b|\bdouble\b|\bfloat\b|\bdecimal\b",
            RegexOptions.Compiled);

        var offending = ProductionSourceFiles()
            .SelectMany(file => ExecutableLines(file).Select(line => new { file, line }))
            .Where(entry => forbidden.IsMatch(entry.line))
            .Select(entry => $"{Path.GetFileName(entry.file)}: {entry.line}")
            .ToArray();

        Assert.Empty(offending);
    }

    [Fact]
    public void ModuleSourceScanner_SeesEveryProductionFile()
    {
        // Scanner guard: a wrong path or an over-eager comment filter would
        // otherwise make the arithmetic scan above pass for free.
        var files = ProductionSourceFiles();

        Assert.True(files.Length >= 5, $"Expected the module's production sources, found {files.Length}.");
        Assert.Contains(files, file => Path.GetFileName(file) == "T4CaseProcessor.cs");
        Assert.Contains(files, file => Path.GetFileName(file) == "PayloadStructureGuard.cs");
        Assert.Contains(files, file => Path.GetFileName(file) == "ProcessedT4Extraction.cs");

        var allExecutable = string.Join("\n", files.SelectMany(ExecutableLines));
        Assert.Contains("T4SlipParser", allExecutable, StringComparison.Ordinal);
        Assert.Contains("T4LineMapper", allExecutable, StringComparison.Ordinal);
    }

    // ======================================================= fail-closed arms

    [Fact]
    public void Process_NullExtraction_FailsClosedWithExactlyOneError() // AC-9
    {
        var recording = new RecordingProvider();

        var result = Record.Exception(() => new T4CaseProcessor(recording).Process(null));
        Assert.Null(result);

        var failure = new T4CaseProcessor(recording).Process(null);

        Assert.True(failure.IsFailure);
        Assert.Null(failure.Value);
        var error = Assert.Single(failure.Errors);
        Assert.Equal(T4ProcessingFields.Extraction, error.Field);
        Assert.Empty(recording.RequestedYears);
    }

    [Fact]
    public void Process_GenericEvidenceKind_FailsAndNeverQueriesTheProvider() // AC-10
    {
        var envelope = new ExtractionEnvelope
        {
            DocumentId = "doc-1",
            Kind = ExtractedDocumentKind.GenericEvidence,
            FirmId = "firm-1",
            CaseId = "case-1",
            ParticipantId = "participant-1",
            TaxYear = 2025,
            Provenance = new ExtractionProvenance { SourceReference = "blob://evidence" },
            T4Payload = null,
        };

        var validated = ExtractionEnvelopeValidator.Validate(envelope);
        Assert.True(validated.IsSuccess);

        var recording = new RecordingProvider();
        var result = new T4CaseProcessor(recording).Process(validated.Value);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        var error = Assert.Single(result.Errors);
        Assert.Equal(T4ProcessingFields.Kind, error.Field);
        Assert.Empty(recording.RequestedYears);
    }

    [Fact]
    public void Process_NullBoxCollection_FailsClosedWithoutThrowing() // AC-11 at pipeline level
    {
        var payload = Payload(boxes: null);
        var recording = new RecordingProvider();

        var result = new T4CaseProcessor(recording).Process(Validated(payload));

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(T4ProcessingFields.PayloadBoxes, Assert.Single(result.Errors).Field);
        Assert.Empty(recording.RequestedYears);
    }

    [Fact]
    public void Process_StructuralAndYearDefectsAccumulateInStageOrder()
    {
        // Stages 3 and 4 are independent, so a caller fixing one learns about the
        // other in the same pass — the year defect first, then the structural one.
        var payload = Payload(boxes: new List<T4RawBox> { null! });
        var extraction = ExtractionWithYear(2024, payload);
        var recording = new RecordingProvider();

        var result = new T4CaseProcessor(recording).Process(extraction);

        Assert.True(result.IsFailure);
        Assert.Equal(
            new[] { T4ProcessingFields.TaxYear, T4ProcessingFields.PayloadBoxes },
            result.Errors.Select(error => error.Field).ToArray());
        Assert.Empty(recording.RequestedYears);
    }

    /// <summary>
    /// The extra boxes for each parser rejection family. Kept out of
    /// <c>TheoryData</c> deliberately: only serializable arguments cross the
    /// theory boundary, so the family is named by a string and built here.
    /// </summary>
    private static T4RawBox[] RejectionFamilyBoxes(string family) => family switch
    {
        // AC-14 — duplicate box code (spec 007 preserves duplicates by design).
        "duplicate box code" => new[]
        {
            new T4RawBox(T4BoxCodes.EmploymentIncome, "1.00"),
            new T4RawBox(T4BoxCodes.EmploymentIncome, "2.00"),
        },
        // AC-18 — malformed amount.
        "malformed amount" => new[] { new T4RawBox(T4BoxCodes.EmploymentIncome, "not-a-number") },
        // AC-19 — negative amount.
        "negative amount" => new[] { new T4RawBox(T4BoxCodes.EmploymentIncome, "-1.00") },
        // AC-20 — constrained code out of range.
        "constrained code out of range" => new[] { new T4RawBox(T4BoxCodes.ProvinceOfEmployment, "XX") },
        // AC-21 — trigger box without its parser-required companion.
        "missing companion box" => new[] { new T4RawBox(T4BoxCodes.EiPremiums, "900.00") },
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown rejection family."),
    };

    [Theory]
    [InlineData("duplicate box code", "Box 14")]
    [InlineData("malformed amount", "Box 14")]
    [InlineData("negative amount", "Box 14")]
    [InlineData("constrained code out of range", "Box 10")]
    [InlineData("missing companion box", "Box 24")]
    public void Process_ParserRejection_CarriesTheParsersErrorsVerbatim(
        string family, string expectedField) // AC-14, AC-18–AC-21
    {
        var payload = ValidPayload(RejectionFamilyBoxes(family));
        var recording = new RecordingProvider();

        var result = new T4CaseProcessor(recording).Process(Validated(payload));

        AssertCarriesParserErrorsVerbatim(result, payload);
        Assert.Contains(result.Errors, error => error.Field == expectedField);
        Assert.Empty(recording.RequestedYears);
    }

    [Theory]
    [InlineData(T4BoxCodes.SocialInsuranceNumber, "Box 12")] // AC-15
    [InlineData(T4BoxCodes.DentalBenefits, "Box 45")]        // AC-16
    [InlineData(T4BoxCodes.EmployerAccountNumber, "Box 54")] // AC-17
    public void Process_MissingRequiredBox_CarriesTheParsersErrorVerbatim(
        string omittedBoxCode, string expectedField) // AC-15, AC-16, AC-17
    {
        var boxes = RequiredBoxes().Where(box => box.Code != omittedBoxCode).ToList();
        var payload = Payload(boxes: boxes);
        var recording = new RecordingProvider();

        var result = new T4CaseProcessor(recording).Process(Validated(payload));

        AssertCarriesParserErrorsVerbatim(result, payload);
        Assert.Contains(result.Errors, error => error.Field == expectedField);
        Assert.Empty(recording.RequestedYears);
    }

    [Fact]
    public void Process_ProviderFailsForTheYear_SurfacesItsErrorsAndAsksForNoOtherYear() // AC-22
    {
        var provider = new FailingProvider();

        var result = new T4CaseProcessor(provider).Process(ValidExtraction());

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        // Verbatim and in order — never translated, summarized, or re-worded.
        Assert.Equal(FailingProvider.StubErrors, result.Errors.ToArray());
        Assert.Equal(new[] { 2025 }, provider.RequestedYears.ToArray());
    }

    [Fact]
    public void Process_ParserRejection_StopsThePipelineBeforeTheProvider() // AC-23
    {
        var payload = ValidPayload(new T4RawBox(T4BoxCodes.EmploymentIncome, "not-a-number"));
        var recording = new RecordingProvider(Embedded);

        var result = new T4CaseProcessor(recording).Process(Validated(payload));

        Assert.True(result.IsFailure);
        Assert.Empty(recording.RequestedYears);
        AssertCarriesParserErrorsVerbatim(result, payload);
    }

    [Theory]
    [InlineData(2024)]
    [InlineData(2026)]
    [InlineData(1999)]
    public void Process_UnsupportedExtractionYear_FailsClosedAndNeverQueriesTheProvider(int taxYear)
    {
        // Reachable only by reflection: spec 007's gate already refuses to mint a
        // ValidatedExtraction for an off year (AC-37), so this proves stage 3
        // stands on its own rather than leaning on the upstream module.
        var extraction = ExtractionWithYear(taxYear, ValidPayload());
        var recording = new RecordingProvider(Embedded);

        var result = new T4CaseProcessor(recording).Process(extraction);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(T4ProcessingFields.TaxYear, Assert.Single(result.Errors).Field);
        Assert.Empty(recording.RequestedYears);
    }

    [Fact]
    public void SlipYearGuard_IsUnreachableOnlyBecauseBothYearPoliciesAgree()
    {
        // Stage 6 cannot fire today: the module supports 2025 only and the parser
        // yields 2025 only, so a slip that parses can never disagree with an
        // extraction that reached stage 6. That is a property of the two
        // policies, not of the guard — this test fails the moment they diverge,
        // at which point stage 6 needs a live rejection test.
        foreach (var supportedYear in SupportedProcessingTaxYears.All)
        {
            var parsed = new T4SlipParser().Parse(Payload(year: supportedYear, boxes: RequiredBoxes()));

            Assert.True(
                parsed.IsSuccess,
                $"The parser no longer accepts supported processing year {supportedYear}; stage 6 is now reachable.");
            Assert.Equal(supportedYear, parsed.Value!.Year);
        }
    }

    // ========================================================= tax-year isolation

    [Fact]
    public void YearPolicy_IsExactly2025() // AC-34
    {
        Assert.Equal(new[] { 2025 }, SupportedProcessingTaxYears.All);
        Assert.True(SupportedProcessingTaxYears.IsSupported(2025));
        Assert.False(SupportedProcessingTaxYears.IsSupported(2024));
        Assert.False(SupportedProcessingTaxYears.IsSupported(2026));
    }

    [Fact]
    public void Process_AsksTheProviderForTheSlipsYearExactlyOnce() // AC-35
    {
        var recording = new RecordingProvider(Embedded);

        var result = new T4CaseProcessor(recording).Process(ValidExtraction());

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 2025 }, recording.RequestedYears.ToArray());
        Assert.Equal(result.Value!.Slip.Year, recording.RequestedYears.Single());
    }

    [Fact]
    public void Process_ProviderHoldingOnly2024_NeverMapsA2025Slip() // AC-36
    {
        var provider = new SingleYearProvider(2024);

        var result = new T4CaseProcessor(provider).Process(ValidExtraction());

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        // Asked for 2025 and nothing else: no adjacent-year retry, so the 2024
        // map it holds is never handed to a 2025 slip.
        Assert.Equal(new[] { 2025 }, provider.RequestedYears.ToArray());
        Assert.False(provider.MapWasHandedOut);
    }

    [Theory]
    [InlineData(2024)]
    [InlineData(2026)]
    public void OffYearEnvelopes_NeverBecomeAValidatedExtraction(int taxYear) // AC-37
    {
        var envelope = EnvelopeFor(Payload(year: taxYear), taxYear: taxYear);

        var validated = ExtractionEnvelopeValidator.Validate(envelope);

        Assert.True(validated.IsFailure);
        Assert.Null(validated.Value);
        // Nothing reached the processor and neither year was coerced to 2025.
        Assert.DoesNotContain(validated.Errors, error => error.Message.Contains("2025", StringComparison.Ordinal));
    }

    // ================================================== cross-cutting guarantees

    [Fact]
    public void Process_NeverThrowsAndEveryFailureCarriesNoValue()
    {
        foreach (var (name, extraction, provider) in FailureCases())
        {
            var processor = new T4CaseProcessor(provider);

            var exception = Record.Exception(() => processor.Process(extraction));
            Assert.Null(exception);

            var result = processor.Process(extraction);
            Assert.True(result.IsFailure, $"Expected {name} to fail closed.");
            Assert.Null(result.Value);
            Assert.NotEmpty(result.Errors);
        }
    }

    [Fact]
    public void Process_EverySelfOriginatedField_IsAT4ProcessingFieldsConstant()
    {
        var declared = typeof(T4ProcessingFields)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && !field.IsInitOnly)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);

        // Only the stages this module originates itself; engine errors keep the
        // engine's own field names and are surfaced verbatim.
        var selfOriginated = new[]
        {
            new T4CaseProcessor(Embedded).Process(null),
            new T4CaseProcessor(Embedded).Process(GenericEvidenceExtraction()),
            new T4CaseProcessor(Embedded).Process(Validated(Payload(boxes: null))),
            new T4CaseProcessor(Embedded).Process(Validated(Payload(boxes: new[] { new T4RawBox("", "x") }))),
            new T4CaseProcessor(Embedded).Process(ExtractionWithYear(2024, ValidPayload())),
        };

        var emitted = selfOriginated.SelectMany(result => result.Errors).Select(error => error.Field).ToArray();

        Assert.NotEmpty(emitted);
        Assert.All(emitted, field => Assert.Contains(field, declared));
    }

    [Fact]
    public void Process_OwnMessagesAreNonInterpolatedConstantsAndNameNoYear()
    {
        // A fixed template cannot carry a caller or payload value, and the
        // supported year is never spelled out, so the policy stays in one place.
        var messages = new[]
        {
            new T4CaseProcessor(Embedded).Process(null),
            new T4CaseProcessor(Embedded).Process(GenericEvidenceExtraction()),
            new T4CaseProcessor(Embedded).Process(ExtractionWithYear(2024, ValidPayload())),
        }.SelectMany(result => result.Errors).Select(error => error.Message).ToArray();

        Assert.NotEmpty(messages);
        Assert.All(messages, message =>
        {
            Assert.DoesNotContain("2025", message, StringComparison.Ordinal);
            Assert.DoesNotContain("2024", message, StringComparison.Ordinal);
            Assert.DoesNotMatch(new Regex(@"\d{9}"), message);
        });
    }

    [Fact]
    public void Process_DoesNotMutateTheExtractionOrItsPayload()
    {
        var payload = ValidPayload(new T4RawBox(T4BoxCodes.EmploymentIncome, "52000.00"));
        var extraction = Validated(payload);
        var boxSnapshot = payload.Boxes.ToArray();

        new T4CaseProcessor(Embedded).Process(extraction);

        Assert.Same(payload, extraction.T4Payload);
        Assert.Equal(boxSnapshot.Length, payload.Boxes.Count);
        for (var index = 0; index < boxSnapshot.Length; index++)
        {
            Assert.Same(boxSnapshot[index], payload.Boxes[index]);
        }

        Assert.Equal(2025, payload.Year);
        Assert.Equal("ACME CORP", payload.EmployerName);
    }

    // ==================================================================== doubles

    private sealed class RecordingProvider : IT4LineMapProvider
    {
        private readonly IT4LineMapProvider? _inner;

        public RecordingProvider(IT4LineMapProvider? inner = null) => _inner = inner;

        public List<int> RequestedYears { get; } = new();

        public ParseResult<T4LineMap> TryGetForYear(int taxYear)
        {
            RequestedYears.Add(taxYear);
            return _inner?.TryGetForYear(taxYear)
                ?? ParseResult<T4LineMap>.Failure(
                    new ValidationError("TaxYear", "stub: this recording provider holds no map."));
        }
    }

    private sealed class FailingProvider : IT4LineMapProvider
    {
        // Two errors in a fixed order, so "verbatim and in order" is falsifiable.
        public static readonly ValidationError[] StubErrors =
        {
            new("TaxYear", "stub: no config for that year."),
            new("Config", "stub: and a second error, to pin ordering."),
        };

        public List<int> RequestedYears { get; } = new();

        public ParseResult<T4LineMap> TryGetForYear(int taxYear)
        {
            RequestedYears.Add(taxYear);
            return ParseResult<T4LineMap>.Failure(StubErrors);
        }
    }

    /// <summary>
    /// Holds a real map for exactly one year and fails closed for every other, so
    /// a wrong-year map can be proved never to be handed out.
    /// </summary>
    private sealed class SingleYearProvider : IT4LineMapProvider
    {
        private readonly int _year;

        public SingleYearProvider(int year) => _year = year;

        public List<int> RequestedYears { get; } = new();

        public bool MapWasHandedOut { get; private set; }

        public ParseResult<T4LineMap> TryGetForYear(int taxYear)
        {
            RequestedYears.Add(taxYear);

            if (taxYear != _year)
            {
                return ParseResult<T4LineMap>.Failure(
                    new ValidationError("TaxYear", "stub: this provider holds another year's config."));
            }

            // A deliberately non-CRA destination label: this suite never names a
            // real line number.
            var config = new T4LineMappingConfig(
                _year,
                new[] { new T4BoxLineMapping(T4BoxCodes.EmploymentIncome, new[] { "TEST-LINE-A" }) });

            var map = T4LineMap.Create(config);
            Assert.True(map.IsSuccess);
            MapWasHandedOut = true;
            return map;
        }
    }

    // ==================================================================== helpers

    private static List<T4RawBox> RequiredBoxes() => new()
    {
        new(T4BoxCodes.SocialInsuranceNumber, "046454286"),
        new(T4BoxCodes.EmployerAccountNumber, "123456789RP0001"),
        new(T4BoxCodes.DentalBenefits, "1"),
    };

    private static T4RawInput Payload(
        int? year = 2025,
        IEnumerable<T4RawBox>? boxes = null) =>
        new()
        {
            Year = year,
            EmployerName = "ACME CORP",
            EmployerAddress = "1 WAY ST, OTTAWA ON",
            EmployeeName = "DOE, JANE",
            EmployeeAddress = "2 HOME AVE, OTTAWA ON",
            Boxes = boxes is null ? null! : boxes.ToList(),
        };

    private static T4RawInput ValidPayload(params T4RawBox[] extraBoxes)
    {
        var boxes = RequiredBoxes();
        boxes.AddRange(extraBoxes);
        return Payload(boxes: boxes);
    }

    private static ExtractionEnvelope EnvelopeFor(
        T4RawInput? payload,
        ExtractedDocumentKind kind = ExtractedDocumentKind.T4Slip,
        int? taxYear = 2025,
        string? documentId = "doc-1",
        string? firmId = "firm-1",
        string? caseId = "case-1",
        string? participantId = "participant-1",
        string? sourceReference = "blob://source-1") =>
        new()
        {
            DocumentId = documentId,
            Kind = kind,
            FirmId = firmId,
            CaseId = caseId,
            ParticipantId = participantId,
            TaxYear = taxYear,
            Provenance = new ExtractionProvenance { SourceReference = sourceReference },
            T4Payload = payload,
        };

    /// <summary>
    /// A <see cref="ValidatedExtraction"/> minted the only supported way — through
    /// spec 007's gate — so no test fabricates upstream proof.
    /// </summary>
    private static ValidatedExtraction Validated(
        T4RawInput payload,
        string documentId = "doc-1",
        string firmId = "firm-1",
        string caseId = "case-1",
        string participantId = "participant-1",
        string sourceReference = "blob://source-1")
    {
        var validated = ExtractionEnvelopeValidator.Validate(
            EnvelopeFor(
                payload,
                documentId: documentId,
                firmId: firmId,
                caseId: caseId,
                participantId: participantId,
                sourceReference: sourceReference));

        Assert.True(validated.IsSuccess, "The test envelope must clear spec 007's gate.");
        return validated.Value!;
    }

    private static ValidatedExtraction ValidExtraction() =>
        Validated(ValidPayload(new T4RawBox(T4BoxCodes.EmploymentIncome, "52000.00")));

    private static ValidatedExtraction GenericEvidenceExtraction()
    {
        var validated = ExtractionEnvelopeValidator.Validate(
            EnvelopeFor(payload: null, kind: ExtractedDocumentKind.GenericEvidence));

        Assert.True(validated.IsSuccess);
        return validated.Value!;
    }

    /// <summary>
    /// Builds a <see cref="ValidatedExtraction"/> for an unsupported year through
    /// its internal constructor. Spec 007 will not mint one (AC-37), and that is
    /// exactly why stage 3 must be provable on its own rather than by assuming the
    /// upstream gate stays as it is.
    /// </summary>
    private static ValidatedExtraction ExtractionWithYear(int taxYear, T4RawInput payload)
    {
        var constructor = typeof(ValidatedExtraction)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();

        return (ValidatedExtraction)constructor.Invoke(new object?[]
        {
            "doc-1",
            ExtractedDocumentKind.T4Slip,
            "firm-1",
            "case-1",
            "participant-1",
            taxYear,
            "blob://source-1",
            null,
            null,
            payload,
        });
    }

    private static IEnumerable<(string Name, ValidatedExtraction? Extraction, IT4LineMapProvider Provider)> FailureCases()
    {
        yield return ("null extraction", null, Embedded);
        yield return ("generic evidence kind", GenericEvidenceExtraction(), Embedded);
        yield return ("null box collection", Validated(Payload(boxes: null)), Embedded);
        yield return ("null box element", Validated(Payload(boxes: new List<T4RawBox> { null! })), Embedded);
        yield return ("blank box code", Validated(Payload(boxes: new[] { new T4RawBox("  ", "x") })), Embedded);
        yield return ("unsupported year", ExtractionWithYear(2024, ValidPayload()), Embedded);
        yield return ("empty box list", Validated(Payload(boxes: Array.Empty<T4RawBox>())), Embedded);
        yield return ("duplicate box", Validated(ValidPayload(
            new T4RawBox(T4BoxCodes.EmploymentIncome, "1.00"),
            new T4RawBox(T4BoxCodes.EmploymentIncome, "2.00"))), Embedded);
        yield return ("malformed amount", Validated(ValidPayload(
            new T4RawBox(T4BoxCodes.EmploymentIncome, "not-a-number"))), Embedded);
        yield return ("provider failure", ValidExtraction(), new FailingProvider());
        yield return ("wrong-year provider", ValidExtraction(), new SingleYearProvider(2024));
    }

    private static void AssertCarriesParserErrorsVerbatim(
        ParseResult<ProcessedT4Extraction> result,
        T4RawInput payload)
    {
        var parserErrors = new T4SlipParser().Parse(payload);

        Assert.True(parserErrors.IsFailure, "This payload must be one the parser itself rejects.");
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        // Same errors, same order, nothing translated, filtered, or interleaved.
        Assert.Equal(parserErrors.Errors.ToArray(), result.Errors.ToArray());
    }

    private static void AssertSlipsEqual(T4Slip expected, T4Slip actual)
    {
        var properties = typeof(T4Slip).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // Guard: a reflection walk that found nothing would assert nothing.
        Assert.True(properties.Length > 20, $"Expected T4Slip's full property set, found {properties.Length}.");

        foreach (var property in properties)
        {
            var expectedValue = property.GetValue(expected);
            var actualValue = property.GetValue(actual);

            if (expectedValue is not string && expectedValue is IEnumerable expectedItems)
            {
                Assert.Equal(
                    expectedItems.Cast<object>().ToArray(),
                    ((IEnumerable)actualValue!).Cast<object>().ToArray());
            }
            else
            {
                Assert.Equal(expectedValue, actualValue);
            }
        }
    }

    private static void AssertMappingsEqual(T4MappingResult expected, T4MappingResult actual)
    {
        Assert.Equal(expected.Lines.Count, actual.Lines.Count);

        for (var index = 0; index < expected.Lines.Count; index++)
        {
            // Same entries in the same order, with the same amounts and the same
            // source box codes.
            Assert.Equal(expected.Lines[index].LineNumber, actual.Lines[index].LineNumber, StringComparer.Ordinal);
            Assert.Equal(expected.Lines[index].Amount, actual.Lines[index].Amount);
            Assert.Equal(expected.Lines[index].SourceBoxCodes, actual.Lines[index].SourceBoxCodes);
        }

        Assert.Equal(expected.UnmappedBoxCodes, actual.UnmappedBoxCodes);
    }

    private static string[] ProductionSourceFiles() =>
        Directory.GetFiles(
                Path.Combine(FindRepositoryRoot(), "src", "CaseProcessing"), "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Source lines excluding comments. Documentation legitimately names forbidden
    /// concepts while explaining why they are forbidden, so only executable code is
    /// scanned.
    /// </summary>
    private static IEnumerable<string> ExecutableLines(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the repository root containing TaxAI.sln.");
    }
}
