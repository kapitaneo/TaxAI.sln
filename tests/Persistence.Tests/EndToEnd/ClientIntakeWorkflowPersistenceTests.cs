using System.Data.Common;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClientIntake.Contracts;
using ClientIntake.Models;
using ClientIntake.Services;
using Microsoft.EntityFrameworkCore;
using Persistence.Entities;
using Persistence.Repositories;
using Persistence.Tests.Support;
using TaxCases.Models;
using Tenancy.Models;

namespace Persistence.Tests.EndToEnd;

/// <summary>
/// Spec 014 T04 — the cross-module proof that only becomes possible after T01,
/// T02, and T03 have merged: the <b>real</b> <c>ClientIntake</c> workflows driving
/// the <b>real</b> relational adapter, through the approved
/// <c>InternalsVisibleTo("Persistence.Tests")</c> seam, against a real relational
/// database with the real migration applied.
/// <para>
/// Nothing here is a stand-in for the store. <c>SqlIntakeStore</c>,
/// <c>SqlReviewRecordRepository</c>, <c>TaxAiDbContext</c>, and the intake
/// migration are production code; only the credential source, the content
/// adapter (whose real Development implementation is proved in the portal suite),
/// and the clock are supplied by the test, because randomness, key material, and
/// time are the workflow's only non-deterministic inputs.
/// </para>
/// <para>
/// Every taxpayer-shaped value in this file — display names, source bytes,
/// filenames, and the SIN-shaped probes — is assembled here at runtime and is
/// obviously synthetic. No probe is a real identifier.
/// </para>
/// </summary>
public sealed class ClientIntakeWorkflowPersistenceTests
{
    // Assembled at runtime, never checked in as a fixture value. This is the
    // shape AC-64 and AC-47 require to be absent from every structured surface.
    private static string SinProbe => string.Concat("046", "454", "286");

    private static string FormattedSinProbe =>
        string.Join(" ", "046", "454", "286");

    private static readonly Guid FirmOne =
        Guid.Parse("1a1a1a1a-1a1a-4a1a-8a1a-1a1a1a1a1a01");

    private static readonly Guid FirmTwo =
        Guid.Parse("2b2b2b2b-2b2b-4b2b-8b2b-2b2b2b2b2b02");

    private static readonly Guid AccountantActor =
        Guid.Parse("3c3c3c3c-3c3c-4c3c-8c3c-3c3c3c3c3c03");

    // ------------------------------------------------------------ happy path

    [Fact] // AC-1, AC-2, AC-3, AC-33, AC-52, AC-55
    public async Task TheCompleteFlowReachesReadyThroughTheRealRelationalAdapter()
    {
        await using var fixture = await IntakeFixture.CreateAsync();

        var created = await fixture.CreateCaseAsync(t4Count: 1, medicalCount: 1);
        Assert.Equal(IntakeResultCode.Success, created.Code);
        var caseId = created.Value!.CaseId;

        // The link is inspected read-only, then atomically redeemed once.
        var inspection = await fixture.Client.InspectLinkAsync(
            created.Value.LinkCredential,
            fixture.Now,
            CancellationToken.None);
        Assert.Equal(IntakeResultCode.Success, inspection.Code);
        Assert.Equal(2025, inspection.Value!.TaxYear);

        // Inspection spends nothing: the link is still redeemable.
        var session = await fixture.RedeemAsync(inspection.Value.Proof);
        Assert.Equal(IntakeResultCode.Success, session.Code);

        var answered = await fixture.AnswerAsync(
            session.Value!.SessionCredential,
            (DocumentCategory.T4Slip, AvailabilityAnswerCode.AvailableNow),
            (DocumentCategory.MedicalExpenseEvidence,
                AvailabilityAnswerCode.AvailableNow));
        Assert.Equal(IntakeResultCode.Success, answered.Code);

        var t4Upload = await fixture.UploadAsync(
            session.Value.SessionCredential,
            DocumentCategory.T4Slip,
            ordinal: 1,
            IntakeMediaKind.Pdf,
            SyntheticSource.Pdf(SinProbe));
        Assert.Equal(IntakeResultCode.Success, t4Upload.Code);

        var medicalUpload = await fixture.UploadAsync(
            session.Value.SessionCredential,
            DocumentCategory.MedicalExpenseEvidence,
            ordinal: 1,
            IntakeMediaKind.Png,
            SyntheticSource.Png(FormattedSinProbe));
        Assert.Equal(IntakeResultCode.Success, medicalUpload.Code);

        var submitted = await fixture.SubmitAsync(session.Value.SessionCredential);
        Assert.Equal(IntakeResultCode.Success, submitted.Code);
        Assert.Equal(1, submitted.Value!.LatestSubmissionVersion);
        Assert.False(submitted.Value.UnsubmittedChanges);
        Assert.All(
            submitted.Value.ExpectedDocuments,
            document => Assert.Equal(
                ExpectedDocumentStatus.Ready,
                document.Status));

        // Readiness is reconstructed from the latest immutable submission by the
        // existing TaxCase authority, never from a stored flag.
        var progress = await fixture.ProgressAsync(caseId);
        Assert.Equal(IntakeResultCode.Success, progress.Code);
        Assert.Equal(ReadinessState.Ready, progress.Value!.Readiness);
        Assert.Equal(2025, progress.Value.TaxYear);
        Assert.Equal(1, progress.Value.ExpectedT4Count);
        Assert.Equal(1, progress.Value.ExpectedMedicalExpenseCount);
        Assert.Equal(2, progress.Value.AnsweredQuestionCount);
        Assert.Equal(2, progress.Value.ExpectedQuestionCount);
        Assert.Equal(1, progress.Value.LatestSubmissionVersion);
        Assert.Equal(IntakeLinkState.Spent, progress.Value.LinkState);
        Assert.Equal(
            ReviewHandoffState.AwaitingReviewRecord,
            progress.Value.HandoffState);
        Assert.Null(progress.Value.ReviewRecordId);

        // There is no writable readiness column anywhere in the schema.
        await using var context = fixture.CreateContext();
        Assert.Empty(
            context.Model
                .GetEntityTypes()
                .Where(entity => entity.Name.Contains(
                    "Intake",
                    StringComparison.Ordinal))
                .SelectMany(entity => entity.GetProperties())
                .Where(property => property.Name.Contains(
                    "Readiness",
                    StringComparison.OrdinalIgnoreCase)));
    }

