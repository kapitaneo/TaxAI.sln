using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using CaseProcessing.Models;
using CaseProcessing.Services;
using DocumentExtraction.Models;
using DocumentExtraction.Services;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace CaseProcessing.Tests.EndToEnd;

/// <summary>
/// T04 — the finished contract exercised only through its public surface,
/// starting from a real <see cref="ExtractionEnvelope"/>. Covers AC-2 (the full
/// chain), AC-38 (no CRA line number is hardcoded) and AC-39 (no box value is
/// interpreted and no <c>T4Slip</c> is constructed here).
/// </summary>
public sealed class T4ProcessingAcceptanceTests
{
    [Fact]
    public void FullChain_EnvelopeToValidatedExtractionToResult_MatchesTheEngine() // AC-2
    {
        // Every step is shipped code: spec 007's gate, then this module, then the
        // engine underneath it. Nothing between them is hand-written.
        var envelope = T4ProcessingFixture.Envelope();
        var payload = envelope.T4Payload!;

        var validated = ExtractionEnvelopeValidator.Validate(envelope);
        Assert.True(validated.IsSuccess);

        var result = new T4CaseProcessor(T4ProcessingFixture.Embedded).Process(validated.Value);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);

        // The line entries are the engine's own for the same payload.
        var slip = new T4SlipParser().Parse(payload).Value!;
        var mapped = new T4LineMapper(T4ProcessingFixture.Embedded).Map(new[] { slip }).Value!;

