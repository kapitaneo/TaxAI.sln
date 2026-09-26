using System.Net;
using ExtractionWorkflow.Models;

namespace TaxAI.Portal.Tests.EndToEnd;

/// <summary>
/// Spec 015 T07 — tenant refusal is proved at the HTTP boundary for every
/// extraction route. A resource which genuinely exists in another firm must be
/// indistinguishable from one which has never existed.
/// </summary>
public sealed class DocumentExtractionSecurityAcceptanceTests
{
    [Fact]
    public async Task EveryExtractionRouteReturnsTheSameEmptySafe404ForForeignAndMissing()
    {
        await using var world = ExtractionPortalWorld.Create();
        var owned = await world.SeedFirmOneAsync();
        var foreign = await world.SeedFirmTwoAsync();
        using var owner = world.Intake.FirmTwo.CreateAccountantClient();
        await world.Intake.FirmTwo.SignInAsync(owner);
        foreach (var documentId in new[] { foreign.FirstT4DocumentId, foreign.SecondT4DocumentId })
        {
            Assert.Equal(HttpStatusCode.OK, (await ExtractionPortalHttp.ExtractAsync(
                owner, foreign.CaseId, documentId)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await ExtractionPortalHttp.GetAsync(
                owner, $"/intake-cases/{foreign.CaseId:D}/extractions/{documentId:D}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await ExtractionPortalHttp.GetAsync(
            owner, $"/intake-cases/{foreign.CaseId:D}/extractions")).StatusCode);
        var before = (await world.Extractions.FindCaseAsync(
            foreign.Firm, foreign.CaseId, CancellationToken.None))!;
        var foreignCandidateId = Assert.Single(before.Documents.Single(
            document => document.DocumentId == foreign.SecondT4DocumentId)
            .DuplicateCandidates).CandidateId;

        using var accountant = world.Intake.FirmOne.CreateAccountantClient();
        await world.Intake.FirmOne.SignInAsync(accountant);

        var tokenPage = await ExtractionPortalHttp.GetAsync(
            accountant,
            $"/intake-cases/{owned.CaseId:D}/extractions/{owned.FirstT4DocumentId:D}");
        Assert.Equal(HttpStatusCode.OK, tokenPage.StatusCode);
        var token = IntakeFlow.AntiforgeryToken(tokenPage.Body);

        var missingCaseId = Guid.NewGuid();
        var missingDocumentId = Guid.NewGuid();
        var missingCandidateId = Guid.NewGuid();
        var probes = new[]
        {
            Probe.Get(
                "list",
                $"/intake-cases/{foreign.CaseId:D}/extractions",
                $"/intake-cases/{missingCaseId:D}/extractions"),
            Probe.Get(
                "review",
                $"/intake-cases/{foreign.CaseId:D}/extractions/{foreign.FirstT4DocumentId:D}",
                $"/intake-cases/{missingCaseId:D}/extractions/{missingDocumentId:D}"),
            Probe.Post(
                "extract",
                $"/intake-cases/{foreign.CaseId:D}/extractions/{foreign.FirstT4DocumentId:D}/extract",
                $"/intake-cases/{missingCaseId:D}/extractions/{missingDocumentId:D}/extract"),
            Probe.Post(
                "classification",
                $"/intake-cases/{foreign.CaseId:D}/extractions/{foreign.FirstT4DocumentId:D}/classification",
                $"/intake-cases/{missingCaseId:D}/extractions/{missingDocumentId:D}/classification",
                new Dictionary<string, string>
                {
                    ["targetClassification"] = nameof(DocumentClassification.T4Slip),
                }),
            Probe.Post(
                "correction",
                $"/intake-cases/{foreign.CaseId:D}/extractions/{foreign.FirstT4DocumentId:D}/corrections",
                $"/intake-cases/{missingCaseId:D}/extractions/{missingDocumentId:D}/corrections",
                new Dictionary<string, string>
                {
                    ["field"] = nameof(CorrectionField.T4BoxValue),
                    ["boxCode"] = "14",
                    ["correctedValue"] = "1.00",
                }),
            Probe.Post(
                "accept",
                $"/intake-cases/{foreign.CaseId:D}/extractions/{foreign.FirstT4DocumentId:D}/accept",
                $"/intake-cases/{missingCaseId:D}/extractions/{missingDocumentId:D}/accept"),
            Probe.Post(
                "duplicate",
                $"/intake-cases/{foreign.CaseId:D}/extractions/duplicates/{foreignCandidateId:D}",
                $"/intake-cases/{missingCaseId:D}/extractions/duplicates/{missingCandidateId:D}",
                new Dictionary<string, string>
                {
                    ["resolution"] = nameof(DuplicateResolutionKind.DistinctDocuments),
                }),
            Probe.Post(
                "finalize",
                $"/intake-cases/{foreign.CaseId:D}/extractions/finalize",
                $"/intake-cases/{missingCaseId:D}/extractions/finalize"),
        };

        foreach (var probe in probes)
        {
            var foreignResponse = await SendAsync(accountant, probe.ForeignPath, probe, token);
            var missingResponse = await SendAsync(accountant, probe.MissingPath, probe, token);

            Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
            Assert.Equal(foreignResponse.StatusCode, missingResponse.StatusCode);
            Assert.Equal(string.Empty, foreignResponse.Body);
            Assert.Equal(foreignResponse.Body, missingResponse.Body);
            Assert.Equal(foreignResponse.Headers, missingResponse.Headers);
        }

        Assert.Empty(world.Reviews.All);
        var foreignState = await world.Extractions.FindCaseAsync(
            foreign.Firm,
            foreign.CaseId,
            CancellationToken.None);
        Assert.NotNull(foreignState);
        Assert.Equal(before.AuditEvents, foreignState.AuditEvents);
        Assert.Equal(before.CaseAcceptances, foreignState.CaseAcceptances);
        Assert.Equal(before.Documents.Select(item => item.DocumentId),
            foreignState.Documents.Select(item => item.DocumentId));
        foreach (var original in before.Documents)
        {
            var unchanged = foreignState.Documents.Single(item => item.DocumentId == original.DocumentId);
            Assert.Equal(original.ParticipantId, unchanged.ParticipantId);
            Assert.Equal(original.Versions, unchanged.Versions);
            Assert.Equal(original.MedicalEvidenceVersions, unchanged.MedicalEvidenceVersions);
            Assert.Equal(original.Corrections, unchanged.Corrections);
            Assert.Equal(original.ClassificationOverrides, unchanged.ClassificationOverrides);
            Assert.Equal(original.DuplicateCandidates, unchanged.DuplicateCandidates);
            Assert.Equal(original.DuplicateResolutions, unchanged.DuplicateResolutions);
            Assert.Equal(original.Acceptances, unchanged.Acceptances);
        }
    }

    private static async Task<ExtractionHttpCapture> SendAsync(
        HttpClient client,
        string path,
        Probe probe,
        string token)
    {
        if (probe.Method == HttpMethod.Get)
        {
            return await ExtractionPortalHttp.GetAsync(client, path);
        }

        var values = probe.Values.ToDictionary(pair => pair.Key, pair => pair.Value);
        values["__RequestVerificationToken"] = token;
        using var response = await client.PostAsync(path, new FormUrlEncodedContent(values));
        return await ExtractionPortalHttp.CaptureAsync(response);
    }

    private sealed record Probe(
        string Name,
        HttpMethod Method,
        string ForeignPath,
        string MissingPath,
        IReadOnlyDictionary<string, string> Values)
    {
        internal static Probe Get(string name, string foreignPath, string missingPath) =>
            new(name, HttpMethod.Get, foreignPath, missingPath, new Dictionary<string, string>());

        internal static Probe Post(
            string name,
            string foreignPath,
            string missingPath,
            IReadOnlyDictionary<string, string>? values = null) =>
            new(
                name,
                HttpMethod.Post,
                foreignPath,
                missingPath,
                values ?? new Dictionary<string, string>());
    }
}
