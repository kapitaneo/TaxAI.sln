using System.Reflection;
using DocumentExtraction.Models;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using Microsoft.EntityFrameworkCore;
using Persistence.Entities;
using Persistence.Repositories;
using Persistence.Tests.Support;
using Tenancy.Models;
using Tenancy.Services;

namespace Persistence.Tests.Repositories;

/// <summary>
/// Spec 015 T03 — the extraction store against a real relational database with
/// the real migrations applied.
/// <para>
/// Three properties are worth more than the rest and are asserted from several
/// directions: history is append-only, every read is firm-scoped and gives a
/// foreign firm the same answer as a missing row, and no column anywhere can end
/// up holding a social insurance number.
/// </para>
/// </summary>
public sealed class SqlExtractionStoreTests
{
    private const string PlantedSin = "123456789";

    private static readonly Guid CaseId =
        Guid.Parse("12121212-1212-4212-8212-121212121212");

    private static readonly Guid ParticipantId =
        Guid.Parse("16161616-1616-4616-8616-161616161616");

    private static readonly Guid DocumentId =
        Guid.Parse("21212121-2121-4121-8121-212121212121");

    private static readonly Guid OtherDocumentId =
        Guid.Parse("22222222-2222-4222-8222-222222222222");

    private static readonly Guid ActorReference =
        Guid.Parse("14141414-1414-4414-8414-141414141414");

    private static readonly DateTimeOffset Instant =
        new(new DateTime(2028, 1, 2, 3, 4, 5, DateTimeKind.Utc));

    /// <summary>
    /// The boundary the existing policy computes for the case's own year. This
    /// feature declares no second retention authority and computes no boundary
    /// of its own.
    /// </summary>
    private static readonly DateTimeOffset RetentionUntilUtc =
        RetentionPolicy.ComputeRetentionUntilUtc(2025);

    // ---------------------------------------------------------- round trip

    [Fact]
    public async Task AnAppendedVersionReadsBackThroughTheWorkflowsOwnFactories()
    {
        await using var database = await SeededDatabaseAsync();
        await using var context = database.CreateContext();
        var store = new SqlExtractionStore(context);

        Assert.Equal(
            ExtractionStoreWriteCode.Success,
            await store.AppendExtractionVersionAsync(
                Firm(),
                CaseId,
                ParticipantId,
                Version(),
                RetentionUntilUtc,
                Audit(1, ExtractionAuditKind.ExtractionRecorded),
                CancellationToken.None));

        var stored = await store.FindDocumentAsync(
            Firm(), CaseId, DocumentId, CancellationToken.None);

        Assert.NotNull(stored);
        var version = Assert.Single(stored!.Versions);
        Assert.Equal(1, version.Version);
        Assert.Equal(DocumentClassification.T4Slip, version.Classification.EffectiveClassification);
        Assert.Equal("Acme Manufacturing", version.EmployerDisplayName);
        Assert.Equal("Casey Taxpayer", version.EmployeeDisplayName);
        Assert.Equal(ExtractionAcceptanceState.AwaitingAccountantDecision, version.AcceptanceState);
        Assert.Equal(Instant, version.RecordedAtUtc);
        Assert.Equal(0.75m, version.Provenance!.Confidence);
        Assert.Equal("deterministic-test-adapter", version.Provenance.ProviderLabel);

        // The opaque source reference is absent from the schema, so a stored
        // version reports it absent rather than reconstructing something like it.
        Assert.Null(version.Provenance.SourceReference);
    }

    [Fact]
    public async Task BoxesKeepTheirOrderAndTheirDuplicates()
    {
        await using var database = await SeededDatabaseAsync();
        await using var context = database.CreateContext();
        var store = new SqlExtractionStore(context);

        await AppendAsync(store, Version(boxes:
        [
            Box("14", "50000.00"),
            Box("14", "60000.00"),
            Box("16A", "188.00"),
        ]));

        var stored = await store.FindDocumentAsync(
            Firm(), CaseId, DocumentId, CancellationToken.None);

        // Collapsing or reordering here would hide exactly what the engine's own
        // duplicate-box rejection exists to catch.
        Assert.Equal(
            new[] { ("14", "50000.00"), ("14", "60000.00"), ("16A", "188.00") },
            stored!.Versions.Single().BoxValues
                .Select(box => (box.Code, box.Value!))
                .ToArray());
    }