        T4ProcessingFixture.AssertMappingsEqual(mapped, result.Value!.Mapping);
        Assert.NotEmpty(result.Value.Mapping.Lines);
        Assert.Equal(validated.Value!.TaxYear, result.Value.TaxYear);
    }

    [Fact]
    public void FullChain_PayloadReachesTheParserReferenceIdentical() // AC-2
    {
        // Spec 007 promises the payload passes through reference-identical; this
        // pins that the processor consumes that same instance rather than a copy
        // it normalized on the way in.
        var envelope = T4ProcessingFixture.Envelope();
        var validated = ExtractionEnvelopeValidator.Validate(envelope).Value!;

        Assert.Same(envelope.T4Payload, validated.T4Payload);

        var result = new T4CaseProcessor(T4ProcessingFixture.Embedded).Process(validated);

        Assert.True(result.IsSuccess);
        Assert.Same(envelope.T4Payload, validated.T4Payload);
    }

    // ============================================ AC-38 — no hardcoded CRA line

    [Fact]
    public void LineNumbers_ComeOnlyFromTheInjectedMap() // AC-38
    {
        // A provider holding a known in-memory map whose destinations are
        // deliberately not CRA lines. If any line number were baked into this
        // module, the output could not follow this map exactly.
        var provider = new InMemoryMapProvider(
            T4ProcessingFixture.TaxYear,
            (T4BoxCodes.EmploymentIncome, new[] { "TEST-LINE-A", "TEST-LINE-B" }),
            (T4BoxCodes.IncomeTaxDeducted, new[] { "TEST-LINE-C" }));

        var validated = ExtractionEnvelopeValidator.Validate(T4ProcessingFixture.Envelope()).Value!;

        var result = new T4CaseProcessor(provider).Process(validated);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { "TEST-LINE-A", "TEST-LINE-B", "TEST-LINE-C" },
            result.Value!.Mapping.Lines.Select(line => line.LineNumber).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void LineNumbers_FollowTheProviderAndNothingElse() // AC-38
    {
        // The same extraction through two different maps yields two different sets
        // of destinations — the output tracks the config, not the code.
        var validated = ExtractionEnvelopeValidator.Validate(T4ProcessingFixture.Envelope()).Value!;
        var processor = new T4CaseProcessor(T4ProcessingFixture.Embedded);
        var injected = new T4CaseProcessor(new InMemoryMapProvider(
            T4ProcessingFixture.TaxYear,
            (T4BoxCodes.EmploymentIncome, new[] { "TEST-LINE-A" })));

        var fromEmbedded = processor.Process(validated);
        var fromInjected = injected.Process(validated);

        Assert.True(fromEmbedded.IsSuccess);
        Assert.True(fromInjected.IsSuccess);

        var embeddedLines = fromEmbedded.Value!.Mapping.Lines.Select(line => line.LineNumber).ToHashSet(StringComparer.Ordinal);
        var injectedLines = fromInjected.Value!.Mapping.Lines.Select(line => line.LineNumber).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(embeddedLines);
        Assert.Equal(new[] { "TEST-LINE-A" }, injectedLines);
        Assert.Empty(embeddedLines.Intersect(injectedLines, StringComparer.Ordinal));
    }

    [Fact]
    public void ModuleSource_ContainsNoCraLineLiteralAndNoMappingResourceName() // AC-38
    {
        var craLikeLiteral = new Regex(@"""\d{3,5}""", RegexOptions.Compiled);

        var offending = T4ProcessingFixture.ProductionSourceFiles()
            .SelectMany(file => T4ProcessingFixture.ExecutableLines(file)
                .Select(line => new { File = Path.GetFileName(file), Line = line }))
            .Where(entry =>
                craLikeLiteral.IsMatch(entry.Line) ||
                entry.Line.Contains("TaxLogicEngine.Mappings", StringComparison.Ordinal) ||
                entry.Line.Contains("T4LineMap.", StringComparison.Ordinal))
            .Select(entry => $"{entry.File}: {entry.Line}")
            .ToArray();

        Assert.Empty(offending);
    }

    // ==================================== AC-39 — no box value, no slip built here

    [Fact]
    public void ModuleSource_ConstructsNoSlipAndInterpretsNoBoxValue() // AC-39
    {
        var forbidden = new (string Name, Regex Pattern)[]
        {
            ("constructs a T4Slip", new Regex(@"new\s+T4Slip\b")),
            ("names T4RawBox", new Regex(@"\bT4RawBox\b")),
            ("parses a value", new Regex(@"\bdecimal\s*\.\s*(Try)?Parse\b")),
            ("declares a box-code list", new Regex(@"\bHashSet<string>|\bT4BoxCodes\b|\bT4OtherInformationCodes\b")),
            ("constructs a RecipientIdentifier", new Regex(@"new\s+RecipientIdentifier\b")),
        };

        foreach (var (name, pattern) in forbidden)
        {
            var offending = T4ProcessingFixture.ProductionSourceFiles()
                .SelectMany(file => T4ProcessingFixture.ExecutableLines(file)
                    .Select(line => new { File = Path.GetFileName(file), Line = line }))
                .Where(entry => pattern.IsMatch(entry.Line))
                .Select(entry => $"{name} — {entry.File}: {entry.Line}")
                .ToArray();

            Assert.Empty(offending);
        }
    }

    [Fact]
    public void ModuleSource_ReadsNoBoxValue() // AC-39
    {
        // `.Value` is legitimate on a ParseResult and forbidden on a T4RawBox. The
        // module never names T4RawBox (asserted above), so every `.Value` read is
        // pinned here to the ParseResult locals it is allowed on — anything else,
        // including a box read through `var`, fails.
        var allowedReceivers = new HashSet<string>(StringComparer.Ordinal) { "parsed", "mapped", "map", "result" };
        var valueRead = new Regex(@"(\w+)\s*(?:!|\?)?\s*\.\s*Value\b", RegexOptions.Compiled);

        var offending = T4ProcessingFixture.ProductionSourceFiles()
            .SelectMany(file => T4ProcessingFixture.ExecutableLines(file)
                .Select(line => new { File = Path.GetFileName(file), Line = line }))
            .SelectMany(entry => valueRead.Matches(entry.Line)
                .Select(match => new { entry.File, entry.Line, Receiver = match.Groups[1].Value }))
            .Where(hit => !allowedReceivers.Contains(hit.Receiver))
            .Select(hit => $"{hit.File}: `{hit.Receiver}.Value` in `{hit.Line}`")
            .ToArray();

        Assert.Empty(offending);
    }

    [Fact]
    public void ModuleRuntime_NeverProducesASlipTheParserWouldNot() // AC-39
    {
        // The behavioural counterpart: for a payload the parser rejects, the module
        // produces no slip at all rather than an interpretation of its own.
        var payload = T4ProcessingFixture.Payload(defect: "MalformedAmount");
        var validated = ExtractionEnvelopeValidator.Validate(T4ProcessingFixture.Envelope(payload: payload)).Value!;

        var result = new T4CaseProcessor(T4ProcessingFixture.Embedded).Process(validated);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(new T4SlipParser().Parse(payload).Errors.ToArray(), result.Errors.ToArray());
    }

    [Fact]
    public void SourceScanner_SeesEveryProductionFileAndStillMatchesAKnownPattern()
    {
        // Scanner guard: a broken walk or an over-eager comment filter would make
        // every scan above pass for free.
        var files = T4ProcessingFixture.ProductionSourceFiles();

        Assert.True(files.Length >= 5, $"Expected the module's production sources, found {files.Length}.");
        Assert.Contains(files, file => Path.GetFileName(file) == "T4CaseProcessor.cs");
        Assert.Contains(files, file => Path.GetFileName(file) == "PayloadStructureGuard.cs");

        var executable = files.SelectMany(T4ProcessingFixture.ExecutableLines).ToArray();

        Assert.True(executable.Length > 80, $"Implausibly few executable lines: {executable.Length}.");
        // Known-present patterns: if these stop matching, the scan is broken.
        Assert.Contains(executable, line => line.Contains("T4SlipParser", StringComparison.Ordinal));
        Assert.Contains(executable, line => line.Contains("T4LineMapper", StringComparison.Ordinal));
        Assert.Contains(executable, line => Regex.IsMatch(line, @"\.Value\b"));
    }

    /// <summary>
    /// A provider whose map is authored in the test, with destinations that are
    /// deliberately not CRA line numbers.
    /// </summary>
    private sealed class InMemoryMapProvider : IT4LineMapProvider
    {
        private readonly int _year;
        private readonly (string BoxCode, string[] Lines)[] _mappings;

        public InMemoryMapProvider(int year, params (string BoxCode, string[] Lines)[] mappings)
        {
            _year = year;
            _mappings = mappings;
        }

        public ParseResult<T4LineMap> TryGetForYear(int taxYear)
        {
            if (taxYear != _year)
            {
                return ParseResult<T4LineMap>.Failure(
                    new ValidationError("TaxYear", "stub: this provider holds another year's config."));
            }

            var config = new T4LineMappingConfig(
                _year,
                _mappings.Select(m => new T4BoxLineMapping(m.BoxCode, m.Lines)).ToArray());

            var map = T4LineMap.Create(config);
            Assert.True(map.IsSuccess, "The in-memory test map must be valid.");
            return map;
        }
    }
}

