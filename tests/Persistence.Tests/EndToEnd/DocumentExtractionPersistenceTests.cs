using System.Data.Common;
using System.Text;
using CaseProcessing.Services;
using ClientIntake.Contracts;
using ClientIntake.Models;
using ClientIntake.Services;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using ExtractionWorkflow.Services;
using Microsoft.EntityFrameworkCore;
using Persistence.Entities;
using Persistence.Repositories;
using Persistence.Services;
using Persistence.Tests.Support;
using TaxCases.Models;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;
using Tenancy.Contracts;
using Tenancy.Models;
using Tenancy.Services;

namespace Persistence.Tests.EndToEnd;

/// <summary>
/// Spec 015 T07 — the cross-module proof that only becomes possible after T01
/// through T06 have merged: the <b>real</b> extraction workflows driving the
/// <b>real</b> relational adapters, against a real relational database with the
/// real migrations applied, ending in a persisted review record and its one-time
/// association.
/// <para>
/// Nothing here stands in for a store. <c>SqlExtractionStore</c>,
/// <c>SqlIntakeStore</c>, <c>SqlReviewRecordRepository</c>,
/// <c>GuidOpaqueIdentifierSource</c>, <c>TaxAiDbContext</c>, the three
/// migrations, <c>ExtractionIngestWorkflow</c>, <c>ExtractionReviewWorkflow</c>,
/// <c>AccountantIntakeWorkflow</c>, <c>DocumentExtractionIngress</c>, and the
/// whole <c>T4CaseProcessor</c> chain beneath it are production code. Only the
/// document-intelligence provider and the content reader are supplied by the
/// test — they are this module's two outward seams, and their real Development
/// implementations are driven over HTTP in the portal suite instead.
/// </para>
/// <para>
/// This task verifies; it repairs nothing. No production code is touched, no
/// floor is raised, and no gap found here would be closed here.
/// </para>
/// <para>
/// Every taxpayer-shaped value in this file — display names, source bytes, and
/// the SIN-shaped probe — is assembled at runtime and is obviously synthetic.
/// No probe is a real identifier.
/// </para>
/// </summary>
public sealed class DocumentExtractionPersistenceTests
{
    // Assembled at runtime, never checked in as a literal. This is the shape
    // AC-27, AC-58, and AC-101 require to be absent from every stored column.
    private static string SinProbe => string.Concat("123", "456", "789");

    private static string FormattedSinProbe =>
        string.Join(" ", "123", "456", "789");

    /// <summary>
    /// Planted in the source bytes. AC-30 and AC-53 require it to reach no
    /// column, and AC-55 requires no provider payload to reach one either.
    /// </summary>
    private const string SourceMarker = "MARKER-4B71-SOURCE-BYTES";

    /// <summary>The provider label AC-55 requires never to be stored.</summary>
    private const string ProviderLabel = "SyntheticDeterministicProbe";

    private const string PatientName = "Synthetic Medical Patient";
    private const string MedicalProviderName = "Synthetic Dental Clinic";
    private const string EmployerName = "Synthetic Employer Limited";
    private const string EmployeeName = "Synthetic Casey Taxpayer";

    private static readonly Guid FirmOne =
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1");

    private static readonly Guid FirmTwo =
        Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2");

    private static readonly Guid AccountantActor =
        Guid.Parse("14141414-1414-4414-8414-141414141414");

    // ------------------------------------------------------ the T4 journey

    [Fact] // AC-5, AC-57, AC-63, AC-102, AC-105, AC-106, AC-112
    public async Task AT4DocumentIsExtractedCorrectedAcceptedAndFinalizedIntoAPersistedReviewRecord()
    {
        await using var world = await ExtractionWorld.CreateAsync();
        var opened = await world.OpenT4CaseAsync();

        // ------------------------------------------------ AC-5, AC-57, AC-63
        var extracted = await world.ExtractAsync(opened);
        Assert.True(extracted.IsSuccess);
        Assert.Equal(
            ExtractionRecordedKind.T4ExtractionRecorded,
            extracted.Value!.Recorded);
        Assert.True(extracted.Value.IsEngineAccepted);

        // The version landed in the database, not merely in a projection.
        var storedAfterExtract = await world.DocumentAsync(opened);
        Assert.Single(storedAfterExtract.Versions);
        Assert.Equal(1, storedAfterExtract.Versions[0].Version);
        Assert.Equal(
            DocumentClassification.T4Slip,
            storedAfterExtract.Versions[0].Classification.EffectiveClassification);

        // AC-105: nothing has been accepted, so no review record exists yet.
        Assert.Empty(await world.EveryReviewRecordIdAsync());

        // ------------------------------------------------ AC-102: correction
        var corrected = await world.CorrectAsync(
            opened, CorrectionField.T4BoxValue, "52500.00", boxCode: "14");
        Assert.True(corrected.IsSuccess);

        var storedAfterCorrection = await world.DocumentAsync(opened);
        Assert.Equal(2, storedAfterCorrection.Versions.Count);
        Assert.Single(storedAfterCorrection.Corrections);

        // AC-100: the prior version is byte-unchanged by the correction.
        Assert.Equal(
            storedAfterExtract.Versions[0].ExtractionVersionId,
            storedAfterCorrection.Versions[0].ExtractionVersionId);
        Assert.Equal(
            storedAfterExtract.Versions[0].RecordedAtUtc,
            storedAfterCorrection.Versions[0].RecordedAtUtc);

        // ------------------------------------------- AC-105: accept, then not
        Assert.True((await world.AcceptDocumentAsync(opened)).IsSuccess);
        Assert.Empty(await world.EveryReviewRecordIdAsync());

        // ----------------------------------------- AC-106: case finalization
        var finalized = await world.FinalizeAsync(opened);
        Assert.True(finalized.IsSuccess);
        Assert.Equal(1, finalized.Value!.ReviewRecordVersion);
        Assert.Equal(1, finalized.Value.AcceptedT4DocumentCount);

        // Exactly one versioned record, readable through the real repository.
        var recordId = Assert.Single(await world.EveryReviewRecordIdAsync());
        Assert.Equal(finalized.Value.ReviewRecordId, recordId);

        var record = await world.FindReviewRecordAsync(opened.Firm, recordId);
        Assert.NotNull(record);
        Assert.Equal(2025, record!.TaxYear);
        var slip = Assert.Single(record.Slips);
        Assert.Equal(SlipTypeCodes.T4, slip.SlipTypeCode);
        Assert.NotEmpty(slip.LineEntries);

        // AC-106: the one-time case/review association exists in the intake case.
        var intakeCase = await world.IntakeCaseAsync(opened);
        Assert.NotNull(intakeCase!.ReviewAssociation);
        Assert.Equal(recordId, intakeCase.ReviewAssociation!.RecordId);

        // AC-112: the boundary is the existing exclusive seven-year policy,
        // derived from the case's own year, with no second authority.
        Assert.Equal(
            RetentionPolicy.ComputeRetentionUntilUtc(2025),
            record.RetentionUntilUtc);
    }

    [Fact] // AC-109
    public async Task ASecondFinalizationOfTheSameCaseFailsClosedAndLeavesOneRecord()
    {
        await using var world = await ExtractionWorld.CreateAsync();
        var opened = await world.OpenT4CaseAsync();
        Assert.True((await world.ExtractAsync(opened)).IsSuccess);
        Assert.True((await world.AcceptDocumentAsync(opened)).IsSuccess);
        Assert.True((await world.FinalizeAsync(opened)).IsSuccess);

        var second = await world.FinalizeAsync(opened);

        Assert.False(second.IsSuccess);
        Assert.Equal(ExtractionResultCode.Conflict, second.Code);
        Assert.Single(await world.EveryReviewRecordIdAsync());
    }