    [Fact]
    public async Task AnOverriddenClassificationRetainsTheAutomatedOne()
    {
        await using var database = await SeededDatabaseAsync();
        await using var context = database.CreateContext();
        var store = new SqlExtractionStore(context);

        Assert.True(ClassificationOutcome.TryAutomated(
            DocumentClassification.Unsupported, out var automated));
        Assert.True(automated!.TryOverride(
            DocumentClassification.T4Slip, ActorReference, Instant, out var overridden));

        await AppendAsync(store, Version(classification: overridden));

        var stored = await store.FindDocumentAsync(
            Firm(), CaseId, DocumentId, CancellationToken.None);
        var classification = stored!.Versions.Single().Classification;

        Assert.Equal(DocumentClassification.Unsupported, classification.AutomatedClassification);
        Assert.Equal(DocumentClassification.T4Slip, classification.OverriddenClassification);
        Assert.Equal(ActorReference, classification.OverrideActorReference);
    }

    // ------------------------------------------------------- append-only

    [Fact]
    public async Task ASecondVersionLeavesThePriorOneByteUnchanged()
    {
        await using var database = await SeededDatabaseAsync();
        await using var context = database.CreateContext();
        var store = new SqlExtractionStore(context);

        await AppendAsync(store, Version(), sequence: 1);
        var firstRow = await SnapshotVersionRowAsync(context, version: 1);

        await AppendAsync(
            store,
            Version(
                versionNumber: 2,
                acceptance: ExtractionAcceptanceState.AcceptedByAccountant,
                boxes: [Box("14", "99999.00")]),
            sequence: 2);

        var afterRow = await SnapshotVersionRowAsync(context, version: 1);
        Assert.Equal(firstRow, afterRow);

        var stored = await store.FindDocumentAsync(
            Firm(), CaseId, DocumentId, CancellationToken.None);

        Assert.Equal(2, stored!.Versions.Count);
        Assert.Equal(
            ExtractionAcceptanceState.AwaitingAccountantDecision,
            stored.Versions[0].AcceptanceState);
        Assert.Equal(
            ExtractionAcceptanceState.AcceptedByAccountant,
            stored.Versions[1].AcceptanceState);
    }

    [Fact]
    public async Task ReAppendingAVersionNumberConflictsAndCommitsNothing()
    {
        await using var database = await SeededDatabaseAsync();

        await using (var context = database.CreateContext())
        {
            await AppendAsync(new SqlExtractionStore(context), Version());
        }

        await using (var context = database.CreateContext())
        {
            var store = new SqlExtractionStore(context);

            Assert.Equal(
                ExtractionStoreWriteCode.Conflict,
                await store.AppendExtractionVersionAsync(
                    Firm(),
                    CaseId,
                    ParticipantId,
                    Version(boxes: [Box("14", "1.00")]),
                    RetentionUntilUtc,
                    Audit(2, ExtractionAuditKind.ExtractionRecorded),
                    CancellationToken.None));
        }

        await using (var context = database.CreateContext())
        {
            // Nothing partial: the losing append left neither its boxes nor its
            // audit event behind.
            Assert.Equal(1, await context.Set<ExtractionVersionEntity>().CountAsync());
            Assert.Equal(1, await context.Set<ExtractionBoxValueEntity>().CountAsync());
            Assert.Equal(1, await context.Set<ExtractionAuditEventEntity>().CountAsync());
        }
    }

    [Fact]
    public async Task AResolutionLeavesItsDeterminationRowUntouched()
    {
        await using var database = await SeededDatabaseAsync();
        await using var context = database.CreateContext();
        var store = new SqlExtractionStore(context);
        var candidateId = Guid.Parse("31313131-3131-4131-8131-313131313131");

        Assert.Equal(
            ExtractionStoreWriteCode.Success,
            await store.AppendDuplicateCandidateAsync(
                Firm(),
                CaseId,
                new DuplicateCandidateRecord(candidateId, DocumentId, OtherDocumentId, Instant),
                RetentionUntilUtc,
                Audit(1, ExtractionAuditKind.DuplicateFlagged),
                CancellationToken.None));

        var determination = await SnapshotCandidateRowAsync(context, candidateId);

        Assert.Equal(
            ExtractionStoreWriteCode.Success,
            await store.AppendDuplicateResolutionAsync(
                Firm(),
                CaseId,
                new DuplicateResolutionRecord(
                    Guid.Parse("32323232-3232-4232-8232-323232323232"),
                    candidateId,
                    DuplicateResolutionKind.ConfirmedDuplicate,
                    ExtractionActorKind.Accountant,
                    ActorReference,
                    Instant),
                RetentionUntilUtc,
                Audit(2, ExtractionAuditKind.DuplicateResolved),
                CancellationToken.None));

        Assert.Equal(determination, await SnapshotCandidateRowAsync(context, candidateId));

        var stored = await store.FindDocumentAsync(
            Firm(), CaseId, DocumentId, CancellationToken.None);

        // Both documents survive the decision. Nothing is discarded or merged.
        Assert.Single(stored!.DuplicateCandidates);
        Assert.Equal(
            DuplicateResolutionKind.ConfirmedDuplicate,
            Assert.Single(stored.DuplicateResolutions).Resolution);
    }