    // ------------------------------------------------- one-way credentials

    [Fact] // AC-13, AC-14
    public async Task TheRawLinkCredentialIsNeverPersistedInAnyForm()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var created = await fixture.CreateCaseAsync(t4Count: 1, medicalCount: 0);
        var raw = created.Value!.LinkCredential.Reveal();

        var stored = await fixture.EveryStoredValueAsync();

        Assert.NotEmpty(stored);
        Assert.DoesNotContain(raw, stored, StringComparer.Ordinal);
        Assert.All(
            stored,
            value => Assert.DoesNotContain(raw, value, StringComparison.Ordinal));

        // Only the keyed digest is stored, and it is a digest of that credential.
        await using var context = fixture.CreateContext();
        var link = Assert.Single(await context.Set<IntakeLinkEntity>().ToListAsync());
        Assert.Equal(32, link.CredentialDigest.Length);
        Assert.Equal(
            fixture.Credentials
                .Digest(created.Value.LinkCredential, IntakeTokenPurpose.BootstrapLink)
                .Bytes
                .ToArray(),
            link.CredentialDigest);
    }

    [Fact] // AC-14, AC-19
    public async Task ReissueMintsANewCredentialAndInvalidatesTheDerivedSession()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var created = await fixture.CreateCaseAsync(t4Count: 1, medicalCount: 0);
        var session = await fixture.RedeemAsync(
            (await fixture.Client.InspectLinkAsync(
                created.Value!.LinkCredential,
                fixture.Now,
                CancellationToken.None)).Value!.Proof);

        fixture.Advance(TimeSpan.FromMinutes(1));
        var reissued = await fixture.Accountant.ReissueLinkAsync(
            fixture.TrustedFirmOne,
            created.Value.CaseId,
            AccountantActor,
            fixture.Now,
            CancellationToken.None);

        Assert.Equal(IntakeResultCode.Success, reissued.Code);
        Assert.NotEqual(
            created.Value.LinkCredential.Reveal(),
            reissued.Value!.LinkCredential.Reveal());

        // The old cookie fails, and so does the old link.
        Assert.Equal(
            IntakeResultCode.NotFound,
            (await fixture.Client.GetAsync(
                session.Value!.SessionCredential,
                fixture.Now,
                CancellationToken.None)).Code);
        Assert.Equal(
            IntakeResultCode.NotFound,
            (await fixture.Client.InspectLinkAsync(
                created.Value.LinkCredential,
                fixture.Now,
                CancellationToken.None)).Code);

        // Immutable prior history is retained rather than deleted.
        await using var context = fixture.CreateContext();
        Assert.Equal(2, await context.Set<IntakeLinkEntity>().CountAsync());
        Assert.Single(
            await context.Set<IntakeLinkEntity>()
                .Where(link => link.RevokedAtUtc == null)
                .ToListAsync());
    }

    [Fact] // AC-17, AC-18
    public async Task ARevokedOrAlreadySpentLinkRefusesRedemptionIdentically()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var created = await fixture.CreateCaseAsync(t4Count: 1, medicalCount: 0);
        var proof = (await fixture.Client.InspectLinkAsync(
            created.Value!.LinkCredential,
            fixture.Now,
            CancellationToken.None)).Value!.Proof;

        Assert.Equal(IntakeResultCode.Success, (await fixture.RedeemAsync(proof)).Code);

        // A replay of the same proof loses, with the unknown-link code.
        var replay = await fixture.RedeemAsync(proof);
        Assert.Equal(IntakeResultCode.NotFound, replay.Code);

        var unknown = await fixture.RedeemAsync(
            new IntakeRedemptionProof(
                new IntakeTokenDigest(Enumerable.Repeat((byte)0x5a, 32).ToArray()),
                proof.LinkExpiresAtUtc));
        Assert.Equal(replay.Code, unknown.Code);

        // Exactly one session exists.
        await using var context = fixture.CreateContext();
        Assert.Single(await context.Set<IntakeClientSessionEntity>().ToListAsync());
    }

    // -------------------------------------------------- tenant isolation

    [Fact] // AC-57, AC-58, AC-59
    public async Task AForeignFirmSelectorIsIndistinguishableFromAMissingCase()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var created = await fixture.CreateCaseAsync(t4Count: 1, medicalCount: 0);

        var foreign = await fixture.Accountant.GetProgressAsync(
            fixture.TrustedFirmTwo,
            created.Value!.CaseId,
            fixture.Now,
            CancellationToken.None);
        var missing = await fixture.Accountant.GetProgressAsync(
            fixture.TrustedFirmTwo,
            Guid.NewGuid(),
            fixture.Now,
            CancellationToken.None);

        Assert.Equal(IntakeResultCode.NotFound, foreign.Code);
        Assert.Equal(foreign.Code, missing.Code);
        Assert.Null(foreign.Value);
        Assert.Null(missing.Value);

        // The firm-scoped read never returns a foreign row.
        await using var context = fixture.CreateContext();
        var store = new SqlIntakeStore(context);
        Assert.Null(await store.FindAsync(
            fixture.TrustedFirmTwo,
            created.Value.CaseId,
            CancellationToken.None));
        Assert.NotNull(await store.FindAsync(
            fixture.TrustedFirmOne,
            created.Value.CaseId,
            CancellationToken.None));
    }

    [Fact] // AC-58
    public async Task OnlyTheTwoDigestLookupsOmitATrustedFirmOrDerivedScope()
    {
        // The contract, not the implementation: every other member must require
        // a TrustedFirmId or a scope one of these two produced.
        var exempt = new[] { "FindLinkScopeAsync", "FindClientScopeAsync" };

        var firmOmitting = typeof(IIntakeStore)
            .GetMethods()
            .Where(method => !method.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(TrustedFirmId) ||
                parameter.ParameterType == typeof(IntakeClientScope) ||
                parameter.ParameterType == typeof(IntakeLinkScope) ||
                parameter.ParameterType == typeof(NewIntakeCase)))
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(exempt.OrderBy(name => name, StringComparer.Ordinal), firmOmitting);

        // And each of the two returns scope rather than accepting it.
        foreach (var name in exempt)
        {
            var method = typeof(IIntakeStore).GetMethod(name)!;
            Assert.DoesNotContain(
                method.GetParameters(),
                parameter => parameter.ParameterType == typeof(TrustedFirmId));
        }
    }

    // ---------------------------------------------- duplicates and scoping

    [Fact] // AC-41, AC-43
    public async Task IdenticalBytesDuplicateWithinACaseAndNeverCorrelateAcrossScopes()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var bytes = SyntheticSource.Pdf(SinProbe);

        var first = await fixture.OpenCaseAsync(fixture.TrustedFirmOne);
        var second = await fixture.OpenCaseAsync(fixture.TrustedFirmOne);
        var other = await fixture.OpenCaseAsync(fixture.TrustedFirmTwo);

        Assert.Equal(
            IntakeResultCode.Success,
            (await fixture.UploadAsync(
                first.SessionCredential,
                DocumentCategory.T4Slip,
                1,
                IntakeMediaKind.Pdf,
                bytes)).Code);

        // Same case, same bytes, different original filename: duplicate.
        Assert.Equal(
            IntakeResultCode.Duplicate,
            (await fixture.UploadAsync(
                first.SessionCredential,
                DocumentCategory.T4Slip,
                1,
                IntakeMediaKind.Pdf,
                bytes)).Code);

        // Another case in the same firm, and a case in another firm, both accept
        // the identical bytes: neither is told the other exists.
        Assert.Equal(
            IntakeResultCode.Success,
            (await fixture.UploadAsync(
                second.SessionCredential,
                DocumentCategory.T4Slip,
                1,
                IntakeMediaKind.Pdf,
                bytes)).Code);
        Assert.Equal(
            IntakeResultCode.Success,
            (await fixture.UploadAsync(
                other.SessionCredential,
                DocumentCategory.T4Slip,
                1,
                IntakeMediaKind.Pdf,
                bytes)).Code);

        // The three stored fingerprints of identical bytes are all different,
        // because the scope participates in the derivation.
        await using var context = fixture.CreateContext();
        var fingerprints = await context.Set<IntakeDocumentEntity>()
            .Select(document => document.Fingerprint)
            .ToListAsync();
        Assert.Equal(3, fingerprints.Count);
        Assert.Equal(
            3,
            fingerprints
                .Select(Convert.ToBase64String)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact] // AC-42
    public async Task DuplicateDetectionUsesContentRatherThanTheOriginalFilename()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var opened = await fixture.OpenCaseAsync(fixture.TrustedFirmOne);

        Assert.Equal(
            IntakeResultCode.Success,
            (await fixture.UploadAsync(
                opened.SessionCredential,
                DocumentCategory.T4Slip,
                1,
                IntakeMediaKind.Pdf,
                SyntheticSource.Pdf("first distinct body"))).Code);

        // Different bytes under a filename the workflow never sees at all.
        Assert.Equal(
            IntakeResultCode.Success,
            (await fixture.UploadAsync(
                opened.SessionCredential,
                DocumentCategory.T4Slip,
                1,
                IntakeMediaKind.Pdf,
                SyntheticSource.Pdf("second distinct body"))).Code);

        await using var context = fixture.CreateContext();
        Assert.Equal(2, await context.Set<IntakeDocumentEntity>().CountAsync());
    }

    // -------------------------------------------------------- immutability

    [Fact] // AC-28, AC-30, AC-56
    public async Task AnswerAndSubmissionHistoryIsAppendedRatherThanRewritten()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var opened = await fixture.OpenCaseAsync(fixture.TrustedFirmOne);

        await fixture.AnswerAsync(
            opened.SessionCredential,
            (DocumentCategory.T4Slip, AvailabilityAnswerCode.WillProvideLater));
        fixture.Advance(TimeSpan.FromMinutes(1));
        await fixture.UploadAsync(
            opened.SessionCredential,
            DocumentCategory.T4Slip,
            1,
            IntakeMediaKind.Jpeg,
            SyntheticSource.Jpeg("version one body"));
        var firstSubmission =
            await fixture.SubmitAsync(opened.SessionCredential);
        Assert.Equal(1, firstSubmission.Value!.LatestSubmissionVersion);

        await using (var beforeContext = fixture.CreateContext())
        {
            var versionOne = await beforeContext.Set<IntakeSubmissionEntity>()
                .SingleAsync(submission => submission.Version == 1);
            Assert.Equal(
                fixture.Now,
                versionOne.SubmittedAtUtc.ToUniversalTime());
        }

        // A changed answer appends version 2 and leaves version 1 alone.
        fixture.Advance(TimeSpan.FromMinutes(1));
        var changed = await fixture.AnswerAsync(
            opened.SessionCredential,
            (DocumentCategory.T4Slip, AvailabilityAnswerCode.AvailableNow));
        Assert.Equal(IntakeResultCode.Success, changed.Code);
        Assert.True(changed.Value!.UnsubmittedChanges);
        Assert.Equal(1, changed.Value.LatestSubmissionVersion);

        var secondSubmission =
            await fixture.SubmitAsync(opened.SessionCredential);
        Assert.Equal(2, secondSubmission.Value!.LatestSubmissionVersion);

        await using var context = fixture.CreateContext();
        var answers = await context.Set<IntakeAnswerVersionEntity>()
            .OrderBy(answer => answer.Version)
            .ToListAsync();
        Assert.Equal(2, answers.Count);
        Assert.Equal(
            AvailabilityAnswerCode.WillProvideLater,
            answers[0].Answer);
        Assert.Equal(AvailabilityAnswerCode.AvailableNow, answers[1].Answer);

        // Submission 1 still references answer version 1; submission 2
        // references version 2. Neither snapshot was rewritten.
        var references = await context.Set<IntakeSubmissionAnswerEntity>()
            .OrderBy(reference => reference.SubmissionVersion)
            .ToListAsync();
        Assert.Equal(2, references.Count);
        Assert.Equal(1, references[0].AnswerVersion);
        Assert.Equal(2, references[1].AnswerVersion);
    }

    [Fact] // AC-51, AC-53, AC-54
    public async Task AnUnavailableItemProjectsMissingAndKeepsTheCaseNotReady()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var opened = await fixture.OpenCaseAsync(
            fixture.TrustedFirmOne,
            t4Count: 1,
            medicalCount: 1);

        await fixture.AnswerAsync(
            opened.SessionCredential,
            (DocumentCategory.T4Slip, AvailabilityAnswerCode.AvailableNow),
            (DocumentCategory.MedicalExpenseEvidence,
                AvailabilityAnswerCode.Unavailable));
        await fixture.UploadAsync(
            opened.SessionCredential,
            DocumentCategory.T4Slip,
            1,
            IntakeMediaKind.Pdf,
            SyntheticSource.Pdf("partial submission body"));

        var submitted = await fixture.SubmitAsync(opened.SessionCredential);

        Assert.Equal(IntakeResultCode.Success, submitted.Code);
        Assert.Equal(
            ExpectedDocumentStatus.Ready,
            submitted.Value!.ExpectedDocuments
                .Single(item => item.Category == DocumentCategory.T4Slip)
                .Status);
        Assert.Equal(
            ExpectedDocumentStatus.Missing,
            submitted.Value.ExpectedDocuments
                .Single(item =>
                    item.Category == DocumentCategory.MedicalExpenseEvidence)
                .Status);

        var progress = await fixture.ProgressAsync(opened.CaseId);
        Assert.Equal(ReadinessState.NotReady, progress.Value!.Readiness);
    }

    // ------------------------------------------------------- review handoff

    [Fact] // AC-73, AC-75, AC-86
    public async Task OneExactExistingReviewRecordIsAssociatedOnceAndNeverReplaced()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var ready = await fixture.ReadyCaseAsync();

        // The review record is created independently, by the existing review
        // contract, before the intake workflow is asked to associate anything.
        var recordId = await fixture.SaveReviewRecordAsync(
            fixture.TrustedFirmOne,
            ready.CaseId);

        fixture.Advance(TimeSpan.FromMinutes(1));
        var associated = await fixture.Accountant.AssociateReviewAsync(
            fixture.TrustedFirmOne,
            ready.CaseId,
            recordId,
            AccountantActor,
            fixture.Now,
            CancellationToken.None);

        Assert.Equal(IntakeResultCode.Success, associated.Code);
        Assert.Equal(ReviewHandoffState.Available, associated.Value!.HandoffState);
        Assert.Equal(recordId, associated.Value.ReviewRecordId);

        // Replacement is refused by the store, not merely by the caller.
        var replacementId = await fixture.SaveReviewRecordAsync(
            fixture.TrustedFirmOne,
            ready.CaseId);
        var replaced = await fixture.Accountant.AssociateReviewAsync(
            fixture.TrustedFirmOne,
            ready.CaseId,
            replacementId,
            AccountantActor,
            fixture.Now,
            CancellationToken.None);

        Assert.Equal(IntakeResultCode.Conflict, replaced.Code);

        await using var context = fixture.CreateContext();
        var association = Assert.Single(
            await context.Set<IntakeReviewAssociationEntity>().ToListAsync());
        Assert.Equal(recordId, association.RecordId);
    }

    [Fact] // AC-74, AC-75
    public async Task AForeignOrWrongCaseReviewRecordIsRefusedAndAssociatesNothing()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var ready = await fixture.ReadyCaseAsync();

        // Same firm, but a record naming a different case.
        var wrongCase = await fixture.SaveReviewRecordAsync(
            fixture.TrustedFirmOne,
            Guid.NewGuid());
        // Correct case, but the record belongs to another firm.
        var foreignFirm = await fixture.SaveReviewRecordAsync(
            fixture.TrustedFirmTwo,
            ready.CaseId);

        foreach (var candidate in new[] { wrongCase, foreignFirm, Guid.NewGuid() })
        {
            var refused = await fixture.Accountant.AssociateReviewAsync(
                fixture.TrustedFirmOne,
                ready.CaseId,
                candidate,
                AccountantActor,
                fixture.Now,
                CancellationToken.None);

            Assert.Equal(IntakeResultCode.NotFound, refused.Code);
        }

        var blank = await fixture.Accountant.AssociateReviewAsync(
            fixture.TrustedFirmOne,
            ready.CaseId,
            Guid.Empty,
            AccountantActor,
            fixture.Now,
            CancellationToken.None);
        Assert.Equal(IntakeResultCode.Invalid, blank.Code);

        await using var context = fixture.CreateContext();
        Assert.Empty(await context.Set<IntakeReviewAssociationEntity>().ToListAsync());

        // No review record was manufactured by the intake workflow: exactly the
        // two this test saved through the review contract exist.
        Assert.Equal(2, await context.Set<PersistedReviewRecord>().CountAsync());
    }

    // ----------------------------------------------------- audit and no-SIN

    [Fact] // AC-69, AC-70
    public async Task TheAuditTrailIsAppendOnlyOrderedAndStructuralOnly()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        var ready = await fixture.ReadyCaseAsync();

        // An invalid public token appends no case-scoped event.
        await using (var beforeContext = fixture.CreateContext())
        {
            var before = await beforeContext.Set<IntakeAuditEventEntity>().CountAsync();
            var unknown = new IntakeTokenDigest(
                Enumerable.Repeat((byte)0x7f, 32).ToArray());
            Assert.Equal(
                IntakeResultCode.NotFound,
                (await fixture.RedeemAsync(
                    new IntakeRedemptionProof(
                        unknown,
                        fixture.Now.AddHours(1)))).Code);

            await using var afterContext = fixture.CreateContext();
            Assert.Equal(
                before,
                await afterContext.Set<IntakeAuditEventEntity>().CountAsync());
        }

        await using var context = fixture.CreateContext();
        var events = await context.Set<IntakeAuditEventEntity>()
            .OrderBy(audit => audit.Sequence)
            .ToListAsync();

        Assert.NotEmpty(events);
        Assert.Equal(
            events.Select(audit => audit.Sequence).OrderBy(sequence => sequence),
            events.Select(audit => audit.Sequence));
        Assert.Contains(events, audit => audit.Kind == IntakeAuditKind.CaseCreated);
        Assert.Contains(events, audit => audit.Kind == IntakeAuditKind.LinkRedeemed);
        Assert.Contains(events, audit => audit.Kind == IntakeAuditKind.UploadAccepted);
        Assert.Contains(events, audit => audit.Kind == IntakeAuditKind.SubmissionCreated);
        Assert.All(events, audit => Assert.Equal(ready.FirmId, audit.FirmId));

        // The audit entity declares no free-text, filename, digest, fingerprint,
        // amount, or content member at all.
        var audited = context.Model
            .FindEntityType(typeof(IntakeAuditEventEntity))!
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "ActorKind",
                "ActorReference",
                "CaseId",
                "EventId",
                "ExpectedItemId",
                "FirmId",
                "Kind",
                "OccurredAtUtc",
                "Sequence",
            },
            audited);
    }

    [Fact] // AC-40, AC-47, AC-64, AC-65, AC-66
    public async Task NoSinShapeOrFilenameReachesAnyStoredIntakeValue()
    {
        await using var fixture = await IntakeFixture.CreateAsync();

        // Every probe below is planted inside the opaque source bytes and in the
        // original filename the transport discards. Neither may surface.
        const string PlantedFilename = "T4-046 454 286-<script>.pdf";
        var ready = await fixture.ReadyCaseAsync(
            t4Body: SyntheticSource.Pdf(
                $"{SinProbe} {FormattedSinProbe} 046-454-286 ***-**-286 -1.005 1.005"),
            originalFilename: PlantedFilename);

        var stored = await fixture.EveryStoredValueAsync();
        Assert.NotEmpty(stored);

        foreach (var probe in new[]
                 {
                     SinProbe,
                     FormattedSinProbe,
                     "046-454-286",
                     "***-**-286",
                     "286",
                     PlantedFilename,
                     "T4-046",
                     ".pdf",
                     "<script>",
                     "-1.005",
                 })
        {
            Assert.All(
                stored,
                value => Assert.DoesNotContain(
                    probe,
                    value,
                    StringComparison.OrdinalIgnoreCase));
        }

        // The content store is the one place the bytes live, and it is explicitly
        // NOT claimed to be SIN-free. This asserts the probe is still in there,
        // so the scan above is proving omission rather than an empty store.
        Assert.Contains(
            fixture.Content.StagedBodies,
            body => Encoding.UTF8.GetString(body)
                .Contains(SinProbe, StringComparison.Ordinal));

        // And there is no way to read it back: the contract has no read member.
        Assert.Equal(
            new[] { "DeleteIfExistsAsync", "StageAsync" },
            typeof(IIntakeContentStore)
                .GetMethods()
                .Select(method => method.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        _ = ready;
    }

    [Fact] // AC-66
    public async Task NoIntakeColumnDeclaresASinOrMaskingConcept()
    {
        await using var fixture = await IntakeFixture.CreateAsync();
        await using var context = fixture.CreateContext();

        var intakeProperties = context.Model
            .GetEntityTypes()
            .Where(entity => entity.ClrType.Name.StartsWith(
                "Intake",
                StringComparison.Ordinal))
            .SelectMany(entity => entity.GetProperties())
            .Select(property => property.Name)
            .ToArray();

        Assert.NotEmpty(intakeProperties);

        foreach (var forbidden in new[]
                 {
                     "Sin",
                     "SocialInsurance",
                     "Masked",
                     "Redact",
                     "PartialSin",
                     "LastThree",
                     "LastFour",
                     "DateOfBirth",
                     "Address",
                     "Email",
                     "Phone",
                     "FileName",
                     "Filename",
                 })
        {
            Assert.DoesNotContain(
                intakeProperties,
                name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ----------------------------------------------- schema additivity (T04.7)

    [Fact] // AC-99, and T04's additive-schema check
    public async Task TheIntakeMigrationIsAdditiveAndLeavesTheReviewSchemaAlone()
    {
        // Applied to a real relational harness by CreateAsync, never to a
        // deployed database.
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();

        // The model matches the migrations: nothing was changed without one.
        // Checked against the SQL Server model the snapshot was generated from —
        // under SQLite the provider-specific differences are expected — and it
        // opens no connection.
        using (var sqlServerModel = RelationalTestContext.CreateSqlServerContext())
        {
            Assert.False(sqlServerModel.Database.HasPendingModelChanges());
        }

        var migrationSource = Path.Combine(
            RepositoryRootPath(),
            "src",
            "Persistence",
            "Migrations",
            "20260730000000_AddClientIntake.cs");
        Assert.True(File.Exists(migrationSource));
        var migration = File.ReadAllText(migrationSource);

        // Additive only: it creates tables and never alters, drops, or renames
        // an existing one, and never mutates data.
        foreach (var forbidden in new[]
                 {
                     "AlterColumn",
                     "AlterTable",
                     "DropColumn",
                     "RenameColumn",
                     "RenameTable",
                     "AddColumn",
                     "InsertData",
                     "UpdateData",
                     "DeleteData",
                 })
        {
            Assert.DoesNotContain(forbidden, migration, StringComparison.Ordinal);
        }

        // Every table it creates is an intake table.
        var created = Regex
            .Matches(migration, @"CreateTable\(\s*name:\s*""(?<name>[^""]+)""")
            .Select(match => match.Groups["name"].Value)
            .ToArray();
        Assert.NotEmpty(created);
        Assert.All(
            created,
            name => Assert.StartsWith("Intake", name, StringComparison.Ordinal));

        // And the existing review tables still exist, unaltered, alongside them.
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table';";
        var tables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        foreach (var preExisting in new[]
                 {
                     "Firms",
                     "PortalUsers",
                     "PersistedReviewRecords",
                     "PersistedSlips",
                 })
        {
            Assert.Contains(preExisting, tables);
        }

        Assert.All(
            created,
            name => Assert.Contains(name, tables));
    }

    private static string RepositoryRootPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find the repository root containing TaxAI.sln.");
    }

    // --------------------------------------------------------------- fixture

    private sealed class IntakeFixture : IAsyncDisposable
    {
        private readonly RelationalTestContext _database;
        private DateTimeOffset _now =
            new(new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc));

        private IntakeFixture(
            RelationalTestContext database,
            TestCredentialSource credentials,
            RecordingContentStore content)
        {
            _database = database;
            Credentials = credentials;
            Content = content;

            var store = new ScopedRelationalIntakeStore(database);
            var options = new IntakeWorkflowOptions(
                LinkLifetime: TimeSpan.FromHours(2),
                ClientSessionIdleLifetime: TimeSpan.FromMinutes(30),
                MaximumFileBytes: 1024 * 1024,
                MaximumFilesPerRequest: 1);

            Accountant = new AccountantIntakeWorkflow(
                store,
                credentials,
                new ScopedReviewRecordRepository(database),
                options);
            Client = new ClientIntakeWorkflow(
                store,
                content,
                credentials,
                options);
        }

        internal AccountantIntakeWorkflow Accountant { get; }
        internal ClientIntakeWorkflow Client { get; }
        internal TestCredentialSource Credentials { get; }
        internal RecordingContentStore Content { get; }
        internal DateTimeOffset Now => _now;

        internal TrustedFirmId TrustedFirmOne => TrustedFirmId.FromGuid(FirmOne);
        internal TrustedFirmId TrustedFirmTwo => TrustedFirmId.FromGuid(FirmTwo);

        internal static async Task<IntakeFixture> CreateAsync()
        {
            var database = await RelationalTestContext.CreateAsync();
            await using (var context = database.CreateContext())
            {
                context.Add(SyntheticFixtures.Firm(FirmOne, "Synthetic Firm One"));
                context.Add(SyntheticFixtures.Firm(FirmTwo, "Synthetic Firm Two"));
                await context.SaveChangesAsync();
            }

            return new IntakeFixture(
                database,
                new TestCredentialSource(Enumerable.Repeat((byte)0x31, 32).ToArray()),
                new RecordingContentStore(
                    Enumerable.Repeat((byte)0x32, 32).ToArray()));
        }

        internal TaxAiDbContext CreateContext() => _database.CreateContext();

        internal void Advance(TimeSpan delta) => _now = _now.Add(delta);

        internal Task<IntakeResult<CreatedIntakeCase>> CreateCaseAsync(
            int t4Count,
            int medicalCount,
            TrustedFirmId? firm = null) =>
            Accountant.CreateAsync(
                firm ?? TrustedFirmOne,
                AccountantActor,
                new CreateIntakeCaseCommand(
                    "Synthetic Intake Taxpayer",
                    2025,
                    t4Count,
                    medicalCount),
                _now,
                CancellationToken.None);

        internal Task<IntakeResult<RedeemedIntakeSession>> RedeemAsync(
            IntakeRedemptionProof proof) =>
            Client.RedeemLinkAsync(proof, _now, CancellationToken.None);

        internal Task<IntakeResult<AccountantIntakeView>> ProgressAsync(
            Guid caseId,
            TrustedFirmId? firm = null) =>
            Accountant.GetProgressAsync(
                firm ?? TrustedFirmOne,
                caseId,
                _now,
                CancellationToken.None);

        internal Task<IntakeResult<ClientIntakeView>> AnswerAsync(
            IntakeSecret session,
            params (DocumentCategory Category, AvailabilityAnswerCode Answer)[] answers) =>
            Client.SaveAnswersAsync(
                session,
                new SaveIntakeAnswersCommand(
                    answers
                        .Select(answer => new IntakeAnswerInput(
                            answer.Category,
                            answer.Answer))
                        .ToArray()),
                _now,
                CancellationToken.None);

        internal async Task<IntakeResult<ClientIntakeView>> UploadAsync(
            IntakeSecret session,
            DocumentCategory category,
            int ordinal,
            IntakeMediaKind kind,
            byte[] body,
            string? originalFilename = null)
        {
            // The original filename is deliberately accepted and immediately
            // dropped here, exactly as the transport does: it is never passed to
            // the workflow, so it cannot reach storage.
            _ = originalFilename;

            using var content = new MemoryStream(body, writable: false);
            return await Client.UploadAsync(
                session,
                new IntakeUploadCommand(category, ordinal, kind, body.LongLength),
                content,
                _now,
                CancellationToken.None);
        }

        internal Task<IntakeResult<ClientIntakeView>> SubmitAsync(IntakeSecret session) =>
            Client.SubmitAsync(session, _now, CancellationToken.None);

        internal async Task<OpenedCase> OpenCaseAsync(
            TrustedFirmId firm,
            int t4Count = 1,
            int medicalCount = 0)
        {
            var created = await CreateCaseAsync(t4Count, medicalCount, firm);
            Assert.Equal(IntakeResultCode.Success, created.Code);

            var inspection = await Client.InspectLinkAsync(
                created.Value!.LinkCredential,
                _now,
                CancellationToken.None);
            Assert.Equal(IntakeResultCode.Success, inspection.Code);

            var session = await RedeemAsync(inspection.Value!.Proof);
            Assert.Equal(IntakeResultCode.Success, session.Code);

            return new OpenedCase(
                firm.Value,
                created.Value.CaseId,
                session.Value!.SessionCredential);
        }

        internal async Task<OpenedCase> ReadyCaseAsync(
            byte[]? t4Body = null,
            string? originalFilename = null)
        {
            var opened = await OpenCaseAsync(TrustedFirmOne);
            await AnswerAsync(
                opened.SessionCredential,
                (DocumentCategory.T4Slip, AvailabilityAnswerCode.AvailableNow));
            Advance(TimeSpan.FromSeconds(30));
            var upload = await UploadAsync(
                opened.SessionCredential,
                DocumentCategory.T4Slip,
                1,
                IntakeMediaKind.Pdf,
                t4Body ?? SyntheticSource.Pdf("ready case body"),
                originalFilename);
            Assert.Equal(IntakeResultCode.Success, upload.Code);

            Advance(TimeSpan.FromSeconds(30));
            var submitted = await SubmitAsync(opened.SessionCredential);
            Assert.Equal(IntakeResultCode.Success, submitted.Code);

            var progress = await ProgressAsync(opened.CaseId);
            Assert.Equal(ReadinessState.Ready, progress.Value!.Readiness);
            return opened;
        }

        /// <summary>
        /// Saves one review record through the existing firm-scoped review
        /// contract, naming the supplied intake case. The intake workflow never
        /// creates one of these.
        /// </summary>
        internal async Task<Guid> SaveReviewRecordAsync(
            TrustedFirmId firm,
            Guid caseId)
        {
            await using var context = _database.CreateContext();
            var repository = new SqlReviewRecordRepository(context);
            var recordId = Guid.NewGuid();
            var saved = await repository.SaveAsync(
                firm,
                ReviewRecordFor(firm.Value, recordId, caseId),
                CancellationToken.None);

            Assert.True(saved.IsSuccess);
            return saved.Value!.RecordId;
        }

        /// <summary>
        /// Every value in every intake table, rendered as text. This is the scan
        /// AC-13 and AC-64 are checked against, and it deliberately reads the
        /// database rather than the model: a value that reached a column but not a
        /// mapped property would still be a leak.
        /// </summary>
        internal async Task<IReadOnlyList<string>> EveryStoredValueAsync()
        {
            await using var context = _database.CreateContext();
            var connection = context.Database.GetDbConnection();
            await context.Database.OpenConnectionAsync();

            var tables = new List<string>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT name FROM sqlite_master WHERE type = 'table' "
                    + "AND name LIKE 'Intake%';";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            Assert.NotEmpty(tables);

            var values = new List<string>();
            foreach (var table in tables)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT * FROM \"{table}\";";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    for (var index = 0; index < reader.FieldCount; index++)
                    {
                        values.Add(Render(reader, index));
                    }
                }
            }

            return values;
        }

        private static string Render(DbDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                return string.Empty;
            }

            var value = reader.GetValue(index);
            return value switch
            {
                byte[] bytes => string.Concat(
                    Convert.ToBase64String(bytes),
                    " ",
                    Encoding.UTF8.GetString(bytes)),
                _ => value.ToString() ?? string.Empty,
            };
        }

        public async ValueTask DisposeAsync() => await _database.DisposeAsync();
    }

    internal sealed record OpenedCase(
        Guid FirmId,
        Guid CaseId,
        IntakeSecret SessionCredential);

    /// <summary>
    /// Gives every store call its own context and its own
    /// <see cref="SqlIntakeStore"/>, the way the portal's scoped registration
    /// does. A single long-lived context would let EF's identity map answer a
    /// read that the database never saw.
    /// </summary>
    private sealed class ScopedRelationalIntakeStore : IIntakeStore
    {
        private readonly RelationalTestContext _database;

        internal ScopedRelationalIntakeStore(RelationalTestContext database) =>
            _database = database;

        private async Task<T> WithStoreAsync<T>(
            Func<SqlIntakeStore, Task<T>> operation)
        {
            await using var context = _database.CreateContext();
            return await operation(new SqlIntakeStore(context));
        }

        public Task<IntakeStoreWriteCode> CreateAsync(
            NewIntakeCase intakeCase,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.CreateAsync(intakeCase, cancellationToken));

        public Task<IntakeLinkScope?> FindLinkScopeAsync(
            IntakeTokenDigest linkDigest,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store =>
                store.FindLinkScopeAsync(linkDigest, nowUtc, cancellationToken));

        public Task<IntakeStoreWriteCode> RedeemLinkAsync(
            IntakeLinkScope linkScope,
            NewIntakeSession session,
            IntakeAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.RedeemLinkAsync(
                linkScope,
                session,
                auditEvent,
                cancellationToken));

        public Task<IntakeClientScope?> FindClientScopeAsync(
            IntakeTokenDigest sessionDigest,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store =>
                store.FindClientScopeAsync(sessionDigest, nowUtc, cancellationToken));

        public Task<StoredIntakeCase?> FindAsync(
            TrustedFirmId firm,
            Guid caseId,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.FindAsync(firm, caseId, cancellationToken));

        public Task<StoredIntakeCase?> FindForClientAsync(
            IntakeClientScope scope,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store =>
                store.FindForClientAsync(scope, cancellationToken));

        public Task<IntakeStoreWriteCode> ReissueLinkAsync(
            TrustedFirmId firm,
            Guid caseId,
            long expectedConcurrencyVersion,
            IntakeLinkRecord replacement,
            IReadOnlyList<IntakeAuditEventRecord> auditEvents,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.ReissueLinkAsync(
                firm,
                caseId,
                expectedConcurrencyVersion,
                replacement,
                auditEvents,
                cancellationToken));

        public Task<IntakeStoreWriteCode> RevokeLinkAsync(
            TrustedFirmId firm,
            Guid caseId,
            long expectedConcurrencyVersion,
            DateTimeOffset revokedAtUtc,
            IntakeAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.RevokeLinkAsync(
                firm,
                caseId,
                expectedConcurrencyVersion,
                revokedAtUtc,
                auditEvent,
                cancellationToken));

        public Task<IntakeStoreWriteCode> AppendAnswersAsync(
            IntakeClientScope scope,
            long expectedConcurrencyVersion,
            DateTimeOffset renewedIdleExpiresAtUtc,
            IReadOnlyList<IntakeAnswerVersionRecord> answers,
            IReadOnlyList<IntakeAuditEventRecord> auditEvents,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.AppendAnswersAsync(
                scope,
                expectedConcurrencyVersion,
                renewedIdleExpiresAtUtc,
                answers,
                auditEvents,
                cancellationToken));

        public Task<IntakeStoreWriteCode> AppendUploadAttemptAsync(
            IntakeClientScope scope,
            long expectedConcurrencyVersion,
            DateTimeOffset renewedIdleExpiresAtUtc,
            IntakeUploadAttemptRecord attempt,
            IntakeDocumentRecord? document,
            IntakeAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.AppendUploadAttemptAsync(
                scope,
                expectedConcurrencyVersion,
                renewedIdleExpiresAtUtc,
                attempt,
                document,
                auditEvent,
                cancellationToken));

        public Task<IntakeStoreWriteCode> AppendSubmissionAsync(
            IntakeClientScope scope,
            long expectedConcurrencyVersion,
            DateTimeOffset renewedIdleExpiresAtUtc,
            IntakeSubmissionRecord submission,
            IReadOnlyList<IntakeAuditEventRecord> auditEvents,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.AppendSubmissionAsync(
                scope,
                expectedConcurrencyVersion,
                renewedIdleExpiresAtUtc,
                submission,
                auditEvents,
                cancellationToken));

        public Task<IntakeStoreWriteCode> AssociateReviewAsync(
            TrustedFirmId firm,
            Guid caseId,
            long expectedConcurrencyVersion,
            IntakeReviewAssociationRecord association,
            IntakeAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.AssociateReviewAsync(
                firm,
                caseId,
                expectedConcurrencyVersion,
                association,
                auditEvent,
                cancellationToken));
    }

    /// <summary>The real review repository, one context per call.</summary>
    private sealed class ScopedReviewRecordRepository : Tenancy.Contracts.IReviewRecordRepository
    {
        private readonly RelationalTestContext _database;

        internal ScopedReviewRecordRepository(RelationalTestContext database) =>
            _database = database;

        public async Task<TaxLogicEngine.Models.ParseResult<PersistedReviewRecord>> SaveAsync(
            TrustedFirmId firm,
            PersistedReviewRecord record,
            CancellationToken cancellationToken)
        {
            // The intake workflow must never reach this. If it ever does, the
            // "no review manufacture" claim is false and this test must fail.
            await Task.CompletedTask;
            throw new InvalidOperationException(
                "The intake workflow attempted to save a review record.");
        }

        public async Task<PersistedReviewRecord?> FindAsync(
            TrustedFirmId firm,
            Guid recordId,
            CancellationToken cancellationToken)
        {
            await using var context = _database.CreateContext();
            return await new SqlReviewRecordRepository(context)
                .FindAsync(firm, recordId, cancellationToken);
        }
    }

    private sealed class TestCredentialSource : IIntakeCredentialSource
    {
        private readonly byte[] _key;

        internal TestCredentialSource(byte[] key) => _key = key;

        public Guid CreateIdentifier() => Guid.NewGuid();

        public IntakeSecret CreateCredential()
        {
            var encoded = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            Assert.True(IntakeSecret.TryParse(encoded, out var credential));
            return credential!;
        }

        public IntakeTokenDigest Digest(
            IntakeSecret credential,
            IntakeTokenPurpose purpose) =>
            new(HMACSHA256.HashData(
                _key,
                Encoding.UTF8.GetBytes(
                    string.Concat(
                        purpose.ToString(),
                        "\n",
                        credential.Reveal()))));
    }

    /// <summary>
    /// Holds staged bytes in memory under an opaque key, with a keyed
    /// firm/case-scoped whole-file fingerprint. It exposes the staged bodies to
    /// the test only so the no-SIN scan can prove the probe really was uploaded;
    /// the production contract still has no read member.
    /// </summary>
    private sealed class RecordingContentStore : IIntakeContentStore
    {
        private readonly byte[] _key;
        private readonly Dictionary<string, byte[]> _objects = [];

        internal RecordingContentStore(byte[] key) => _key = key;

        internal IReadOnlyList<byte[]> StagedBodies => _objects.Values.ToArray();

        public async Task<StagedIntakeContent> StageAsync(
            TrustedFirmId firm,
            Guid caseId,
            Stream content,
            long maximumBytes,
            CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            long total = 0;
            int read;
            while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
            {
                total += read;
                if (total > maximumBytes)
                {
                    throw new InvalidOperationException(
                        "The upload exceeded the configured maximum.");
                }

                buffer.Write(chunk, 0, read);
            }

            var key = $"intake/{Guid.NewGuid():N}";
            var bytes = buffer.ToArray();
            _objects[key] = bytes;

            using var fingerprint = new HMACSHA256(
                HMACSHA256.HashData(
                    _key,
                    Encoding.UTF8.GetBytes($"{firm.Value:D}\n{caseId:D}")));
            return new StagedIntakeContent(
                key,
                total,
                fingerprint.ComputeHash(bytes));
        }

        public Task DeleteIfExistsAsync(
            TrustedFirmId firm,
            Guid caseId,
            string contentKey,
            CancellationToken cancellationToken)
        {
            _objects.Remove(contentKey);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Signature-valid synthetic files, assembled at runtime. These are not real
    /// documents and carry no real identifier; the bodies exist only so the
    /// upload boundary has bytes with a correct magic prefix to stream.
    /// </summary>
    private static class SyntheticSource
    {
        internal static byte[] Pdf(string body) =>
            [.. "%PDF-1.7\n"u8.ToArray(), .. Encoding.UTF8.GetBytes(body)];

        internal static byte[] Png(string body) =>
        [
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
            .. Encoding.UTF8.GetBytes(body),
        ];

        internal static byte[] Jpeg(string body) =>
            [0xff, 0xd8, 0xff, .. Encoding.UTF8.GetBytes(body)];
    }

    // -------------------------------------------------------------- helpers

    private static PersistedReviewRecord ReviewRecordFor(
        Guid firmId,
        Guid recordId,
        Guid caseId)
    {
        var slipId = Guid.NewGuid();
        var lineEntryId = Guid.NewGuid();

        var sourceCode = Construct<PersistedSourceBoxCode>(
            Guid.NewGuid(),
            firmId,
            lineEntryId,
            0,
            "B_A");
        var lineEntry = Construct<PersistedLineEntry>(
            lineEntryId,
            firmId,
            slipId,
            0,
            "L_A",
            2.005m,
            Array.AsReadOnly<PersistedSourceBoxCode>([sourceCode]));
        var boxAmount = Construct<PersistedBoxAmount>(
            Guid.NewGuid(),
            firmId,
            slipId,
            0,
            "B_A",
            1.005m);
        var unmappedCode = Construct<PersistedUnmappedBoxCode>(
            Guid.NewGuid(),
            firmId,
            slipId,
            0,
            "B_B");
        var slip = Construct<PersistedSlip>(
            slipId,
            firmId,
            recordId,
            0,
            SlipTypeCodes.T4,
            Guid.NewGuid(),
            "Synthetic Intake Taxpayer",
            "Synthetic Employer",
            Array.AsReadOnly<PersistedBoxAmount>([boxAmount]),
            Array.AsReadOnly<PersistedLineEntry>([lineEntry]),
            Array.AsReadOnly<PersistedUnmappedBoxCode>([unmappedCode]));

        return Construct<PersistedReviewRecord>(
            recordId,
            firmId,
            caseId.ToString("D"),
            Guid.NewGuid().ToString("D"),
            2025,
            1,
            SyntheticFixtures.CreatedAtUtc,
            SyntheticFixtures.CreatedAtUtc,
            new DateTimeOffset(new DateTime(2035, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Array.AsReadOnly<PersistedSlip>([slip]));
    }

    private static T Construct<T>(params object?[] arguments)
    {
        var constructor = typeof(T)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == arguments.Length);

        return (T)constructor.Invoke(arguments);
    }
}
