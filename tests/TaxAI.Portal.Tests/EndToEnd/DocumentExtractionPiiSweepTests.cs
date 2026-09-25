using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using ExtractionWorkflow.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using TaxAI.Portal.Components.Pages;
using TaxAI.Portal.Tests.Identity;

namespace TaxAI.Portal.Tests.EndToEnd;

/// <summary>
/// Spec 015 T07 — planted-value capture sweep. The synthetic probes make a
/// disclosure unambiguous without ever using a real person's information.
/// Raw encrypted source bytes and the accountant-only medical evidence model
/// are the only intentionally permitted locations.
/// </summary>
public sealed class DocumentExtractionPiiSweepTests
{
    [Fact]
    public async Task PlantedValuesRemainConfinedToTheirExplicitlyAllowedSurfaces()
    {
        var logs = new ClientIntakeSecurityAcceptanceTests.RecordingLoggerProvider();
        var diagnostics = new ConcurrentQueue<string>();
        using var activityListener = StartActivityCapture(diagnostics);
        using var meterListener = StartMetricCapture(diagnostics);

        EventHandler<FirstChanceExceptionEventArgs> exceptionCapture = (_, args) =>
            diagnostics.Enqueue(args.Exception.ToString());
        AppDomain.CurrentDomain.FirstChanceException += exceptionCapture;

        try
        {
            await using var world = ExtractionPortalWorld.Create(
                builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
            var seeded = await world.SeedFirmOneAsync();
            using var accountant = world.Intake.FirmOne.CreateAnonymousClient();
            var disallowedHttp = new List<ExtractionHttpCapture>();
            var allowedMedicalHttp = new List<ExtractionHttpCapture>();

            // Capture the real authentication Set-Cookie header as well as
            // allowing the client's cookie container to replay it thereafter.
            var signIn = await ExtractionPortalHttp.GetAsync(
                accountant,
                PortalAuthorizationHarness.TestSignInPath);
            Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
            disallowedHttp.Add(signIn);

            disallowedHttp.Add(await ExtractionPortalHttp.GetAsync(
                accountant,
                $"/intake-cases/{seeded.CaseId:D}/extractions"));
            disallowedHttp.Add(await ExtractionPortalHttp.ExtractAsync(
                accountant,
                seeded.CaseId,
                seeded.FirstT4DocumentId));
            disallowedHttp.Add(await ExtractionPortalHttp.ExtractAsync(
                accountant,
                seeded.CaseId,
                seeded.SecondT4DocumentId));
            allowedMedicalHttp.Add(await ExtractionPortalHttp.ExtractAsync(
                accountant,
                seeded.CaseId,
                seeded.MedicalDocumentId));

            var duplicateOwner = (await world.Extractions.FindDocumentAsync(
                seeded.Firm,
                seeded.CaseId,
                seeded.SecondT4DocumentId,
                CancellationToken.None))!;
            var candidate = Assert.Single(duplicateOwner.DuplicateCandidates);
            disallowedHttp.Add(await ExtractionPortalHttp.PostReviewMutationAsync(
                accountant,
                seeded.CaseId,
                seeded.SecondT4DocumentId,
                $"/intake-cases/{seeded.CaseId:D}/extractions/duplicates/{candidate.CandidateId:D}",
                new Dictionary<string, string>
                {
                    ["resolution"] = nameof(DuplicateResolutionKind.DistinctDocuments),
                }));

            disallowedHttp.Add(await ExtractionPortalHttp.PostReviewMutationAsync(
                accountant,
                seeded.CaseId,
                seeded.FirstT4DocumentId,
                $"/intake-cases/{seeded.CaseId:D}/extractions/{seeded.FirstT4DocumentId:D}/corrections",
                new Dictionary<string, string>
                {
                    ["field"] = nameof(CorrectionField.T4BoxValue),
                    ["boxCode"] = "14",
                    ["correctedValue"] = "51000.00",
                }));

            foreach (var documentId in new[]
                     {
                         seeded.FirstT4DocumentId,
                         seeded.SecondT4DocumentId,
                     })
            {
                disallowedHttp.Add(await ExtractionPortalHttp.PostReviewMutationAsync(
                    accountant,
                    seeded.CaseId,
                    documentId,
                    $"/intake-cases/{seeded.CaseId:D}/extractions/{documentId:D}/accept",
                    new Dictionary<string, string>()));
            }

            allowedMedicalHttp.Add(await ExtractionPortalHttp.PostReviewMutationAsync(
                accountant,
                seeded.CaseId,
                seeded.MedicalDocumentId,
                $"/intake-cases/{seeded.CaseId:D}/extractions/{seeded.MedicalDocumentId:D}/accept",
                new Dictionary<string, string>()));

            disallowedHttp.Add(await ExtractionPortalHttp.FinalizeAsync(
                accountant,
                seeded.CaseId,
                seeded.FirstT4DocumentId));
            disallowedHttp.Add(await ExtractionPortalHttp.GetAsync(
                accountant,
                $"/intake-cases/{seeded.CaseId:D}"));
            disallowedHttp.Add(await ExtractionPortalHttp.GetAsync(accountant, "/health"));

            Assert.All(
                disallowedHttp.Concat(allowedMedicalHttp),
                capture => Assert.True(
                    capture.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent,
                    $"Unexpected {capture.StatusCode} from {capture.Url}."));

            var intakeCase = (await world.Intake.Store.FindAsync(
                seeded.Firm,
                seeded.CaseId,
                CancellationToken.None))!;
            var stored = (await world.Extractions.FindCaseAsync(
                seeded.Firm,
                seeded.CaseId,
                CancellationToken.None))!;
            var t4Document = stored.Documents.Single(
                document => document.DocumentId == seeded.FirstT4DocumentId);
            var medicalDocument = stored.Documents.Single(
                document => document.DocumentId == seeded.MedicalDocumentId);
            var t4Model = SerializeBrowserModel(
                intakeCase,
                seeded.FirstT4DocumentId,
                t4Document);
            var medicalModel = SerializeBrowserModel(
                intakeCase,
                seeded.MedicalDocumentId,
                medicalDocument);

            Assert.Contains(
                ExtractionPortalWorld.ProviderMarker,
                medicalModel,
                StringComparison.Ordinal);
            Assert.Contains(
                ExtractionPortalWorld.PatientMarker,
                medicalModel,
                StringComparison.Ordinal);
            Assert.Contains(
                ExtractionPortalWorld.ProviderMarker,
                string.Join("\n", allowedMedicalHttp.Select(capture => capture.Body)),
                StringComparison.Ordinal);
            Assert.Contains(
                ExtractionPortalWorld.PatientMarker,
                string.Join("\n", allowedMedicalHttp.Select(capture => capture.Body)),
                StringComparison.Ordinal);

            var extractionAudit = stored.AuditEvents.Select(audit => audit.ToString());
            var intakeAudit = world.Intake.Store
                .Audit(seeded.Firm.Value, seeded.CaseId)
                .Select(audit => audit.ToString());
            var reviewRecords = world.Reviews.All.Select(record => record.ToString());
            var disallowed = disallowedHttp.Select(capture => capture.All)
                // Only the medical BODY/model may contain approved names.
                // Its URL, cookies and other headers remain disallowed.
                .Concat(allowedMedicalHttp.Select(capture => capture.Transport))
                .Append(t4Model)
                .Concat(extractionAudit)
                .Concat(intakeAudit)
                .Concat(reviewRecords)
                .Concat(logs.Messages)
                .Concat(diagnostics)
                .ToArray();
            var everyStructuredSurface = disallowed
                .Concat(allowedMedicalHttp.Select(capture => capture.All))
                .Append(medicalModel)
                .ToArray();

            Assert.NotEmpty(disallowed);
            Assert.All(everyStructuredSurface, surface =>
            {
                Assert.DoesNotContain(
                    ExtractionPortalWorld.PlantedSin,
                    surface,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    ExtractionPortalWorld.SourceMarker,
                    surface,
                    StringComparison.Ordinal);
            });
            Assert.All(disallowed, surface =>
            {
                Assert.DoesNotContain(
                    ExtractionPortalWorld.ProviderMarker,
                    surface,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    ExtractionPortalWorld.PatientMarker,
                    surface,
                    StringComparison.Ordinal);
            });

            Assert.DoesNotContain(
                ExtractionPortalWorld.PlantedSin,
                string.Join("\n", t4Document.Versions.Select(version => version.ToString())),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                ExtractionPortalWorld.SourceMarker,
                string.Join("\n", medicalDocument.MedicalEvidenceVersions.Select(version => version.ToString())),
                StringComparison.Ordinal);
            Assert.Contains(
                medicalDocument.MedicalEvidenceVersions,
                version => version.ProviderDisplayName == ExtractionPortalWorld.ProviderMarker &&
                           version.PatientDisplayName == ExtractionPortalWorld.PatientMarker);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= exceptionCapture;
        }
    }

    private static string SerializeBrowserModel(
        ClientIntake.Models.StoredIntakeCase intakeCase,
        Guid documentId,
        StoredDocumentExtraction extraction)
    {
        var document = intakeCase.Documents.Single(item => item.DocumentId == documentId);
        var expected = intakeCase.ExpectedItems.Single(
            item => item.ExpectedItemId == document.ExpectedItemId);
        var endpointType = typeof(ExtractionListPageModel).Assembly.GetType(
            "TaxAI.Portal.ExtractionEndpoints",
            throwOnError: true)!;
        var projector = endpointType.GetMethod(
            "ProjectReview",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var model = projector.Invoke(null, [expected, extraction])!;
        return JsonSerializer.Serialize(model, model.GetType());
    }

    private static ActivityListener StartActivityCapture(
        ConcurrentQueue<string> diagnostics)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => diagnostics.Enqueue(string.Join(
                " ",
                activity.Source.Name,
                activity.DisplayName,
                activity.OperationName,
                string.Join(" ", activity.Tags.Select(tag => $"{tag.Key}={tag.Value}")),
                string.Join(" ", activity.Baggage.Select(tag => $"{tag.Key}={tag.Value}")))),
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static MeterListener StartMetricCapture(
        ConcurrentQueue<string> diagnostics)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                diagnostics.Enqueue($"{instrument.Meter.Name} {instrument.Name} {instrument.Unit}");
                meterListener.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            diagnostics.Enqueue($"{instrument.Name} {value} {FormatTags(tags)}"));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            diagnostics.Enqueue($"{instrument.Name} {value} {FormatTags(tags)}"));
        listener.Start();
        return listener;
    }

    private static string FormatTags(
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var values = new List<string>(tags.Length);
        foreach (var tag in tags)
        {
            values.Add($"{tag.Key}={tag.Value}");
        }
        return string.Join(" ", values);
    }
}