    [Fact]
    public async Task ASecondDecisionOnOneCandidateIsRefusedByTheDatabase()
    {
        await using var database = await SeededDatabaseAsync();
        var candidateId = Guid.Parse("31313131-3131-4131-8131-313131313131");

        await using (var context = database.CreateContext())
        {
            var store = new SqlExtractionStore(context);
            await store.AppendDuplicateCandidateAsync(
                Firm(),
                CaseId,
                new DuplicateCandidateRecord(candidateId, DocumentId, OtherDocumentId, Instant),
                RetentionUntilUtc,
                Audit(1, ExtractionAuditKind.DuplicateFlagged),
                CancellationToken.None);
            await store.AppendDuplicateResolutionAsync(
                Firm(),
                CaseId,
                Resolution(candidateId, "32323232-3232-4232-8232-323232323232"),
                RetentionUntilUtc,
                Audit(2, ExtractionAuditKind.DuplicateResolved),
                CancellationToken.None);
        }

        await using (var context = database.CreateContext())
        {
            Assert.Equal(
                ExtractionStoreWriteCode.Conflict,
                await new SqlExtractionStore(context).AppendDuplicateResolutionAsync(
                    Firm(),
                    CaseId,
                    Resolution(candidateId, "33333333-3333-4333-8333-333333333333"),
                    RetentionUntilUtc,
                    Audit(3, ExtractionAuditKind.DuplicateResolved),
                    CancellationToken.None));
        }
    }