/// <summary>
/// The shared fixture for the three T04 end-to-end suites: envelope/payload
/// builders, the rejection families, and the comparison helpers. Kept in one
/// place so the acceptance, PII, and determinism suites exercise <b>identical</b>
/// pipelines — if they drifted apart, "no plant leaks" and "replays are equal"
/// would be claims about different things.
/// </summary>
internal static class T4ProcessingFixture
{
    internal const int TaxYear = 2025;

    /// <summary>
    /// The mandatory plant set. The two non-numeric markers are load-bearing: a
    /// nine-digit regex cannot catch an echoed document id.
    /// </summary>
    internal static readonly string[] Plants =
    {
        "123456789",
        "123-456-789",
        "123 456 789",
        "ZZMARKERZZ",
        "<<leak-me>>",
    };

    /// <summary>Every slot the matrix requires a plant in, one at a time.</summary>
    internal static readonly string[] PlantSlots =
    {
        "Box12Value",
        "OtherBoxValue",
        "EmployerName",
        "EmployerAddress",
        "EmployeeName",
        "EmployeeAddress",
        "DocumentId",
        "FirmId",
        "CaseId",
        "ParticipantId",
        "SourceReference",
    };

    /// <summary>Every way the pipeline can reject, plus the accepted run.</summary>
    internal static readonly string[] Pipelines =
    {
        "Accepted",
        "GenericEvidenceKind",
        "NullBoxes",
        "NullBoxElement",
        "BlankBoxCode",
        "DuplicateBox",
        "MissingBox12",
        "MalformedAmount",
        "NegativeAmount",
        "UnsupportedYear",
        "ProviderFailure",
    };

    internal static EmbeddedT4LineMapProvider Embedded { get; } = new();

    // Deliberately not nine consecutive digits: this suite asserts that no
    // nine-digit run reaches any output, and a default that contained one would
    // make a real leak indistinguishable from the fixture's own noise.
    private const string DefaultSin = "046454286";
    private const string DefaultAccountNumber = "12345678RP0001";