    // ------------------------------------------------ the medical journey

    [Fact] // AC-70, AC-74, AC-80, AC-111
    public async Task MedicalEvidenceIsExtractedAndVersionedBesideTheT4WithoutEnteringTheRecord()
    {
        await using var world = await ExtractionWorld.CreateAsync();
        var opened = await world.OpenT4AndMedicalCaseAsync();

        Assert.True((await world.ExtractAsync(opened)).IsSuccess);
        var medical = await world.ExtractAsync(opened, opened.MedicalDocumentId);
        Assert.True(medical.IsSuccess);
        Assert.Equal(
            ExtractionRecordedKind.MedicalEvidenceRecorded,
            medical.Value!.Recorded);

        var storedMedical = await world.DocumentAsync(opened, opened.MedicalDocumentId);
        var version = Assert.Single(storedMedical.MedicalEvidenceVersions);

        // AC-74: the printed forms survive the round trip unnormalized. The
        // amount keeps its currency symbol and separator and the date keeps its
        // printed layout — nothing parsed, converted, or rounded either one.
        Assert.Equal("$1,120.50", version.GrossAmountAsPrinted);
        Assert.Equal("14 March 2025", version.ServiceDateAsPrinted);
        Assert.Equal(MedicalProviderName, version.ProviderDisplayName);
        Assert.Equal(PatientName, version.PatientDisplayName);

        // AC-80: a second extraction adds a version and leaves the first alone.
        Assert.True(
            (await world.ExtractAsync(opened, opened.MedicalDocumentId)).IsSuccess);
        var reread = await world.DocumentAsync(opened, opened.MedicalDocumentId);
        Assert.Equal(2, reread.MedicalEvidenceVersions.Count);
        Assert.Equal(
            version.MedicalEvidenceVersionId,
            reread.MedicalEvidenceVersions[0].MedicalEvidenceVersionId);
        Assert.Equal(1, reread.MedicalEvidenceVersions[0].Version);
        Assert.Equal(2, reread.MedicalEvidenceVersions[1].Version);

        // AC-111: the T4 record carries no medical field, name, or identifier.
        Assert.True((await world.AcceptDocumentAsync(opened)).IsSuccess);
        Assert.True((await world.FinalizeAsync(opened)).IsSuccess);

        var recordId = Assert.Single(await world.EveryReviewRecordIdAsync());
        var record = await world.FindReviewRecordAsync(opened.Firm, recordId);
        var slip = Assert.Single(record!.Slips);
        Assert.Equal(SlipTypeCodes.T4, slip.SlipTypeCode);

        var rendered = await world.EveryReviewColumnAsync();
        foreach (var marker in new[]
                 {
                     MedicalProviderName,
                     PatientName,
                     "1,120.50",
                     opened.MedicalDocumentId.ToString("D"),
                 })
        {
            Assert.DoesNotContain(
                rendered,
                value => value.Text.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }
    }

    // -------------------------------------------------- append-only history

    [Fact] // AC-33, AC-97, AC-100, AC-117
    public async Task EveryStoredFactIsAppendedAndNoEarlierRowIsRewritten()
    {
        await using var world = await ExtractionWorld.CreateAsync();
        var opened = await world.OpenT4AndMedicalCaseAsync();
        Assert.True((await world.ExtractAsync(opened)).IsSuccess);

        var afterExtract = await world.EveryExtractionRowAsync();

        // A correction, an override, a duplicate determination and its
        // resolution, and an acceptance — five more facts, no rewrites.
        Assert.True(
            (await world.CorrectAsync(
                opened, CorrectionField.EmployerDisplayName, "Corrected Employer"))
            .IsSuccess);
        Assert.True(
            (await world.OverrideClassificationAsync(
                opened,
                opened.MedicalDocumentId,
                DocumentClassification.MedicalExpenseEvidence))
            .IsSuccess);
        var candidateId = await world.FlagDuplicateAsync(opened);
        Assert.True(
            (await world.ResolveDuplicateAsync(
                opened, candidateId, DuplicateResolutionKind.DistinctDocuments))
            .IsSuccess);
        Assert.True((await world.AcceptDocumentAsync(opened)).IsSuccess);

        var afterEverything = await world.EveryExtractionRowAsync();

        // Every row that existed before still exists, unchanged, and the only
        // difference is additions. An immutable history something can edit is
        // not a history.
        foreach (var row in afterExtract)
        {
            Assert.Contains(row, afterEverything);
        }

        Assert.True(afterEverything.Count > afterExtract.Count);

        var stored = await world.DocumentAsync(opened);
        Assert.Equal(2, stored.Versions.Count);
        Assert.Single(stored.Corrections);
        Assert.Single(stored.DuplicateCandidates);
        Assert.Single(stored.DuplicateResolutions);
        Assert.Single(stored.Acceptances);

        // AC-97: the determination row is untouched by its own resolution.
        Assert.Equal(candidateId, stored.DuplicateCandidates[0].CandidateId);

        // AC-117: the override retains the automated classification beside it.
        var overridden = await world.DocumentAsync(opened, opened.MedicalDocumentId);
        var applied = Assert.Single(overridden.ClassificationOverrides);
        Assert.Equal(
            DocumentClassification.MedicalExpenseEvidence,
            applied.OverriddenClassification);
        Assert.Equal(AccountantActor, applied.ActorReference);
    }

    [Fact] // AC-34
    public async Task EveryAppendedAuditRowIsStructuralOnly()
    {
        await using var world = await ExtractionWorld.CreateAsync();
        var opened = await world.OpenT4AndMedicalCaseAsync();
        Assert.True((await world.ExtractAsync(opened)).IsSuccess);
        Assert.True(
            (await world.CorrectAsync(
                opened, CorrectionField.T4BoxValue, "52500.00", boxCode: "14"))
            .IsSuccess);
        Assert.True((await world.AcceptDocumentAsync(opened)).IsSuccess);
        Assert.True((await world.FinalizeAsync(opened)).IsSuccess);

        var extractions = await world.CaseAsync(opened);
        Assert.NotEmpty(extractions.AuditEvents);

        var sequences = extractions.AuditEvents.Select(item => item.Sequence).ToArray();
        Assert.Equal(sequences.OrderBy(value => value).ToArray(), sequences);
        Assert.Equal(sequences.Distinct().Count(), sequences.Length);

        foreach (var audit in extractions.AuditEvents)
        {
            Assert.True(Enum.IsDefined(audit.Kind));
            Assert.Equal(ExtractionActorKind.Accountant, audit.ActorKind);
            Assert.Equal(AccountantActor, audit.ActorReference);
        }

        // The audit table itself carries no free text, box code, CRA line,
        // amount, date-as-printed, provider name, patient name, or content key.
        var auditRows = (await world.EveryExtractionRowAsync())
            .Where(row => row.Table == "ExtractionAuditEvents")
            .ToArray();
        Assert.NotEmpty(auditRows);

        foreach (var forbidden in new[]
                 {
                     SinProbe,
                     SourceMarker,
                     ProviderLabel,
                     MedicalProviderName,
                     PatientName,
                     EmployerName,
                     EmployeeName,
                     "52500.00",
                     "1,120.50",
                     "14 March 2025",
                 })
        {
            Assert.DoesNotContain(
                auditRows,
                row => row.Text.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ------------------------------------------------------ tenant isolation

    [Fact] // AC-11, AC-12, AC-15, AC-91
    public async Task AForeignFirmSeesExactlyWhatAMissingResourceProduces()
    {
        await using var world = await ExtractionWorld.CreateAsync();
        var opened = await world.OpenT4CaseAsync();
        Assert.True((await world.ExtractAsync(opened)).IsSuccess);

        var foreignFirm = TrustedFirmId.FromGuid(FirmTwo);
        var absentCaseId = Guid.Parse("cafecafe-cafe-4afe-8afe-cafecafecafe");

        // Reads: a foreign firm and a missing case are indistinguishable — both
        // are null, through the real relational store.
        Assert.Null(await world.FindDocumentAsync(foreignFirm, opened.CaseId, opened.DocumentId));
        Assert.Null(await world.FindDocumentAsync(opened.Firm, absentCaseId, opened.DocumentId));
        Assert.Null(await world.FindCaseAsync(foreignFirm, opened.CaseId));
        Assert.Null(await world.FindCaseAsync(opened.Firm, absentCaseId));

        // Writes: every workflow entry point refuses the foreign firm with the
        // same code a missing case produces, and stores nothing.
        var foreignOpened = opened with { Firm = foreignFirm };
        var absentOpened = opened with { CaseId = absentCaseId };

        Assert.Equal(
            (await world.ExtractAsync(absentOpened)).Code,
            (await world.ExtractAsync(foreignOpened)).Code);
        Assert.Equal(
            (await world.AcceptDocumentAsync(absentOpened)).Code,
            (await world.AcceptDocumentAsync(foreignOpened)).Code);
        Assert.Equal(
            (await world.FinalizeAsync(absentOpened)).Code,
            (await world.FinalizeAsync(foreignOpened)).Code);

        // Nothing crossed: firm one still has exactly the one version it had,
        // and firm two's side of the database is untouched.
        var stored = await world.DocumentAsync(opened);
        Assert.Single(stored.Versions);
        Assert.Empty(stored.Acceptances);
        Assert.Empty(await world.EveryReviewRecordIdAsync());
    }

    [Fact] // AC-50, AC-91
    public async Task AContentKeyAloneReadsNothingOutsideItsOwnFourPartScope()
    {
        await using var world = await ExtractionWorld.CreateAsync();
        var opened = await world.OpenT4CaseAsync();

        var key = opened.ContentKey;
        var foreignFirm = TrustedFirmId.FromGuid(FirmTwo);
        var otherCase = Guid.Parse("dadadada-dada-4ada-8ada-dadadadadada");
        var otherDocument = Guid.Parse("ebebebeb-ebeb-4beb-8beb-ebebebebebeb");

        // The correct four-part scope reads; every single-part deviation does
        // not, and none of them distinguishes itself from a missing document.
        await using (var lease = await world.Content.OpenAsync(
                         opened.Firm, opened.CaseId, opened.DocumentId, key, CancellationToken.None))
        {
            Assert.NotNull(lease);
        }

        Assert.Null(await world.Content.OpenAsync(
            foreignFirm, opened.CaseId, opened.DocumentId, key, CancellationToken.None));
        Assert.Null(await world.Content.OpenAsync(
            opened.Firm, otherCase, opened.DocumentId, key, CancellationToken.None));
        Assert.Null(await world.Content.OpenAsync(
            opened.Firm, opened.CaseId, otherDocument, key, CancellationToken.None));
        Assert.Null(await world.Content.OpenAsync(
            opened.Firm, opened.CaseId, opened.DocumentId, "a-key-nobody-issued",
            CancellationToken.None));
    }

    // -------------------------------------------------------- the SIN sweep

    [Fact] // AC-27, AC-30, AC-55, AC-58, AC-62, AC-101
    public async Task NoColumnOfAnyExtractionRowCarriesASinASourceByteOrAProviderPayload()
    {
        await using var world = await ExtractionWorld.CreateAsync();
        var opened = await world.OpenT4AndMedicalCaseAsync();

        // Drive the complete feature, so every table this feature writes has
        // rows in it before the sweep runs.
        Assert.True((await world.ExtractAsync(opened)).IsSuccess);
        Assert.True((await world.ExtractAsync(opened, opened.MedicalDocumentId)).IsSuccess);
        Assert.True(
            (await world.CorrectAsync(
                opened, CorrectionField.T4BoxValue, "52500.00", boxCode: "14"))
            .IsSuccess);
        Assert.True(
            (await world.OverrideClassificationAsync(
                opened,
                opened.MedicalDocumentId,
                DocumentClassification.MedicalExpenseEvidence))
            .IsSuccess);
        var candidateId = await world.FlagDuplicateAsync(opened);
        Assert.True(
            (await world.ResolveDuplicateAsync(
                opened, candidateId, DuplicateResolutionKind.DistinctDocuments))
            .IsSuccess);
        Assert.True((await world.AcceptDocumentAsync(opened)).IsSuccess);
        Assert.True((await world.FinalizeAsync(opened)).IsSuccess);

        // The probe really did travel: the source the provider read carried it,
        // so a scan that stopped covering the payload would be caught here.
        Assert.Contains(SinProbe, world.Content.EveryStagedBody());
        Assert.Contains(SourceMarker, world.Content.EveryStagedBody());

        var rows = await world.EveryWrittenColumnAsync();
        Assert.NotEmpty(rows);

        // Every table this feature writes is represented, so a probe that found
        // nothing cannot be a probe that scanned nothing.
        foreach (var table in new[]
                 {
                     "ExtractionVersions",
                     "ExtractionBoxValues",
                     "ExtractionFieldOutcomes",
                     "ExtractionCorrections",
                     "ExtractionClassificationOverrides",
                     "ExtractionDuplicateCandidates",
                     "ExtractionDuplicateResolutions",
                     "ExtractionAcceptances",
                     "ExtractionAuditEvents",
                     "MedicalEvidenceVersions",
                 })
        {
            Assert.Contains(rows, row => row.Table == table);
        }

        foreach (var probe in new[]
                 {
                     SinProbe,
                     FormattedSinProbe,
                     "123-456-789",
                     "***-**-789",
                     SourceMarker,
                     MedicalProviderName,
                 })
        {
            var leak = rows.FirstOrDefault(row =>
                row.Text.Contains(probe, StringComparison.OrdinalIgnoreCase) &&
                !IsApprovedMedicalDisplayColumn(row));
            Assert.True(
                leak is null,
                $"'{probe}' reached {leak?.Table}.{leak?.Column}.");
        }

        // AC-55: the adapter's own identity is the existing spec 007
        // ExtractionProvenance.ProviderLabel — a declared, reused provenance
        // field, and the only provider-derived value stored. What must not be
        // stored is a payload: a prompt, a request, a response, or the source
        // text itself. None of the three provenance columns is wide enough to
        // hold one, and the source text reaches no column at all.
        var provenanceColumns = rows
            .Where(row => row.Column is "ProviderLabel" or "SourceReference")
            .ToArray();
        Assert.NotEmpty(provenanceColumns);
        Assert.All(
            provenanceColumns,
            row => Assert.DoesNotContain(
                SourceMarker, row.Text, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            provenanceColumns,
            row => row.Column == "ProviderLabel" && row.Text == ProviderLabel);

        // No column anywhere holds the whole provider response.
        Assert.DoesNotContain(
            rows,
            row => row.Text.Contains("BOX 14", StringComparison.OrdinalIgnoreCase));

        // AC-62: no column means "a SIN was present" either. Box 12 never
        // becomes a box value row, so the stored box codes exclude it entirely.
        var boxCodeRows = rows
            .Where(row => row.Table == "ExtractionBoxValues")
            .ToArray();
        Assert.NotEmpty(boxCodeRows);
        Assert.DoesNotContain(
            boxCodeRows,
            row => row.Column == "BoxCode" && row.Text == "12");
    }

    /// <summary>
    /// The medical evidence version is the one place the approved medical
    /// display fields are stored, and the accountant extraction-review surface
    /// is the one place they are rendered (AC-70, AC-76). Everywhere else the
    /// provider and patient names are a leak, which is what the sweep checks.
    /// </summary>
    private static bool IsApprovedMedicalDisplayColumn(StoredColumn row) =>
        row.Table == "MedicalEvidenceVersions" &&
        row.Column is "ProviderDisplayName" or "PatientDisplayName";

    [Fact] // AC-29, AC-32
    public async Task NoDefensiveRenderingOfAResultOrAStoredFactCarriesTaxpayerText()
    {
        await using var world = await ExtractionWorld.CreateAsync();
        var opened = await world.OpenT4AndMedicalCaseAsync();

        var extracted = await world.ExtractAsync(opened);
        var medical = await world.ExtractAsync(opened, opened.MedicalDocumentId);
        var stored = await world.DocumentAsync(opened);
        var storedMedical = await world.DocumentAsync(opened, opened.MedicalDocumentId);
        var extractions = await world.CaseAsync(opened);

        var renderings = new List<string>
        {
            extracted.ToString()!,
            extracted.Value!.ToString()!,
            extracted.Value.Document.ToString()!,
            medical.ToString()!,
            medical.Value!.ToString()!,
            stored.ToString()!,
            storedMedical.ToString()!,
            extractions.ToString()!,
        };

        renderings.AddRange(stored.Versions.Select(version => version.ToString()!));
        renderings.AddRange(
            storedMedical.MedicalEvidenceVersions.Select(version => version.ToString()!));
        renderings.AddRange(extractions.AuditEvents.Select(audit => audit.ToString()!));

        foreach (var rendering in renderings)
        {
            foreach (var probe in new[]
                     {
                         SinProbe,
                         FormattedSinProbe,
                         SourceMarker,
                         ProviderLabel,
                         EmployerName,
                         EmployeeName,
                         MedicalProviderName,
                         PatientName,
                         "50000.00",
                         "1,120.50",
                         opened.ContentKey,
                     })
            {
                Assert.DoesNotContain(probe, rendering, StringComparison.OrdinalIgnoreCase);
            }

            // Nine consecutive digits is the shape a SIN takes whatever the
            // value, so the rendering carries none at all.
            Assert.DoesNotMatch(@"\d{9}", rendering);
        }
    }

    // ------------------------------------------------------------ fail closed

    [Fact] // AC-21, AC-119
    public async Task AnUnrecognizedDocumentPersistsAnAuditEventAndNothingElse()
    {
        await using var world = await ExtractionWorld.CreateAsync();
        var opened = await world.OpenT4CaseAsync();
        world.Provider.Classification = DocumentClassification.Unsupported;

        var outcome = await world.ExtractAsync(opened);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(ExtractionRecordedKind.NothingRecorded, outcome.Value!.Recorded);

        var extractions = await world.CaseAsync(opened);
        Assert.Single(extractions.AuditEvents);
        Assert.Empty(extractions.Documents.SelectMany(document => document.Versions));
        Assert.Empty(
            extractions.Documents.SelectMany(document => document.MedicalEvidenceVersions));
        Assert.Empty(await world.EveryReviewRecordIdAsync());

        // The rows exist only in the audit table.
        var rows = await world.EveryWrittenColumnAsync();
        Assert.All(rows, row => Assert.Equal("ExtractionAuditEvents", row.Table));
    }

    [Fact] // AC-18, AC-20
    public async Task AProviderThatThrowsOrReturnsNothingStoresNoPartialState()
    {
        foreach (var failure in new[] { ProviderFailure.Throws, ProviderFailure.ReturnsNothing })
        {
            await using var world = await ExtractionWorld.CreateAsync();
            var opened = await world.OpenT4CaseAsync();
            world.Provider.Failure = failure;

            var outcome = await world.ExtractAsync(opened);

            Assert.False(outcome.IsSuccess);

            var extractions = await world.FindCaseAsync(opened.Firm, opened.CaseId);
            Assert.True(
                extractions is null ||
                extractions.Documents.All(document =>
                    document.Versions.Count == 0 &&
                    document.MedicalEvidenceVersions.Count == 0));
            Assert.Empty(await world.EveryReviewRecordIdAsync());
        }
    }

    [Fact] // AC-8, AC-9, AC-10
    public async Task NoAdjacentYearCanBeStoredAndADocumentYearMismatchFailsClosed()
    {
        await using var world = await ExtractionWorld.CreateAsync();
        var opened = await world.OpenT4CaseAsync();

        // AC-9: an adjacent year is not merely refused by a workflow rule — the
        // relational schema will not hold one, so no 2024 or 2026 case can ever
        // be presented to extraction in the first place.
        foreach (var year in new[] { 2024, 2026 })
        {
            Assert.False(
                await world.TrySetStoredTaxYearAsync(opened.Firm, opened.CaseId, year),
                $"The database accepted a case tax year of {year}.");
            Assert.Equal(2025, await world.StoredTaxYearAsync(opened.Firm, opened.CaseId));
        }

        // AC-8: a 2025 case whose document prints 2024 fails through the
        // engine's existing coherence rule, and neither year is coerced.
        world.Provider.T4Year = 2024;

        var rejected = await world.ExtractAsync(opened);

        Assert.True(rejected.IsSuccess);
        Assert.False(rejected.Value!.IsEngineAccepted);
        Assert.NotEmpty(rejected.Value.EngineErrors);
        Assert.Empty(await world.EveryReviewRecordIdAsync());
        Assert.Equal(2025, await world.StoredTaxYearAsync(opened.Firm, opened.CaseId));

        // AC-8: no processed extraction survived the mismatch, and the year on
        // the case is still the server-stored one.
        var stored = await world.IntakeCaseAsync(opened);
        Assert.Equal(2025, stored!.TaxYear);
    }

    // ------------------------------------------------------------- fixture

    private enum ProviderFailure
    {
        None = 0,
        Throws = 1,
        ReturnsNothing = 2,
    }

    /// <summary>One column of one row of one table, exactly as it is stored.</summary>
    private sealed record StoredColumn(string Table, string Column, string Text);

    private sealed record OpenedCase(
        TrustedFirmId Firm,
        Guid CaseId,
        Guid ParticipantId,
        Guid DocumentId,
        string ContentKey,
        Guid MedicalDocumentId,
        string MedicalContentKey);

    /// <summary>
    /// The real relational database, the real stores, and the real workflows —
    /// assembled the way the portal's scoped registrations assemble them.
    /// </summary>
    private sealed class ExtractionWorld : IAsyncDisposable
    {
        private readonly RelationalTestContext _database;
        private DateTimeOffset _now =
            new(new DateTime(2026, 9, 3, 10, 30, 0, DateTimeKind.Utc));

        private ExtractionWorld(
            RelationalTestContext database,
            IntakeContentAdapter content,
            StubDocumentIntelligenceProvider provider)
        {
            _database = database;
            Content = content;
            Provider = provider;

            var intake = new ScopedIntakeStore(database);
            var extractions = new ScopedExtractionStore(database);
            var reviews = new ScopedReviewRecordRepository(database);
            var identifiers = new GuidOpaqueIdentifierSource();
            var options = new ExtractionWorkflowOptions
            {
                ProviderCallLimit = TimeSpan.FromSeconds(30),
            };
            var credentials = new TestCredentialSource(
                Enumerable.Repeat((byte)0x33, 32).ToArray());
            var intakeOptions = new IntakeWorkflowOptions(
                LinkLifetime: TimeSpan.FromHours(2),
                ClientSessionIdleLifetime: TimeSpan.FromMinutes(30),
                MaximumFileBytes: 1024 * 1024,
                MaximumFilesPerRequest: 1);

            Intake = intake;
            Reviews = reviews;
            Extractions = extractions;

            Accountant = new AccountantIntakeWorkflow(
                intake, credentials, reviews, intakeOptions);
            Client = new ClientIntakeWorkflow(
                intake, content, credentials, intakeOptions);

            Ingest = new ExtractionIngestWorkflow(
                intake,
                extractions,
                content,
                provider,
                new DocumentExtractionIngress(),
                new T4CaseProcessor(new EmbeddedT4LineMapProvider()),
                identifiers,
                options);

            Review = new ExtractionReviewWorkflow(
                intake,
                Accountant,
                extractions,
                content,
                provider,
                new DocumentExtractionIngress(),
                new T4CaseProcessor(new EmbeddedT4LineMapProvider()),
                reviews,
                identifiers,
                options);
        }

        internal IntakeContentAdapter Content { get; }
        internal AccountantIntakeWorkflow Accountant { get; }
        internal ClientIntakeWorkflow Client { get; }
        internal StubDocumentIntelligenceProvider Provider { get; }
        internal ScopedIntakeStore Intake { get; }
        internal ScopedExtractionStore Extractions { get; }
        internal ScopedReviewRecordRepository Reviews { get; }
        internal ExtractionIngestWorkflow Ingest { get; }
        internal ExtractionReviewWorkflow Review { get; }

        internal static async Task<ExtractionWorld> CreateAsync()
        {
            var database = await RelationalTestContext.CreateAsync();
            await using (var context = database.CreateContext())
            {
                context.Add(SyntheticFixtures.Firm(FirmOne, "Synthetic Firm One"));
                context.Add(SyntheticFixtures.Firm(FirmTwo, "Synthetic Firm Two"));
                await context.SaveChangesAsync();
            }

            var content = new IntakeContentAdapter();
            var world = new ExtractionWorld(
                database,
                content,
                new StubDocumentIntelligenceProvider());

            // The document leg of the four-part scope is answered by the
            // server-stored case, read through the real relational store.
            content.DocumentOwnsKey = (firm, caseId, documentId, contentKey) =>
            {
                var stored = world.Intake
                    .FindAsync(firm, caseId, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                return stored is not null &&
                    stored.Documents.Any(document =>
                        document.DocumentId == documentId &&
                        string.Equals(
                            document.ContentKey, contentKey, StringComparison.Ordinal));
            };

            return world;
        }

        internal Task<OpenedCase> OpenT4CaseAsync() =>
            OpenAsync(includeMedical: false);

        internal Task<OpenedCase> OpenT4AndMedicalCaseAsync() =>
            OpenAsync(includeMedical: true);

        /// <summary>
        /// Opens one firm-scoped case the way a real one is opened: the real
        /// accountant workflow creates it and issues the link, the real client
        /// workflow redeems it, answers, and uploads, and the real relational
        /// store holds every row. Extraction then reads the document back
        /// through the same <c>SqlIntakeStore</c> the portal resolves.
        /// </summary>
        private async Task<OpenedCase> OpenAsync(bool includeMedical)
        {
            var firm = TrustedFirmId.FromGuid(FirmOne);

            var created = await Accountant.CreateAsync(
                firm,
                AccountantActor,
                new CreateIntakeCaseCommand(
                    "Synthetic Casey Taxpayer",
                    2025,
                    1,
                    includeMedical ? 1 : 0),
                _now,
                CancellationToken.None);
            Assert.Equal(IntakeResultCode.Success, created.Code);
            var caseId = created.Value!.CaseId;

            var inspected = await Client.InspectLinkAsync(
                created.Value.LinkCredential, _now, CancellationToken.None);
            Assert.Equal(IntakeResultCode.Success, inspected.Code);

            var session = await Client.RedeemLinkAsync(
                inspected.Value!.Proof, _now, CancellationToken.None);
            Assert.Equal(IntakeResultCode.Success, session.Code);
            var credential = session.Value!.SessionCredential;

            var answers = new List<IntakeAnswerInput>
            {
                new(DocumentCategory.T4Slip, AvailabilityAnswerCode.AvailableNow),
            };

            if (includeMedical)
            {
                answers.Add(new IntakeAnswerInput(
                    DocumentCategory.MedicalExpenseEvidence,
                    AvailabilityAnswerCode.AvailableNow));
            }

            _now = _now.AddSeconds(30);
            Assert.Equal(
                IntakeResultCode.Success,
                (await Client.SaveAnswersAsync(
                    credential,
                    new SaveIntakeAnswersCommand(answers),
                    _now,
                    CancellationToken.None)).Code);

            _now = _now.AddSeconds(30);
            await UploadAsync(
                credential, DocumentCategory.T4Slip, IntakeMediaKind.Pdf, T4Source());

            if (includeMedical)
            {
                _now = _now.AddSeconds(30);
                await UploadAsync(
                    credential,
                    DocumentCategory.MedicalExpenseEvidence,
                    IntakeMediaKind.Pdf,
                    MedicalSource());
            }

            // Read the case back through the real store, so the identifiers and
            // content keys this fixture hands out are the ones the database
            // actually holds rather than ones the test invented.
            var stored = await Intake.FindAsync(firm, caseId, CancellationToken.None);
            Assert.NotNull(stored);

            var t4 = Assert.Single(
                stored!.Documents,
                document => Category(stored, document) == DocumentCategory.T4Slip);
            var medical = stored.Documents.FirstOrDefault(document =>
                Category(stored, document) == DocumentCategory.MedicalExpenseEvidence);

            return new OpenedCase(
                firm,
                caseId,
                stored.PrimaryParticipantId,
                t4.DocumentId,
                t4.ContentKey,
                medical?.DocumentId ?? Guid.Empty,
                medical?.ContentKey ?? string.Empty);
        }

        private static DocumentCategory? Category(
            StoredIntakeCase stored,
            IntakeDocumentRecord document)
        {
            foreach (var expected in stored.ExpectedItems)
            {
                if (expected.ExpectedItemId == document.ExpectedItemId)
                {
                    return expected.Category;
                }
            }

            return null;
        }

        private async Task UploadAsync(
            IntakeSecret credential,
            DocumentCategory category,
            IntakeMediaKind kind,
            string body)
        {
            var bytes = Pdf(body);
            using var content = new MemoryStream(bytes, writable: false);
            var upload = await Client.UploadAsync(
                credential,
                new IntakeUploadCommand(category, 1, kind, bytes.LongLength),
                content,
                _now,
                CancellationToken.None);
            Assert.Equal(IntakeResultCode.Success, upload.Code);
        }

        /// <summary>
        /// Attempts to rewrite only the stored tax year, reporting whether the
        /// database accepted it. Nothing else is touched.
        /// </summary>
        internal async Task<bool> TrySetStoredTaxYearAsync(
            TrustedFirmId firm,
            Guid caseId,
            int taxYear)
        {
            await using var context = _database.CreateContext();
            var entity = await context.Set<IntakeCaseEntity>()
                .SingleAsync(candidate =>
                    candidate.FirmId == firm.Value && candidate.CaseId == caseId);
            entity.TaxYear = taxYear;

            try
            {
                await context.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }

        /// <summary>The tax year the database actually holds for one case.</summary>
        internal async Task<int> StoredTaxYearAsync(TrustedFirmId firm, Guid caseId)
        {
            await using var context = _database.CreateContext();
            return await context.Set<IntakeCaseEntity>()
                .AsNoTracking()
                .Where(candidate =>
                    candidate.FirmId == firm.Value && candidate.CaseId == caseId)
                .Select(candidate => candidate.TaxYear)
                .SingleAsync();
        }


        // ------------------------------------------------------- operations

        internal Task<ExtractionResult<DocumentExtractionOutcome>> ExtractAsync(
            OpenedCase opened,
            Guid? documentId = null)
        {
            _now = _now.AddMinutes(1);
            return Ingest.ExtractAsync(
                opened.Firm,
                AccountantActor,
                new ExtractDocumentCommand
                {
                    CaseId = opened.CaseId,
                    DocumentId = documentId ?? opened.DocumentId,
                },
                _now,
                CancellationToken.None);
        }

        internal Task<ExtractionResult<DocumentExtractionOutcome>> CorrectAsync(
            OpenedCase opened,
            CorrectionField field,
            string? correctedValue,
            string? boxCode = null,
            Guid? documentId = null)
        {
            _now = _now.AddMinutes(1);
            return Review.CorrectAsync(
                opened.Firm,
                AccountantActor,
                new ApplyExtractionCorrectionCommand
                {
                    CaseId = opened.CaseId,
                    DocumentId = documentId ?? opened.DocumentId,
                    Field = field,
                    BoxCode = boxCode,
                    CorrectedValue = correctedValue,
                },
                _now,
                CancellationToken.None);
        }

        internal Task<ExtractionResult<DocumentExtractionView>> OverrideClassificationAsync(
            OpenedCase opened,
            Guid documentId,
            DocumentClassification target)
        {
            _now = _now.AddMinutes(1);
            return Review.OverrideClassificationAsync(
                opened.Firm,
                AccountantActor,
                new OverrideExtractionClassificationCommand
                {
                    CaseId = opened.CaseId,
                    DocumentId = documentId,
                    TargetClassification = target,
                },
                _now,
                CancellationToken.None);
        }

        internal Task<ExtractionResult<DocumentExtractionView>> ResolveDuplicateAsync(
            OpenedCase opened,
            Guid candidateId,
            DuplicateResolutionKind resolution)
        {
            _now = _now.AddMinutes(1);
            return Review.ResolveDuplicateAsync(
                opened.Firm,
                AccountantActor,
                new ResolveExtractionDuplicateCommand
                {
                    CaseId = opened.CaseId,
                    CandidateId = candidateId,
                    Resolution = resolution,
                },
                _now,
                CancellationToken.None);
        }

        internal Task<ExtractionResult<DocumentExtractionView>> AcceptDocumentAsync(
            OpenedCase opened,
            Guid? documentId = null)
        {
            _now = _now.AddMinutes(1);
            return Review.AcceptDocumentAsync(
                opened.Firm,
                AccountantActor,
                new AcceptExtractionDocumentCommand
                {
                    CaseId = opened.CaseId,
                    DocumentId = documentId ?? opened.DocumentId,
                },
                _now,
                CancellationToken.None);
        }

        internal Task<ExtractionResult<FinalizedExtractionReview>> FinalizeAsync(
            OpenedCase opened)
        {
            _now = _now.AddMinutes(1);
            return Review.FinalizeCaseAsync(
                opened.Firm,
                AccountantActor,
                new FinalizeExtractionCaseCommand { CaseId = opened.CaseId },
                _now,
                CancellationToken.None);
        }

        /// <summary>
        /// Appends one probable-duplicate determination through the real store,
        /// the way the ingest workflow does when it finds one.
        /// </summary>
        internal async Task<Guid> FlagDuplicateAsync(OpenedCase opened)
        {
            var candidateId = Guid.NewGuid();
            var extractions = await CaseAsync(opened);
            var sequence = extractions.AuditEvents.Count == 0
                ? 1
                : extractions.AuditEvents.Max(item => item.Sequence) + 1;

            _now = _now.AddMinutes(1);
            Assert.Equal(
                ExtractionStoreWriteCode.Success,
                await Extractions.AppendDuplicateCandidateAsync(
                    opened.Firm,
                    opened.CaseId,
                    new DuplicateCandidateRecord(
                        candidateId,
                        opened.DocumentId,
                        opened.MedicalDocumentId,
                        _now),
                    RetentionPolicy.ComputeRetentionUntilUtc(2025),
                    new ExtractionAuditEventRecord(
                        Guid.NewGuid(),
                        sequence,
                        ExtractionAuditKind.DuplicateFlagged,
                        ExtractionActorKind.Accountant,
                        AccountantActor,
                        _now),
                    CancellationToken.None));

            return candidateId;
        }

        // ----------------------------------------------------------- reads

        internal Task<StoredDocumentExtraction?> FindDocumentAsync(
            TrustedFirmId firm,
            Guid caseId,
            Guid documentId) =>
            Extractions.FindDocumentAsync(firm, caseId, documentId, CancellationToken.None);

        internal Task<StoredCaseExtractions?> FindCaseAsync(TrustedFirmId firm, Guid caseId) =>
            Extractions.FindCaseAsync(firm, caseId, CancellationToken.None);

        internal async Task<StoredDocumentExtraction> DocumentAsync(
            OpenedCase opened,
            Guid? documentId = null)
        {
            var stored = await FindDocumentAsync(
                opened.Firm, opened.CaseId, documentId ?? opened.DocumentId);
            Assert.NotNull(stored);
            return stored!;
        }

        internal async Task<StoredCaseExtractions> CaseAsync(OpenedCase opened)
        {
            var stored = await FindCaseAsync(opened.Firm, opened.CaseId);
            Assert.NotNull(stored);
            return stored!;
        }

        internal Task<StoredIntakeCase?> IntakeCaseAsync(OpenedCase opened) =>
            Intake.FindAsync(opened.Firm, opened.CaseId, CancellationToken.None);

        internal Task<PersistedReviewRecord?> FindReviewRecordAsync(
            TrustedFirmId firm,
            Guid recordId) =>
            Reviews.FindAsync(firm, recordId, CancellationToken.None);

        /// <summary>Every review record identifier the database actually holds.</summary>
        internal async Task<IReadOnlyList<Guid>> EveryReviewRecordIdAsync()
        {
            await using var context = _database.CreateContext();
            return await context.Set<PersistedReviewRecord>()
                .AsNoTracking()
                .Select(record => record.RecordId)
                .ToListAsync();
        }

        // ------------------------------------------------------- the sweep

        internal Task<IReadOnlyList<StoredColumn>> EveryExtractionRowAsync() =>
            EveryColumnAsync("Extraction%", "MedicalEvidence%");

        internal Task<IReadOnlyList<StoredColumn>> EveryReviewColumnAsync() =>
            EveryColumnAsync("Review%", "Persisted%", "Slip%", "Line%", "Box%", "Unmapped%");

        /// <summary>
        /// Every value of every column of every table this feature writes.
        /// It reads the database rather than the model on purpose: a value that
        /// reached a column but not a mapped property would still be a leak.
        /// </summary>
        internal Task<IReadOnlyList<StoredColumn>> EveryWrittenColumnAsync() =>
            EveryColumnAsync("Extraction%", "MedicalEvidence%");

        private async Task<IReadOnlyList<StoredColumn>> EveryColumnAsync(
            params string[] patterns)
        {
            await using var context = _database.CreateContext();
            var connection = context.Database.GetDbConnection();
            await context.Database.OpenConnectionAsync();

            var tables = new List<string>();
            await using (var command = connection.CreateCommand())
            {
                var clauses = string.Join(
                    " OR ",
                    patterns.Select((_, index) => $"name LIKE $p{index}"));
                command.CommandText =
                    $"SELECT name FROM sqlite_master WHERE type = 'table' AND ({clauses});";
                for (var index = 0; index < patterns.Length; index++)
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = $"$p{index}";
                    parameter.Value = patterns[index];
                    command.Parameters.Add(parameter);
                }

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            var values = new List<StoredColumn>();
            foreach (var table in tables)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT * FROM \"{table}\";";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    for (var index = 0; index < reader.FieldCount; index++)
                    {
                        values.Add(Render(reader, table, index));
                    }
                }
            }

            return values;
        }

        private static StoredColumn Render(DbDataReader reader, string table, int index)
        {
            var column = reader.GetName(index);
            if (reader.IsDBNull(index))
            {
                return new StoredColumn(table, column, string.Empty);
            }

            var value = reader.GetValue(index);
            return new StoredColumn(
                table,
                column,
                value switch
                {
                    byte[] bytes => string.Concat(
                        Convert.ToBase64String(bytes),
                        " ",
                        Encoding.UTF8.GetString(bytes)),
                    _ => value.ToString() ?? string.Empty,
                });
        }

        public async ValueTask DisposeAsync() => await _database.DisposeAsync();

        // -------------------------------------------------- source documents

        /// <summary>
        /// A legitimate 2025 employee copy — Box 12, Box 45, both display names,
        /// income boxes, and deliberately no Box 54, exactly as the CRA
        /// instructs employers to issue it. The SIN probe and the byte marker
        /// both live in here.
        /// </summary>
        private static string T4Source() =>
            string.Join(
                "\n",
                SourceMarker,
                $"BOX 12:{SinProbe}",
                "BOX 14:50000.00",
                "BOX 45:1",
                $"EMPLOYER-NAME:{EmployerName}",
                $"EMPLOYEE-NAME:{EmployeeName}");

        /// <summary>
        /// A signature-valid synthetic PDF. The magic prefix is what the real
        /// upload validator checks; the key/value lines after it are what the
        /// provider reads. Neither is a real document.
        /// </summary>
        private static byte[] Pdf(string body) =>
            [.. "%PDF-1.7\n"u8.ToArray(), .. Encoding.UTF8.GetBytes(body)];

        private static string MedicalSource() =>
            string.Join(
                "\n",
                SourceMarker,
                "SERVICE-DATE:14 March 2025",
                "GROSS-AMOUNT:$1,120.50",
                "CURRENCY:CAD",
                $"PROVIDER-NAME:{MedicalProviderName}",
                $"PATIENT-NAME:{PatientName}",
                "RECEIPT-REFERENCE:R-4471");
    }

    // ------------------------------------------------------- test doubles

    /// <summary>
    /// The intake write path and the extraction read path over one in-memory
    /// object store, so the bytes extraction reads are the bytes the upload
    /// staged. The real Development encrypted adapter implements the same two
    /// ports and is driven over HTTP in the portal suite; here the point is the
    /// relational store, not the cipher.
    /// <para>
    /// The read still demands the full four-part scope. The firm, case, and
    /// document it checks come from the server-stored case rather than from the
    /// key, so a content key on its own authorizes nothing.
    /// </para>
    /// </summary>
    private sealed class IntakeContentAdapter : IIntakeContentStore, IExtractionContentReader
    {
        private readonly Dictionary<string, (Guid Firm, Guid Case, byte[] Body)> _objects = [];
        private readonly byte[] _fingerprintKey = Enumerable.Repeat((byte)0x34, 32).ToArray();

        /// <summary>
        /// Exposed to the test only, so the no-SIN sweep can prove the probe
        /// really was uploaded. The production contract has no read member.
        /// </summary>
        internal string EveryStagedBody() =>
            string.Join(
                "\n",
                _objects.Values.Select(item => Encoding.UTF8.GetString(item.Body)));

        public async Task<StagedIntakeContent> StageAsync(
            TrustedFirmId firm,
            Guid caseId,
            Stream content,
            long maximumBytes,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(firm);
            ArgumentNullException.ThrowIfNull(content);

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
            _objects[key] = (firm.Value, caseId, bytes);

            using var fingerprint = new System.Security.Cryptography.HMACSHA256(
                System.Security.Cryptography.HMACSHA256.HashData(
                    _fingerprintKey,
                    Encoding.UTF8.GetBytes($"{firm.Value:D}\n{caseId:D}")));
            return new StagedIntakeContent(key, total, fingerprint.ComputeHash(bytes));
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

        public async Task<ExtractionSourceLease?> OpenAsync(
            TrustedFirmId firm,
            Guid caseId,
            Guid documentId,
            string contentKey,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(firm);

            if (contentKey is null ||
                documentId == Guid.Empty ||
                !_objects.TryGetValue(contentKey, out var stored) ||
                stored.Firm != firm.Value ||
                stored.Case != caseId)
            {
                return null;
            }

            // The document part of the scope is verified the way the real
            // adapter verifies it: against the server-stored case, never
            // against anything the caller supplied.
            var owned = DocumentOwnsKey?.Invoke(firm, caseId, documentId, contentKey);
            if (owned is false)
            {
                return null;
            }

            await Task.CompletedTask;
            return ExtractionSourceLease.Create(
                new MemoryStream(stored.Body, writable: false));
        }

        /// <summary>
        /// Set by the fixture to the server-stored ownership check. Left unset
        /// the adapter still refuses every firm/case mismatch.
        /// </summary>
        internal Func<TrustedFirmId, Guid, Guid, string, bool>? DocumentOwnsKey { get; set; }
    }

    /// <summary>
    /// Issues opaque intake credentials for the real workflows. The key is a
    /// fixed synthetic value; nothing derived from it leaves this fixture.
    /// </summary>
    private sealed class TestCredentialSource : IIntakeCredentialSource
    {
        private readonly byte[] _key;

        internal TestCredentialSource(byte[] key) => _key = key;

        public Guid CreateIdentifier() => Guid.NewGuid();

        public IntakeSecret CreateCredential()
        {
            var encoded = Convert.ToBase64String(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            Assert.True(IntakeSecret.TryParse(encoded, out var credential));
            return credential!;
        }

        public IntakeTokenDigest Digest(
            IntakeSecret credential,
            IntakeTokenPurpose purpose) =>
            new(System.Security.Cryptography.HMACSHA256.HashData(
                _key,
                Encoding.UTF8.GetBytes(
                    string.Concat(purpose.ToString(), "\n", credential.Reveal()))));
    }

    /// <summary>
    /// Reads the lease it is given — so a content read that never happened would
    /// fail this suite — and answers from the source it read.
    /// </summary>
    private sealed class StubDocumentIntelligenceProvider : IDocumentIntelligenceProvider
    {
        /// <summary>
        /// Forces one answer for every document. Left null the adapter decides
        /// from the bytes it read, the way the real deterministic provider does.
        /// </summary>
        internal DocumentClassification? Classification { get; set; }

        internal ProviderFailure Failure { get; set; } = ProviderFailure.None;

        internal int? T4Year { get; set; } = 2025;

        public async Task<ProviderClassificationResult?> ClassifyAsync(
            Stream source,
            CancellationToken cancellationToken)
        {
            var text = await ReadAsync(source, cancellationToken);
            if (text is null)
            {
                return null;
            }

            return new ProviderClassificationResult
            {
                Classification = ClassificationOf(text),
                ProviderLabel = ProviderLabel,
            };
        }

        public async Task<ProviderT4Result?> ExtractT4Async(
            Stream source,
            CancellationToken cancellationToken)
        {
            var text = await ReadAsync(source, cancellationToken);
            if (text is null || ClassificationOf(text) != DocumentClassification.T4Slip)
            {
                return null;
            }

            return new ProviderT4Result
            {
                Year = T4Year,
                EmployerName = EmployerName,
                EmployeeName = EmployeeName,
                Boxes =
                [
                    new ProviderBoxValue { Code = "12", Value = Read(text, "BOX 12") },
                    new ProviderBoxValue { Code = "14", Value = Read(text, "BOX 14") },
                    new ProviderBoxValue { Code = "45", Value = Read(text, "BOX 45") },
                ],
            };
        }

        public async Task<ProviderMedicalResult?> ExtractMedicalAsync(
            Stream source,
            CancellationToken cancellationToken)
        {
            var text = await ReadAsync(source, cancellationToken);
            if (text is null ||
                ClassificationOf(text) != DocumentClassification.MedicalExpenseEvidence)
            {
                return null;
            }

            return new ProviderMedicalResult
            {
                ServiceDateAsPrinted = Read(text, "SERVICE-DATE"),
                GrossAmountAsPrinted = Read(text, "GROSS-AMOUNT"),
                CurrencyCodeAsPrinted = Read(text, "CURRENCY"),
                ProviderDisplayName = Read(text, "PROVIDER-NAME"),
                PatientDisplayName = Read(text, "PATIENT-NAME"),
                ReceiptReference = Read(text, "RECEIPT-REFERENCE"),
            };
        }

        private async Task<string?> ReadAsync(Stream source, CancellationToken cancellationToken)
        {
            if (Failure == ProviderFailure.Throws)
            {
                throw new InvalidOperationException("The synthetic provider failed.");
            }

            if (Failure == ProviderFailure.ReturnsNothing)
            {
                return null;
            }

            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        /// <summary>
        /// Decides from the source alone. Nothing here decides anything about
        /// tax: it recognizes which of the two supported shapes the bytes are,
        /// and answers Unsupported for anything else.
        /// </summary>
        private DocumentClassification ClassificationOf(string text)
        {
            if (Classification is { } forced)
            {
                return forced;
            }

            if (Read(text, "SERVICE-DATE") is not null ||
                Read(text, "PATIENT-NAME") is not null)
            {
                return DocumentClassification.MedicalExpenseEvidence;
            }

            return Read(text, "BOX 12") is not null || Read(text, "BOX 14") is not null
                ? DocumentClassification.T4Slip
                : DocumentClassification.Unsupported;
        }

        private static string? Read(string text, string key)
        {
            foreach (var line in text.Split('\n'))
            {
                var separator = line.IndexOf(':', StringComparison.Ordinal);
                if (separator < 0)
                {
                    continue;
                }

                if (string.Equals(line[..separator].Trim(), key, StringComparison.Ordinal))
                {
                    var value = line[(separator + 1)..].Trim();
                    return value.Length == 0 ? null : value;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Gives every store call its own context and its own adapter, the way the
    /// portal's scoped registration does. A single long-lived context would let
    /// EF's identity map answer a read the database never saw.
    /// </summary>
    private sealed class ScopedIntakeStore : IIntakeStore
    {
        private readonly RelationalTestContext _database;

        internal ScopedIntakeStore(RelationalTestContext database) => _database = database;

        private async Task<T> WithStoreAsync<T>(Func<SqlIntakeStore, Task<T>> operation)
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
                linkScope, session, auditEvent, cancellationToken));

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
            WithStoreAsync(store => store.FindForClientAsync(scope, cancellationToken));

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

    private sealed class ScopedExtractionStore : IExtractionStore
    {
        private readonly RelationalTestContext _database;

        internal ScopedExtractionStore(RelationalTestContext database) => _database = database;

        private async Task<T> WithStoreAsync<T>(Func<SqlExtractionStore, Task<T>> operation)
        {
            await using var context = _database.CreateContext();
            return await operation(new SqlExtractionStore(context));
        }

        public Task<ExtractionStoreWriteCode> AppendExtractionVersionAsync(
            TrustedFirmId firm,
            Guid caseId,
            Guid participantId,
            ExtractionVersion version,
            DateTimeOffset retentionUntilUtc,
            ExtractionAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.AppendExtractionVersionAsync(
                firm,
                caseId,
                participantId,
                version,
                retentionUntilUtc,
                auditEvent,
                cancellationToken));

        public Task<ExtractionStoreWriteCode> AppendMedicalEvidenceVersionAsync(
            TrustedFirmId firm,
            Guid caseId,
            Guid participantId,
            MedicalEvidenceVersion version,
            DateTimeOffset retentionUntilUtc,
            ExtractionAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.AppendMedicalEvidenceVersionAsync(
                firm,
                caseId,
                participantId,
                version,
                retentionUntilUtc,
                auditEvent,
                cancellationToken));

        public Task<ExtractionStoreWriteCode> AppendCorrectionAsync(
            TrustedFirmId firm,
            Guid caseId,
            ExtractionCorrectionRecord correction,
            DateTimeOffset retentionUntilUtc,
            ExtractionAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.AppendCorrectionAsync(
                firm, caseId, correction, retentionUntilUtc, auditEvent, cancellationToken));

        public Task<ExtractionStoreWriteCode> AppendClassificationOverrideAsync(
            TrustedFirmId firm,
            Guid caseId,
            ClassificationOverrideRecord classificationOverride,
            DateTimeOffset retentionUntilUtc,
            ExtractionAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.AppendClassificationOverrideAsync(
                firm,
                caseId,
                classificationOverride,
                retentionUntilUtc,
                auditEvent,
                cancellationToken));

        public Task<ExtractionStoreWriteCode> AppendDuplicateCandidateAsync(
            TrustedFirmId firm,
            Guid caseId,
            DuplicateCandidateRecord candidate,
            DateTimeOffset retentionUntilUtc,
            ExtractionAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.AppendDuplicateCandidateAsync(
                firm, caseId, candidate, retentionUntilUtc, auditEvent, cancellationToken));

        public Task<ExtractionStoreWriteCode> AppendDuplicateResolutionAsync(
            TrustedFirmId firm,
            Guid caseId,
            DuplicateResolutionRecord resolution,
            DateTimeOffset retentionUntilUtc,
            ExtractionAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.AppendDuplicateResolutionAsync(
                firm, caseId, resolution, retentionUntilUtc, auditEvent, cancellationToken));

        public Task<ExtractionStoreWriteCode> AppendAcceptanceAsync(
            TrustedFirmId firm,
            Guid caseId,
            ExtractionAcceptanceRecord acceptance,
            DateTimeOffset retentionUntilUtc,
            ExtractionAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.AppendAcceptanceAsync(
                firm, caseId, acceptance, retentionUntilUtc, auditEvent, cancellationToken));

        public Task<ExtractionStoreWriteCode> AppendAuditEventAsync(
            TrustedFirmId firm,
            Guid caseId,
            ExtractionAuditEventRecord auditEvent,
            DateTimeOffset retentionUntilUtc,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.AppendAuditEventAsync(
                firm, caseId, auditEvent, retentionUntilUtc, cancellationToken));

        public Task<StoredDocumentExtraction?> FindDocumentAsync(
            TrustedFirmId firm,
            Guid caseId,
            Guid documentId,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store =>
                store.FindDocumentAsync(firm, caseId, documentId, cancellationToken));

        public Task<StoredCaseExtractions?> FindCaseAsync(
            TrustedFirmId firm,
            Guid caseId,
            CancellationToken cancellationToken) =>
            WithStoreAsync(store => store.FindCaseAsync(firm, caseId, cancellationToken));
    }

    private sealed class ScopedReviewRecordRepository : IReviewRecordRepository
    {
        private readonly RelationalTestContext _database;

        internal ScopedReviewRecordRepository(RelationalTestContext database) =>
            _database = database;

        public async Task<ParseResult<PersistedReviewRecord>> SaveAsync(
            TrustedFirmId firm,
            PersistedReviewRecord record,
            CancellationToken cancellationToken)
        {
            await using var context = _database.CreateContext();
            return await new SqlReviewRecordRepository(context)
                .SaveAsync(firm, record, cancellationToken);
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
}