    [Fact]
    public async Task TheContractDeclaresNoUpdateOrDeleteMember()
    {
        await Task.CompletedTask;

        var members = typeof(IExtractionStore)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();

        Assert.All(
            members,
            name => Assert.True(
                name.StartsWith("Append", StringComparison.Ordinal) ||
                name.StartsWith("Find", StringComparison.Ordinal),
                $"{name} is neither an append nor a find."));

        foreach (var forbidden in new[]
                 {
                     "Update", "Delete", "Remove", "Save", "Upsert", "Set", "Purge", "Replace",
                 })
        {
            Assert.DoesNotContain(
                members,
                name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task TheStoreItselfIssuesNoUpdateOrDeleteCall()
    {
        await Task.CompletedTask;

        var source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src", "Persistence", "Repositories", "SqlExtractionStore.cs"));

        foreach (var forbidden in new[]
                 {
                     "_context.Update", "_context.Remove", "_context.RemoveRange",
                     "ExecuteUpdate", "ExecuteDelete", ".Update(", ".Remove(",
                 })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    // -------------------------------------------------------- firm scope

    [Fact]
    public async Task EveryTaxpayerDataMemberTakesATrustedFirmFirstWithNoOmittingOverload()
    {
        await Task.CompletedTask;

        var methods = typeof(IExtractionStore).GetMethods();

        Assert.NotEmpty(methods);
        Assert.All(
            methods,
            method =>
            {
                var parameters = method.GetParameters();
                Assert.Equal(typeof(TrustedFirmId), parameters[0].ParameterType);
                Assert.False(parameters[0].IsOptional);
                Assert.Equal("caseId", parameters[1].Name);
            });

        // No overload can omit the firm: every name appears exactly once.
        Assert.All(
            methods.Select(method => method.Name).Distinct(),
            name => Assert.Equal(1, methods.Count(method => method.Name == name)));
    }

    [Fact]
    public async Task AForeignFirmReadIsIndistinguishableFromAMissingOne()
    {
        await using var database = await SeededDatabaseAsync();
        await using var context = database.CreateContext();
        var store = new SqlExtractionStore(context);

        await AppendAsync(store, Version());

        var foreignFirm = TrustedFirmId.FromGuid(SyntheticFixtures.OtherFirmId);
        var missingCase = Guid.Parse("44444444-4444-4444-8444-444444444444");

        Assert.Null(await store.FindDocumentAsync(
            foreignFirm, CaseId, DocumentId, CancellationToken.None));
        Assert.Null(await store.FindDocumentAsync(
            Firm(), missingCase, DocumentId, CancellationToken.None));
        Assert.Null(await store.FindCaseAsync(foreignFirm, CaseId, CancellationToken.None));
        Assert.Null(await store.FindCaseAsync(Firm(), missingCase, CancellationToken.None));

        // A document that exists in the firm's case but was never extracted is
        // also simply absent — the shared answer stays shared.
        var caseView = await store.FindCaseAsync(Firm(), CaseId, CancellationToken.None);
        Assert.NotNull(caseView);
        Assert.DoesNotContain(caseView!.Documents, document => document.DocumentId == OtherDocumentId);
    }

    [Fact]
    public async Task AForeignFirmWriteIsRefusedAndStoresNothing()
    {
        await using var database = await SeededDatabaseAsync();
        await using var context = database.CreateContext();
        var store = new SqlExtractionStore(context);

        Assert.Equal(
            ExtractionStoreWriteCode.NotFound,
            await store.AppendExtractionVersionAsync(
                TrustedFirmId.FromGuid(SyntheticFixtures.OtherFirmId),
                CaseId,
                ParticipantId,
                Version(),
                RetentionUntilUtc,
                Audit(1, ExtractionAuditKind.ExtractionRecorded),
                CancellationToken.None));

        Assert.Equal(0, await context.Set<ExtractionVersionEntity>().CountAsync());
        Assert.Equal(0, await context.Set<ExtractionAuditEventEntity>().CountAsync());
    }

    // ----------------------------------------------------------- retention

    [Fact]
    public async Task EveryAppendedRowCarriesTheExclusiveSevenYearBoundary()
    {
        await using var database = await SeededDatabaseAsync();
        await using var context = database.CreateContext();
        var store = new SqlExtractionStore(context);

        await AppendEveryKindAsync(store);

        var boundaries = new[]
        {
            await context.Set<ExtractionVersionEntity>()
                .Select(entity => entity.RetentionUntilUtc).ToArrayAsync(),
            await context.Set<ExtractionBoxValueEntity>()
                .Select(entity => entity.RetentionUntilUtc).ToArrayAsync(),
            await context.Set<ExtractionFieldOutcomeEntity>()
                .Select(entity => entity.RetentionUntilUtc).ToArrayAsync(),
            await context.Set<MedicalEvidenceVersionEntity>()
                .Select(entity => entity.RetentionUntilUtc).ToArrayAsync(),
            await context.Set<ExtractionCorrectionEntity>()
                .Select(entity => entity.RetentionUntilUtc).ToArrayAsync(),
            await context.Set<ExtractionClassificationOverrideEntity>()
                .Select(entity => entity.RetentionUntilUtc).ToArrayAsync(),
            await context.Set<ExtractionDuplicateCandidateEntity>()
                .Select(entity => entity.RetentionUntilUtc).ToArrayAsync(),
            await context.Set<ExtractionDuplicateResolutionEntity>()
                .Select(entity => entity.RetentionUntilUtc).ToArrayAsync(),
            await context.Set<ExtractionAcceptanceEntity>()
                .Select(entity => entity.RetentionUntilUtc).ToArrayAsync(),
            await context.Set<ExtractionAuditEventEntity>()
                .Select(entity => entity.RetentionUntilUtc).ToArrayAsync(),
        };

        Assert.All(boundaries, table => Assert.NotEmpty(table));
        Assert.All(
            boundaries.SelectMany(table => table),
            boundary => Assert.Equal(RetentionUntilUtc, boundary));

        // Derived by the existing policy from the case's own year, exclusive:
        // one tick before it the record survives, at it the record does not.
        Assert.Equal(
            new DateTimeOffset(new DateTime(2033, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            RetentionUntilUtc);
        Assert.False(RetentionPolicy.IsPurgeEligible(
            RetentionUntilUtc, RetentionUntilUtc.AddTicks(-1)));
        Assert.True(RetentionPolicy.IsPurgeEligible(RetentionUntilUtc, RetentionUntilUtc));
    }

    // ----------------------------------------------------------------- PII

    [Fact]
    public async Task APlantedSocialInsuranceNumberReachesNoColumnAndNoReadShape()
    {
        await using var database = await SeededDatabaseAsync();
        await using var context = database.CreateContext();
        var store = new SqlExtractionStore(context);

        // The box loop that would have persisted it cannot: the type refused the
        // box before the store ever saw it.
        Assert.False(ExtractionBoxValue.TryCreate("12", PlantedSin, out _));

        await AppendEveryKindAsync(store);

        var captured = await ReadEveryTextAndIdentifierAsync(context);

        Assert.NotEmpty(captured);
        Assert.DoesNotContain(captured, value => value.Contains(PlantedSin, StringComparison.Ordinal));

        var stored = await store.FindCaseAsync(Firm(), CaseId, CancellationToken.None);
        Assert.DoesNotContain(
            PlantedSin,
            stored!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAuditRowCarriesOnlyStructuralFacts()
    {
        await using var database = await SeededDatabaseAsync();
        await using var context = database.CreateContext();
        var store = new SqlExtractionStore(context);

        await AppendAsync(store, Version());

        var audit = await context.Set<ExtractionAuditEventEntity>().SingleAsync();

        Assert.Equal(ExtractionAuditKind.ExtractionRecorded, audit.Kind);
        Assert.Equal(ExtractionActorKind.Accountant, audit.ActorKind);
        Assert.Equal(ActorReference, audit.ActorReference);
        Assert.Equal(1, audit.Sequence);

        // Structural only: every column is a closed enum value, an opaque
        // identifier, a sequence, or an instant.
        Assert.All(
            typeof(ExtractionAuditEventEntity).GetProperties(),
            property => Assert.True(
                property.PropertyType == typeof(Guid) ||
                property.PropertyType == typeof(long) ||
                property.PropertyType == typeof(DateTimeOffset) ||
                property.PropertyType.IsEnum,
                $"{property.Name} is not a structural type."));
    }

    // ------------------------------------------------------------ medical

    [Fact]
    public async Task TwoMedicalExtractionsProduceTwoImmutableVersions()
    {
        await using var database = await SeededDatabaseAsync();
        await using var context = database.CreateContext();
        var store = new SqlExtractionStore(context);

        await AppendMedicalAsync(store, version: 1, amount: "$120.50", sequence: 1);
        var firstRow = await SnapshotMedicalRowAsync(context, version: 1);

        await AppendMedicalAsync(store, version: 2, amount: "$121.50", sequence: 2);

        Assert.Equal(firstRow, await SnapshotMedicalRowAsync(context, version: 1));

        var stored = await store.FindDocumentAsync(
            Firm(), CaseId, DocumentId, CancellationToken.None);

        Assert.Equal(2, stored!.MedicalEvidenceVersions.Count);

        // As printed, both of them: no normalization, no conversion, no rounding.
        Assert.Equal("$120.50", stored.MedicalEvidenceVersions[0].GrossAmountAsPrinted);
        Assert.Equal("$121.50", stored.MedicalEvidenceVersions[1].GrossAmountAsPrinted);
        Assert.Equal("14 mars 2025", stored.MedicalEvidenceVersions[0].ServiceDateAsPrinted);
    }

    [Fact]
    public async Task AnUnknownCurrencyStaysExplicitlyUnknown()
    {
        await using var database = await SeededDatabaseAsync();
        await using var context = database.CreateContext();
        var store = new SqlExtractionStore(context);

        await AppendMedicalAsync(store, version: 1, currency: CurrencyDeclaration.Unknown);

        var stored = await store.FindDocumentAsync(
            Firm(), CaseId, DocumentId, CancellationToken.None);
        var currency = stored!.MedicalEvidenceVersions.Single().Currency;

        Assert.False(currency.IsKnown);
        Assert.Null(currency.CodeAsPrinted);
    }

    // ------------------------------------------------------ case history

    [Fact]
    public async Task TheCaseReadCarriesEveryAppendedFactInOrder()
    {
        await using var database = await SeededDatabaseAsync();
        await using var context = database.CreateContext();
        var store = new SqlExtractionStore(context);

        await AppendEveryKindAsync(store);

        var stored = await store.FindCaseAsync(Firm(), CaseId, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(CaseId, stored!.CaseId);
        Assert.Single(stored.CaseAcceptances);
        Assert.Equal(
            Enumerable.Range(1, stored.AuditEvents.Count).Select(sequence => (long)sequence),
            stored.AuditEvents.Select(audit => audit.Sequence));

        var document = Assert.Single(
            stored.Documents, candidate => candidate.DocumentId == DocumentId);

        Assert.Single(document.Versions);
        Assert.Single(document.MedicalEvidenceVersions);
        Assert.Single(document.Corrections);
        Assert.Single(document.ClassificationOverrides);
        Assert.Single(document.DuplicateCandidates);
        Assert.Single(document.DuplicateResolutions);
        Assert.Single(document.Acceptances);
        Assert.Equal(ParticipantId, document.ParticipantId);
    }

    [Fact]
    public async Task ACorrectionRecordsTheCompleteImmutableFact()
    {
        await using var database = await SeededDatabaseAsync();
        await using var context = database.CreateContext();
        var store = new SqlExtractionStore(context);

        await AppendAsync(store, Version());

        var precedingVersionId = Guid.Parse("51515151-5151-4151-8151-515151515151");

        Assert.Equal(
            ExtractionStoreWriteCode.Success,
            await store.AppendCorrectionAsync(
                Firm(),
                CaseId,
                new ExtractionCorrectionRecord(
                    Guid.Parse("52525252-5252-4252-8252-525252525252"),
                    DocumentId,
                    CorrectionField.EmployerAddress,
                    null,
                    "12 Main Street",
                    "14 Main Street",
                    ExtractionActorKind.Accountant,
                    ActorReference,
                    Instant,
                    precedingVersionId),
                RetentionUntilUtc,
                Audit(2, ExtractionAuditKind.CorrectionRecorded),
                CancellationToken.None));

        var stored = await store.FindDocumentAsync(
            Firm(), CaseId, DocumentId, CancellationToken.None);
        var correction = Assert.Single(stored!.Corrections);

        // An address exists only here, as an opaque value the closed field
        // vocabulary identifies. There is no address column anywhere.
        Assert.Equal(CorrectionField.EmployerAddress, correction.Field);
        Assert.Equal("12 Main Street", correction.PriorValue);
        Assert.Equal("14 Main Street", correction.CorrectedValue);
        Assert.Equal(ExtractionActorKind.Accountant, correction.ActorKind);
        Assert.Equal(ActorReference, correction.ActorReference);
        Assert.Equal(Instant, correction.SuppliedAtUtc);
        Assert.Equal(precedingVersionId, correction.PrecedingVersionId);
    }

    [Fact]
    public async Task ACorrectionCannotNameTheExcludedBox()
    {
        await Task.CompletedTask;

        Assert.Throws<ArgumentException>(() => new ExtractionCorrectionRecord(
            Guid.NewGuid(),
            DocumentId,
            CorrectionField.T4BoxValue,
            "12",
            null,
            PlantedSin,
            ExtractionActorKind.Accountant,
            ActorReference,
            Instant,
            Guid.NewGuid()));
    }

    // ------------------------------------------------------------- helpers

    private static TrustedFirmId Firm() => TrustedFirmId.FromGuid(SyntheticFixtures.FirmId);

    private static async Task<RelationalTestContext> SeededDatabaseAsync()
    {
        var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        context.Add(SyntheticFixtures.Firm());
        context.Add(SyntheticFixtures.Firm(SyntheticFixtures.OtherFirmId, "Example Firm B"));
        await context.SaveChangesAsync();

        var store = new Persistence.Repositories.SqlIntakeStore(context);
        await store.CreateAsync(SyntheticFixtures.IntakeCase(), CancellationToken.None);
        return database;
    }

    private static ExtractionAuditEventRecord Audit(long sequence, ExtractionAuditKind kind) =>
        new(Guid.NewGuid(), sequence, kind, ExtractionActorKind.Accountant, ActorReference, Instant);

    private static ExtractionBoxValue Box(string code, string? value)
    {
        Assert.True(ExtractionBoxValue.TryCreate(code, value, out var boxValue));
        return boxValue!;
    }

    private static ExtractionVersion Version(
        int versionNumber = 1,
        ClassificationOutcome? classification = null,
        IReadOnlyList<ExtractionBoxValue>? boxes = null,
        ExtractionAcceptanceState acceptance =
            ExtractionAcceptanceState.AwaitingAccountantDecision)
    {
        if (classification is null)
        {
            Assert.True(ClassificationOutcome.TryAutomated(
                DocumentClassification.T4Slip, out classification));
        }

        Assert.True(FieldValidationOutcome.TryCreate(
            ExtractionWorkflowFields.BoxCode, FieldOutcomeKind.Extracted, out var outcome));

        return new ExtractionVersion(
            Guid.Parse($"6161616{versionNumber}-6161-4161-8161-616161616161"),
            DocumentId,
            versionNumber,
            classification!,
            new ExtractionProvenance
            {
                ProviderLabel = "deterministic-test-adapter",
                Confidence = 0.75m,
            },
            "Acme Manufacturing",
            "Casey Taxpayer",
            "12 Main Street",
            "9 Bay Road",
            boxes ?? [Box("14", "50000.00")],
            [outcome!],
            acceptance,
            null,
            Instant);
    }

    private static DuplicateResolutionRecord Resolution(Guid candidateId, string resolutionId) =>
        new(
            Guid.Parse(resolutionId),
            candidateId,
            DuplicateResolutionKind.DistinctDocuments,
            ExtractionActorKind.Accountant,
            ActorReference,
            Instant);

    private static async Task AppendAsync(
        SqlExtractionStore store,
        ExtractionVersion version,
        long sequence = 1)
    {
        Assert.Equal(
            ExtractionStoreWriteCode.Success,
            await store.AppendExtractionVersionAsync(
                Firm(),
                CaseId,
                ParticipantId,
                version,
                RetentionUntilUtc,
                Audit(sequence, ExtractionAuditKind.ExtractionRecorded),
                CancellationToken.None));
    }

    private static async Task AppendMedicalAsync(
        SqlExtractionStore store,
        int version,
        string amount = "$120.50",
        CurrencyDeclaration? currency = null,
        long sequence = 1)
    {
        Assert.Equal(
            ExtractionStoreWriteCode.Success,
            await store.AppendMedicalEvidenceVersionAsync(
                Firm(),
                CaseId,
                ParticipantId,
                new MedicalEvidenceVersion(
                    Guid.Parse($"7171717{version}-7171-4171-8171-717171717171"),
                    DocumentId,
                    version,
                    "14 mars 2025",
                    amount,
                    currency ?? CurrencyDeclaration.FromPrintedCode("CAD"),
                    "Bay Street Dental",
                    "Casey Taxpayer",
                    "R-4471",
                    new ExtractionProvenance { ProviderLabel = "deterministic-test-adapter" },
                    [],
                    ExtractionAcceptanceState.AwaitingAccountantDecision,
                    null,
                    Instant),
                RetentionUntilUtc,
                Audit(sequence, ExtractionAuditKind.MedicalEvidenceRecorded),
                CancellationToken.None));
    }

    /// <summary>Appends one row into every extraction table.</summary>
    private static async Task AppendEveryKindAsync(SqlExtractionStore store)
    {
        var candidateId = Guid.Parse("31313131-3131-4131-8131-313131313131");

        await AppendAsync(store, Version(), sequence: 1);
        await AppendMedicalAsync(store, version: 1, sequence: 2);

        await store.AppendCorrectionAsync(
            Firm(),
            CaseId,
            new ExtractionCorrectionRecord(
                Guid.Parse("52525252-5252-4252-8252-525252525252"),
                DocumentId,
                CorrectionField.T4BoxValue,
                "14",
                "50000.00",
                "51000.00",
                ExtractionActorKind.Accountant,
                ActorReference,
                Instant,
                Guid.Parse("51515151-5151-4151-8151-515151515151")),
            RetentionUntilUtc,
            Audit(3, ExtractionAuditKind.CorrectionRecorded),
            CancellationToken.None);

        await store.AppendClassificationOverrideAsync(
            Firm(),
            CaseId,
            new ClassificationOverrideRecord(
                Guid.Parse("81818181-8181-4181-8181-818181818181"),
                DocumentId,
                DocumentClassification.Unsupported,
                DocumentClassification.T4Slip,
                ExtractionActorKind.Accountant,
                ActorReference,
                Instant),
            RetentionUntilUtc,
            Audit(4, ExtractionAuditKind.ClassificationOverridden),
            CancellationToken.None);

        await store.AppendDuplicateCandidateAsync(
            Firm(),
            CaseId,
            new DuplicateCandidateRecord(candidateId, DocumentId, OtherDocumentId, Instant),
            RetentionUntilUtc,
            Audit(5, ExtractionAuditKind.DuplicateFlagged),
            CancellationToken.None);

        await store.AppendDuplicateResolutionAsync(
            Firm(),
            CaseId,
            Resolution(candidateId, "32323232-3232-4232-8232-323232323232"),
            RetentionUntilUtc,
            Audit(6, ExtractionAuditKind.DuplicateResolved),
            CancellationToken.None);

        await store.AppendAcceptanceAsync(
            Firm(),
            CaseId,
            new ExtractionAcceptanceRecord(
                Guid.Parse("91919191-9191-4191-8191-919191919191"),
                ExtractionAcceptanceScope.Document,
                DocumentId,
                Guid.Parse("61616161-6161-4161-8161-616161616161"),
                ExtractionActorKind.Accountant,
                ActorReference,
                Instant),
            RetentionUntilUtc,
            Audit(7, ExtractionAuditKind.DocumentAccepted),
            CancellationToken.None);

        await store.AppendAcceptanceAsync(
            Firm(),
            CaseId,
            new ExtractionAcceptanceRecord(
                Guid.Parse("92929292-9292-4292-8292-929292929292"),
                ExtractionAcceptanceScope.Case,
                null,
                null,
                ExtractionActorKind.Accountant,
                ActorReference,
                Instant),
            RetentionUntilUtc,
            Audit(8, ExtractionAuditKind.CaseAccepted),
            CancellationToken.None);
    }

    private static async Task<string> SnapshotVersionRowAsync(TaxAiDbContext context, int version)
    {
        var entity = await context.Set<ExtractionVersionEntity>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Version == version);

        return string.Join(
            "|",
            typeof(ExtractionVersionEntity)
                .GetProperties()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => $"{property.Name}={property.GetValue(entity)}"));
    }

    private static async Task<string> SnapshotMedicalRowAsync(TaxAiDbContext context, int version)
    {
        var entity = await context.Set<MedicalEvidenceVersionEntity>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Version == version);

        return string.Join(
            "|",
            typeof(MedicalEvidenceVersionEntity)
                .GetProperties()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => $"{property.Name}={property.GetValue(entity)}"));
    }

    private static async Task<string> SnapshotCandidateRowAsync(
        TaxAiDbContext context,
        Guid candidateId)
    {
        var entity = await context.Set<ExtractionDuplicateCandidateEntity>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.CandidateId == candidateId);

        return string.Join(
            "|",
            typeof(ExtractionDuplicateCandidateEntity)
                .GetProperties()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => $"{property.Name}={property.GetValue(entity)}"));
    }

    /// <summary>
    /// Every string and identifier value in every extraction table, read back
    /// through the model rather than through the store, so a value the store
    /// declines to return is still captured.
    /// </summary>
    private static async Task<string[]> ReadEveryTextAndIdentifierAsync(TaxAiDbContext context)
    {
        var captured = new List<string>();

        foreach (var clrType in ExtractionEntityTypes)
        {
            var set = (IQueryable<object>)typeof(DbContext)
                .GetMethods()
                .Single(method =>
                    method.Name == nameof(DbContext.Set) &&
                    method.IsGenericMethod &&
                    method.GetParameters().Length == 0)
                .MakeGenericMethod(clrType)
                .Invoke(context, null)!;

            foreach (var row in await set.ToListAsync())
            {
                foreach (var property in clrType.GetProperties(
                             BindingFlags.Instance | BindingFlags.Public))
                {
                    if (property.GetValue(row) is { } value)
                    {
                        captured.Add(value.ToString() ?? string.Empty);
                    }
                }
            }
        }

        return captured.ToArray();
    }

    internal static readonly Type[] ExtractionEntityTypes =
    [
        typeof(ExtractionVersionEntity),
        typeof(ExtractionBoxValueEntity),
        typeof(ExtractionFieldOutcomeEntity),
        typeof(MedicalEvidenceVersionEntity),
        typeof(ExtractionCorrectionEntity),
        typeof(ExtractionClassificationOverrideEntity),
        typeof(ExtractionDuplicateCandidateEntity),
        typeof(ExtractionDuplicateResolutionEntity),
        typeof(ExtractionAcceptanceEntity),
        typeof(ExtractionAuditEventEntity),
    ];

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