    internal static T4RawInput Payload(string slot = "", string plant = "", string defect = "")
    {
        string Slotted(string name, string fallback) =>
            string.Equals(slot, name, StringComparison.Ordinal) ? plant : fallback;

        var boxes = new List<T4RawBox>
        {
            new(T4BoxCodes.SocialInsuranceNumber, Slotted("Box12Value", DefaultSin)),
            new(T4BoxCodes.EmployerAccountNumber, DefaultAccountNumber),
            new(T4BoxCodes.DentalBenefits, "1"),
            // Box 50 is a scalar box: unconstrained and not amount-validated, so an
            // arbitrary planted string still reaches an accepted result.
            new(T4BoxCodes.RppDpspRegistrationNumber, Slotted("OtherBoxValue", "RPP-0001")),
            new(T4BoxCodes.EmploymentIncome, "52000.00"),
            new(T4BoxCodes.IncomeTaxDeducted, "8000.00"),
        };

        switch (defect)
        {
            case "NullBoxElement":
                boxes.Add(null!);
                break;
            case "BlankBoxCode":
                boxes.Add(new T4RawBox("   ", "x"));
                break;
            case "DuplicateBox":
                boxes.Add(new T4RawBox(T4BoxCodes.EmploymentIncome, "1.00"));
                break;
            case "MissingBox12":
                boxes.RemoveAll(box => box?.Code == T4BoxCodes.SocialInsuranceNumber);
                break;
            case "MalformedAmount":
                boxes.Add(new T4RawBox(T4BoxCodes.UnionDues, "not-a-number"));
                break;
            case "NegativeAmount":
                boxes.Add(new T4RawBox(T4BoxCodes.UnionDues, "-1.00"));
                break;
        }

        return new T4RawInput
        {
            Year = TaxYear,
            EmployerName = Slotted("EmployerName", "ACME CORP"),
            EmployerAddress = Slotted("EmployerAddress", "1 WAY ST, OTTAWA ON"),
            EmployeeName = Slotted("EmployeeName", "DOE, JANE"),
            EmployeeAddress = Slotted("EmployeeAddress", "2 HOME AVE, OTTAWA ON"),
            Boxes = string.Equals(defect, "NullBoxes", StringComparison.Ordinal) ? null! : boxes,
        };
    }

    internal static ExtractionEnvelope Envelope(
        string slot = "",
        string plant = "",
        T4RawInput? payload = null,
        ExtractedDocumentKind kind = ExtractedDocumentKind.T4Slip,
        int taxYear = TaxYear)
    {
        string Slotted(string name, string fallback) =>
            string.Equals(slot, name, StringComparison.Ordinal) ? plant : fallback;

        return new ExtractionEnvelope
        {
            DocumentId = Slotted("DocumentId", "doc-1"),
            Kind = kind,
            FirmId = Slotted("FirmId", "firm-1"),
            CaseId = Slotted("CaseId", "case-1"),
            ParticipantId = Slotted("ParticipantId", "participant-1"),
            TaxYear = taxYear,
            Provenance = new ExtractionProvenance
            {
                SourceReference = Slotted("SourceReference", "blob://source-1"),
                ProviderLabel = "test-provider",
            },
            T4Payload = kind == ExtractedDocumentKind.T4Slip ? payload ?? Payload(slot, plant) : null,
        };
    }

    /// <summary>
    /// Runs one pipeline end to end with a plant in one slot, returning the
    /// extraction that reached the processor (null when spec 007 refused to mint
    /// one) alongside the result.
    /// </summary>
    internal static (ValidatedExtraction? Extraction, ParseResult<ProcessedT4Extraction> Result) Run(
        string pipeline,
        string slot = "",
        string plant = "")
    {
        if (string.Equals(pipeline, "UnsupportedYear", StringComparison.Ordinal))
        {
            // Spec 007 will not mint an off-year extraction (AC-37), so this stage
            // is reached only through the internal constructor.
            var offYear = ExtractionWithYear(2024, Payload(slot, plant), slot, plant);
            return (offYear, new T4CaseProcessor(Embedded).Process(offYear));
        }

        var kind = string.Equals(pipeline, "GenericEvidenceKind", StringComparison.Ordinal)
            ? ExtractedDocumentKind.GenericEvidence
            : ExtractedDocumentKind.T4Slip;

        var defect = pipeline switch
        {
            "Accepted" or "GenericEvidenceKind" or "ProviderFailure" => "",
            _ => pipeline,
        };

        var envelope = Envelope(slot, plant, payload: kind == ExtractedDocumentKind.T4Slip ? Payload(slot, plant, defect) : null, kind: kind);
        var validated = ExtractionEnvelopeValidator.Validate(envelope);
        Assert.True(validated.IsSuccess, $"The {pipeline} envelope must clear spec 007's gate.");

        IT4LineMapProvider provider = string.Equals(pipeline, "ProviderFailure", StringComparison.Ordinal)
            ? new FailingProvider()
            : Embedded;

        return (validated.Value, new T4CaseProcessor(provider).Process(validated.Value));
    }

    internal static ValidatedExtraction ExtractionWithYear(int taxYear, T4RawInput payload, string slot = "", string plant = "")
    {
        string Slotted(string name, string fallback) =>
            string.Equals(slot, name, StringComparison.Ordinal) ? plant : fallback;

        var constructor = typeof(ValidatedExtraction)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();

        return (ValidatedExtraction)constructor.Invoke(new object?[]
        {
            Slotted("DocumentId", "doc-1"),
            ExtractedDocumentKind.T4Slip,
            Slotted("FirmId", "firm-1"),
            Slotted("CaseId", "case-1"),
            Slotted("ParticipantId", "participant-1"),
            taxYear,
            Slotted("SourceReference", "blob://source-1"),
            "test-provider",
            null,
            payload,
        });
    }

