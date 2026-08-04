using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExtractionWorkflow.Models;
using TaxLogicEngine.Models;

namespace ExtractionWorkflow.Tests.Ingest;

/// <summary>
/// Spec 015 T04 — a planted SIN reaches the engine and reaches nothing else, and
/// a planted source-byte marker reaches nothing at all.
/// <para>
/// A nine-consecutive-digit sweep is the assertion rather than an equality check
/// on the planted string. It catches a partial rendering, a rearranged one, and
/// a value nobody planted deliberately.
/// </para>
/// </summary>
public sealed class IngestPiiTests
{
    private static readonly Regex NineDigitRun =
        new(@"\d{9}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    [Fact]
    public async Task ThePlantedSinReachesNoStoredBoxValue()
    {
        var harness = IngestHarness.Create();

        await harness.ExtractAsync();

        var stored = Assert.Single(harness.Store.Versions);

        Assert.DoesNotContain(
            stored.BoxValues,
            box => box.Code == T4BoxCodes.SocialInsuranceNumber);

        Assert.All(
            stored.BoxValues,
            box =>
            {
                Assert.DoesNotMatch(NineDigitRun, box.Value ?? string.Empty);
                Assert.DoesNotMatch(NineDigitRun, box.Code);
            });
    }

    [Fact]
    public async Task ThePlantedSinReachesNoAuditRow()
    {
        var harness = IngestHarness.Create();

        await harness.ExtractAsync();

        Assert.All(
            harness.Store.AuditEvents,
            auditEvent =>
            {
                Assert.DoesNotMatch(NineDigitRun, auditEvent.ToString());
                Assert.DoesNotMatch(NineDigitRun, JsonSerializer.Serialize(auditEvent));
            });
    }

    [Fact]
    public async Task ThePlantedSinReachesNoProjectionOrOutcome()
    {
        var harness = IngestHarness.Create();

        var result = await harness.ExtractAsync();

        foreach (var rendered in new[]
                 {
                     result.ToString(),
                     result.Value!.ToString(),
                     result.Value.Document.ToString(),
                     JsonSerializer.Serialize(result.Value),
                     JsonSerializer.Serialize(result.Value.Document),
                     JsonSerializer.Serialize(result),
                 })
        {
            Assert.DoesNotMatch(NineDigitRun, rendered);
        }
    }

    [Fact]
    public async Task ThePlantedSinReachesNoStoredVersionRendering()
    {
        var harness = IngestHarness.Create();

        await harness.ExtractAsync();

        foreach (var version in harness.Store.Versions)
        {
            Assert.DoesNotMatch(NineDigitRun, version.ToString());
            Assert.DoesNotMatch(NineDigitRun, JsonSerializer.Serialize(version));
        }
    }

    [Fact]
    public async Task NoErrorTheWorkflowCanReturnCarriesAValue()
    {
        // Every structural refusal this module produces is a code, never a
        // message: there is no member on the result able to carry one.
        var harness = IngestHarness.Create();
        harness.Provider.ThrowOnExtract = true;

        var refused = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Invalid, refused.Code);
        Assert.DoesNotContain(
            typeof(ExtractionResult<DocumentExtractionOutcome>).GetProperties(),
            property => property.Name.Contains("Message", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Error", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotMatch(NineDigitRun, refused.ToString());
    }

    [Fact]
    public async Task EngineErrorsAreSurfacedVerbatimAndStillCarryNoSin()
    {
        var harness = IngestHarness.Create();
        harness.Provider.T4Result = IngestHarness.ValidT4Result(
            includeSocialInsuranceNumber: false);

        var result = await harness.ExtractAsync();

        Assert.NotEmpty(result.Value!.EngineErrors);

        // The engine's own Box 12 message, unchanged — and it never echoes a
        // value, which is why surfacing it verbatim is safe.
        Assert.Contains(result.Value.EngineErrors, error => error.Field == "Box 12");
        Assert.All(
            result.Value.EngineErrors,
            error =>
            {
                Assert.DoesNotMatch(NineDigitRun, error.Message);
                Assert.DoesNotMatch(NineDigitRun, error.Field);
            });
    }

    [Fact]
    public async Task ThePlantedSourceMarkerReachesNothingTheWorkflowProduces()
    {
        var harness = IngestHarness.Create(
            sourceBytes: Encoding.UTF8.GetBytes(IngestHarness.SourceMarker));

        var result = await harness.ExtractAsync();

        // The provider genuinely saw the bytes...
        Assert.True(harness.Provider.BytesSeen > 0);

        // ...and nothing that survives the call carries them.
        var captured = new List<string>
        {
            result.ToString(),
            result.Value!.ToString(),
            result.Value.Document.ToString(),
            JsonSerializer.Serialize(result.Value),
        };

        captured.AddRange(harness.Store.Versions.Select(version => version.ToString()));
        captured.AddRange(harness.Store.Versions.Select(version => JsonSerializer.Serialize(version)));
        captured.AddRange(harness.Store.AuditEvents.Select(auditEvent => auditEvent.ToString()));

        Assert.All(
            captured,
            text => Assert.DoesNotContain(
                IngestHarness.SourceMarker, text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EverySourceLeaseIsDisposedBeforeTheOperationReturns()
    {
        var harness = IngestHarness.Create();

        await harness.ExtractAsync();

        Assert.Equal(2, harness.Content.Leases.Count);
        Assert.All(harness.Content.Leases, lease => Assert.True(lease.IsDisposed));
        Assert.All(
            harness.Content.Leases,
            lease => Assert.Throws<ObjectDisposedException>(() => lease.Content));
    }

    [Fact]
    public async Task ProviderAndPatientNamesReachNoAuditRowOrOutcomeRendering()
    {
        const string providerMarker = "PROVIDER-MARKER-4C1A";
        const string patientMarker = "PATIENT-MARKER-7B3D";

        var harness = IngestHarness.Create();
        harness.Provider.Classification = DocumentClassification.MedicalExpenseEvidence;
        harness.Provider.MedicalResult = IngestHarness.ValidMedicalResult(
            providerName: providerMarker, patientName: patientMarker);

        var result = await harness.ExtractAsync();

        var captured = new List<string>
        {
            result.ToString(),
            result.Value!.ToString(),
            result.Value.Document.ToString(),
            JsonSerializer.Serialize(result.Value),
        };

        captured.AddRange(harness.Store.AuditEvents.Select(auditEvent => auditEvent.ToString()));
        captured.AddRange(
            harness.Store.MedicalVersions.Select(version => version.ToString()));
        captured.AddRange(harness.Store.MedicalVersions.Select(version => JsonSerializer.Serialize(version)));

        foreach (var marker in new[] { providerMarker, patientMarker })
        {
            Assert.All(
                captured,
                text => Assert.DoesNotContain(marker, text, StringComparison.Ordinal));
        }

        // They are still readable in memory on the accountant-facing model,
        // which is the one place they may ever appear.
        Assert.Equal(
            providerMarker,
            Assert.Single(harness.Store.MedicalVersions).ProviderDisplayName);
    }

    [Fact]
    public async Task TheRawPayloadNeverLeavesTheOperation()
    {
        var harness = IngestHarness.Create();

        var result = await harness.ExtractAsync();

        // Nothing the caller receives can carry a T4RawInput, and nothing stored
        // can either: the payload existed only inside the builder's
        // continuation.
        foreach (var type in new[]
                 {
                     typeof(DocumentExtractionOutcome),
                     typeof(DocumentExtractionView),
                     typeof(ExtractionVersion),
                 })
        {
            Assert.DoesNotContain(
                type.GetProperties(),
                property => property.PropertyType == typeof(T4RawInput));
        }

        Assert.Equal(ExtractionResultCode.Success, result.Code);
    }

    [Fact]
    public void NoWorkflowTypeDeclaresASinPropertyOrAPresenceSignal()
    {
        var offenders = typeof(ExtractionResultCode).Assembly
            .GetExportedTypes()
            .SelectMany(type => type
                .GetProperties()
                .Select(property => $"{type.Name}.{property.Name}"))
            .Where(name =>
                Regex.IsMatch(
                    name,
                    @"(?:(?<![A-Za-z])|(?<=[a-z0-9]))(?:Sin|SIN)(?![a-z])",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)) ||
                name.Contains("SocialInsurance", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("HasSin", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Masked", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(offenders);
    }
}