    /// <summary>
    /// The <b>omission</b> surfaces: every string this module can put in front of
    /// a caller where taxpayer data must not appear <i>in any form</i> — the
    /// result's own rendering, the supported direct JSON serialization, the
    /// mapping and its line entries, and every validation error. Not even a masked
    /// remnant belongs here.
    /// <para>
    /// The SIN-bearing renderings are deliberately <b>not</b> in this set; they are
    /// <see cref="MaskedSurfaces"/>, governed by the weaker rule that the engine's
    /// masking is the one permitted representation.
    /// </para>
    /// </summary>
    internal static IEnumerable<string> RenderedSurfaces(
        ValidatedExtraction? extraction,
        ParseResult<ProcessedT4Extraction> result)
    {
        if (extraction is not null)
        {
            yield return extraction.ToString();
        }

        foreach (var error in result.Errors)
        {
            yield return error.Field;
            yield return error.Message;
            yield return error.ToString();
        }

        var value = result.Value;
        if (value is null)
        {
            yield break;
        }

        yield return value.ToString();
        yield return JsonSerializer.Serialize(value);
        yield return value.Mapping.ToString();

        foreach (var line in value.Mapping.Lines)
        {
            yield return line.ToString();
            yield return line.LineNumber;

            foreach (var boxCode in line.SourceBoxCodes)
            {
                yield return boxCode;
            }
        }

        foreach (var unmapped in value.Mapping.UnmappedBoxCodes)
        {
            yield return unmapped;
        }
    }

    /// <summary>
    /// The renderings where the engine's masked <c>RecipientIdentifier</c> is the
    /// one permitted SIN representation. A planted value must still never appear
    /// here in full, and no nine-digit run may survive — but the masked last three
    /// characters are exactly what the contract intends.
    /// </summary>
    internal static IEnumerable<string> MaskedSurfaces(ParseResult<ProcessedT4Extraction> result)
    {
        var value = result.Value;
        if (value is null)
        {
            yield break;
        }

        yield return value.Slip.ToString();
        yield return value.Slip.Sin?.ToString() ?? string.Empty;
        yield return value.Slip.Sin?.Masked ?? string.Empty;
    }

    internal static void AssertMappingsEqual(T4MappingResult expected, T4MappingResult actual)
    {
        Assert.Equal(expected.Lines.Count, actual.Lines.Count);

        for (var index = 0; index < expected.Lines.Count; index++)
        {
            Assert.Equal(expected.Lines[index].LineNumber, actual.Lines[index].LineNumber, StringComparer.Ordinal);
            Assert.Equal(expected.Lines[index].Amount, actual.Lines[index].Amount);
            Assert.Equal(expected.Lines[index].SourceBoxCodes, actual.Lines[index].SourceBoxCodes);
        }

        Assert.Equal(expected.UnmappedBoxCodes, actual.UnmappedBoxCodes);
    }

    internal static void AssertSlipsEqual(T4Slip expected, T4Slip actual)
    {
        var properties = typeof(T4Slip).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.True(properties.Length > 20, $"Expected T4Slip's full property set, found {properties.Length}.");

        foreach (var property in properties)
        {
            var expectedValue = property.GetValue(expected);
            var actualValue = property.GetValue(actual);

            if (expectedValue is not string && expectedValue is System.Collections.IEnumerable expectedItems)
            {
                Assert.Equal(
                    expectedItems.Cast<object>().ToArray(),
                    ((System.Collections.IEnumerable)actualValue!).Cast<object>().ToArray());
            }
            else
            {
                Assert.Equal(expectedValue, actualValue);
            }
        }
    }

    internal static string[] ProductionSourceFiles() =>
        Directory.GetFiles(
                Path.Combine(FindRepositoryRoot(), "src", "CaseProcessing"), "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Source lines excluding comments. Documentation legitimately names forbidden
    /// concepts while explaining why they are forbidden.
    /// </summary>
    internal static IEnumerable<string> ExecutableLines(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the repository root containing TaxAI.sln.");
    }

    private sealed class FailingProvider : IT4LineMapProvider
    {
        public ParseResult<T4LineMap> TryGetForYear(int taxYear) =>
            ParseResult<T4LineMap>.Failure(
                new ValidationError("TaxYear", "stub: no config for that year."));
    }
}
