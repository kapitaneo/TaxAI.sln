using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Persistence.Entities;
using Tenancy.Models;
using Tenancy.Services;

namespace Persistence.Repositories;

/// <summary>
/// The relational implementation of the extraction store.
/// <para>
/// Every member appends and commits in one transaction, and every member is
/// scoped to a trusted firm and a server-stored case. A case that does not
/// belong to the firm produces <see cref="ExtractionStoreWriteCode.NotFound"/>
/// on a write and <c>null</c> on a read — the same answer a case that does not
/// exist produces, so neither can be used to probe for another firm's data.
/// </para>
/// <para>
/// There is no update, no delete, and no upsert anywhere in this class. A
/// conflicting append loses on a key or unique index and returns
/// <see cref="ExtractionStoreWriteCode.Conflict"/> with nothing partial
/// committed; it never overwrites the row that was already there.
/// </para>
/// <para>
/// Reads rehydrate through the workflow's own public factories, which is a
/// safety property rather than a convenience: <c>ExtractionBoxValue</c> refuses
/// the SIN box at construction, so this store could not rehydrate a Box 12 row
/// even if one somehow existed. None does — no member writes one.
/// </para>
/// </summary>
internal sealed class SqlExtractionStore : IExtractionStore
{
    private readonly TaxAiDbContext _context;

    public SqlExtractionStore(TaxAiDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public Task<ExtractionStoreWriteCode> AppendExtractionVersionAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid participantId,
        ExtractionVersion version,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);

        return AppendAsync(firm, caseId, auditEvent, retentionUntilUtc, firmId =>
        {
            RequireIdentifier(participantId, nameof(participantId));
            var classification = version.Classification;

            _context.Add(new ExtractionVersionEntity
            {
                FirmId = firmId,
                CaseId = caseId,
                DocumentId = version.DocumentId,
                Version = version.Version,
                ExtractionVersionId = version.ExtractionVersionId,
                ParticipantId = participantId,
                AutomatedClassification = classification.AutomatedClassification,
                OverriddenClassification = classification.OverriddenClassification,
                OverrideActorReference = classification.OverrideActorReference,
                OverriddenAtUtc = classification.OverriddenAtUtc,
                EmployerDisplayName = version.EmployerDisplayName,
                EmployeeDisplayName = version.EmployeeDisplayName,
                ProviderLabel = version.Provenance?.ProviderLabel,
                Confidence = version.Provenance?.Confidence,
                AcceptanceState = version.AcceptanceState,
                PrecedingVersionId = version.PrecedingVersionId,
                RecordedAtUtc = version.RecordedAtUtc,
                RetentionUntilUtc = retentionUntilUtc,
            });

            for (var ordinal = 0; ordinal < version.BoxValues.Count; ordinal++)
            {
                var boxValue = version.BoxValues[ordinal];
                _context.Add(new ExtractionBoxValueEntity
                {
                    FirmId = firmId,
                    CaseId = caseId,
                    DocumentId = version.DocumentId,
                    Version = version.Version,
                    Ordinal = ordinal,
                    BoxCode = boxValue.Code,
                    PrintedValue = boxValue.Value,
                    RetentionUntilUtc = retentionUntilUtc,
                });
            }

            for (var ordinal = 0; ordinal < version.FieldOutcomes.Count; ordinal++)
            {
                var outcome = version.FieldOutcomes[ordinal];
                _context.Add(new ExtractionFieldOutcomeEntity
                {
                    FirmId = firmId,
                    CaseId = caseId,
                    DocumentId = version.DocumentId,
                    Version = version.Version,
                    Ordinal = ordinal,
                    FieldKey = outcome.Field,
                    OutcomeKind = outcome.Kind,
                    RetentionUntilUtc = retentionUntilUtc,
                });
            }
        }, cancellationToken);
    }

    public Task<ExtractionStoreWriteCode> AppendMedicalEvidenceVersionAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid participantId,
        MedicalEvidenceVersion version,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);

        return AppendAsync(firm, caseId, auditEvent, retentionUntilUtc, firmId =>
        {
            RequireIdentifier(participantId, nameof(participantId));

            _context.Add(new MedicalEvidenceVersionEntity
            {
                FirmId = firmId,
                CaseId = caseId,
                DocumentId = version.DocumentId,
                Version = version.Version,
                MedicalEvidenceVersionId = version.MedicalEvidenceVersionId,
                ParticipantId = participantId,
                ServiceDateAsPrinted = version.ServiceDateAsPrinted,
                GrossAmountAsPrinted = version.GrossAmountAsPrinted,
                CurrencyIsKnown = version.Currency.IsKnown,
                CurrencyCodeAsPrinted = version.Currency.CodeAsPrinted,
                ProviderDisplayName = version.ProviderDisplayName,
                PatientDisplayName = version.PatientDisplayName,
                ReceiptReference = version.ReceiptReference,
                ProviderLabel = version.Provenance?.ProviderLabel,
                Confidence = version.Provenance?.Confidence,
                AcceptanceState = version.AcceptanceState,
                PrecedingVersionId = version.PrecedingVersionId,
                RecordedAtUtc = version.RecordedAtUtc,
                RetentionUntilUtc = retentionUntilUtc,
            });

            for (var ordinal = 0; ordinal < version.FieldOutcomes.Count; ordinal++)
            {
                var outcome = version.FieldOutcomes[ordinal];
                _context.Add(new ExtractionFieldOutcomeEntity
                {
                    FirmId = firmId,
                    CaseId = caseId,
                    DocumentId = version.DocumentId,
                    Version = version.Version,
                    Ordinal = ordinal,
                    FieldKey = outcome.Field,
                    OutcomeKind = outcome.Kind,
                    RetentionUntilUtc = retentionUntilUtc,
                });
            }
        }, cancellationToken);
    }

    public Task<ExtractionStoreWriteCode> AppendCorrectionAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionCorrectionRecord correction,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(correction);

        return AppendAsync(firm, caseId, auditEvent, retentionUntilUtc, firmId =>
            _context.Add(new ExtractionCorrectionEntity
            {
                FirmId = firmId,
                CaseId = caseId,
                CorrectionId = correction.CorrectionId,
                DocumentId = correction.DocumentId,
                Field = correction.Field,
                BoxCode = correction.BoxCode,
                PriorValue = correction.PriorValue,
                CorrectedValue = correction.CorrectedValue,
                ActorKind = correction.ActorKind,
                ActorReference = correction.ActorReference,
                SuppliedAtUtc = correction.SuppliedAtUtc,
                PrecedingVersionId = correction.PrecedingVersionId,
                RetentionUntilUtc = retentionUntilUtc,
            }), cancellationToken);
    }

    public Task<ExtractionStoreWriteCode> AppendClassificationOverrideAsync(
        TrustedFirmId firm,
        Guid caseId,
        ClassificationOverrideRecord classificationOverride,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(classificationOverride);

        return AppendAsync(firm, caseId, auditEvent, retentionUntilUtc, firmId =>
            _context.Add(new ExtractionClassificationOverrideEntity
            {
                FirmId = firmId,
                CaseId = caseId,
                OverrideId = classificationOverride.OverrideId,
                DocumentId = classificationOverride.DocumentId,
                AutomatedClassification = classificationOverride.AutomatedClassification,
                OverriddenClassification = classificationOverride.OverriddenClassification,
                ActorKind = classificationOverride.ActorKind,
                ActorReference = classificationOverride.ActorReference,
                OverriddenAtUtc = classificationOverride.OverriddenAtUtc,
                RetentionUntilUtc = retentionUntilUtc,
            }), cancellationToken);
    }

    public Task<ExtractionStoreWriteCode> AppendDuplicateCandidateAsync(
        TrustedFirmId firm,
        Guid caseId,
        DuplicateCandidateRecord candidate,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return AppendAsync(firm, caseId, auditEvent, retentionUntilUtc, firmId =>
            _context.Add(new ExtractionDuplicateCandidateEntity
            {
                FirmId = firmId,
                CaseId = caseId,
                CandidateId = candidate.CandidateId,
                DocumentId = candidate.DocumentId,
                ComparedDocumentId = candidate.ComparedDocumentId,
                DetectedAtUtc = candidate.DetectedAtUtc,
                RetentionUntilUtc = retentionUntilUtc,
            }), cancellationToken);
    }

    public Task<ExtractionStoreWriteCode> AppendDuplicateResolutionAsync(
        TrustedFirmId firm,
        Guid caseId,
        DuplicateResolutionRecord resolution,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        return AppendAsync(firm, caseId, auditEvent, retentionUntilUtc, firmId =>
            _context.Add(new ExtractionDuplicateResolutionEntity
            {
                FirmId = firmId,
                CaseId = caseId,
                ResolutionId = resolution.ResolutionId,
                CandidateId = resolution.CandidateId,
                Resolution = resolution.Resolution,
                ActorKind = resolution.ActorKind,
                ActorReference = resolution.ActorReference,
                ResolvedAtUtc = resolution.ResolvedAtUtc,
                RetentionUntilUtc = retentionUntilUtc,
            }), cancellationToken);
    }

    public Task<ExtractionStoreWriteCode> AppendAcceptanceAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionAcceptanceRecord acceptance,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acceptance);

        return AppendAsync(firm, caseId, auditEvent, retentionUntilUtc, firmId =>
            _context.Add(new ExtractionAcceptanceEntity
            {
                FirmId = firmId,
                CaseId = caseId,
                AcceptanceId = acceptance.AcceptanceId,
                Scope = acceptance.Scope,
                DocumentId = acceptance.DocumentId,
                AcceptedVersionId = acceptance.AcceptedVersionId,
                ActorKind = acceptance.ActorKind,
                ActorReference = acceptance.ActorReference,
                AcceptedAtUtc = acceptance.AcceptedAtUtc,
                RetentionUntilUtc = retentionUntilUtc,
            }), cancellationToken);
    }

    public Task<ExtractionStoreWriteCode> AppendAuditEventAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionAuditEventRecord auditEvent,
        DateTimeOffset retentionUntilUtc,
        CancellationToken cancellationToken)
    {
        return AppendAsync(
            firm,
            caseId,
            auditEvent,
            retentionUntilUtc,
            _ => { },
            cancellationToken);
    }

    public async Task<StoredDocumentExtraction?> FindDocumentAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);

        var stored = await LoadCaseAsync(firm.Value, caseId, cancellationToken)
            .ConfigureAwait(false);

        return stored?.Documents
            .FirstOrDefault(document => document.DocumentId == documentId);
    }

    public async Task<StoredCaseExtractions?> FindCaseAsync(
        TrustedFirmId firm,
        Guid caseId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);

        return await LoadCaseAsync(firm.Value, caseId, cancellationToken)
            .ConfigureAwait(false);
    }

    // ------------------------------------------------------------- writing

    private async Task<ExtractionStoreWriteCode> AppendAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionAuditEventRecord auditEvent,
        DateTimeOffset retentionUntilUtc,
        Action<Guid> append,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        ArgumentNullException.ThrowIfNull(auditEvent);
        ArgumentNullException.ThrowIfNull(append);

        if (UtcInstantGuard.Validate(
                retentionUntilUtc, nameof(retentionUntilUtc)) is { } retentionError)
        {
            throw new ArgumentException(
                retentionError.Message, nameof(retentionUntilUtc));
        }

        // The case must already exist in this firm. Checked before anything is
        // staged, so a foreign-firm write neither inserts a row nor tells the
        // caller which of the two reasons it failed for.
        var caseExists = await _context.Set<IntakeCaseEntity>()
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.FirmId == firm.Value && candidate.CaseId == caseId,
                cancellationToken)
            .ConfigureAwait(false);

        if (!caseExists)
        {
            return ExtractionStoreWriteCode.NotFound;
        }

        await using var transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            append(firm.Value);

            _context.Add(new ExtractionAuditEventEntity
            {
                FirmId = firm.Value,
                CaseId = caseId,
                EventId = auditEvent.EventId,
                Sequence = auditEvent.Sequence,
                Kind = auditEvent.Kind,
                ActorKind = auditEvent.ActorKind,
                ActorReference = auditEvent.ActorReference,
                OccurredAtUtc = auditEvent.OccurredAtUtc,
                RetentionUntilUtc = retentionUntilUtc,
            });

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ExtractionStoreWriteCode.Success;
        }
        catch (DbUpdateException)
        {
            await RollbackAsync(transaction).ConfigureAwait(false);
            return ExtractionStoreWriteCode.Conflict;
        }
    }

    private async Task RollbackAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // The transaction was already finished by the failed save. Nothing
            // was committed either way, which is the property that matters.
        }

        _context.ChangeTracker.Clear();
    }

    // ------------------------------------------------------------- reading

    private async Task<StoredCaseExtractions?> LoadCaseAsync(
        Guid firmId,
        Guid caseId,
        CancellationToken cancellationToken)
    {
        var caseExists = await _context.Set<IntakeCaseEntity>()
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.FirmId == firmId && candidate.CaseId == caseId,
                cancellationToken)
            .ConfigureAwait(false);

        if (!caseExists)
        {
            return null;
        }

        var versions = await ScopedAsync<ExtractionVersionEntity>(firmId, caseId)
            .OrderBy(entity => entity.DocumentId)
            .ThenBy(entity => entity.Version)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var boxValues = await ScopedAsync<ExtractionBoxValueEntity>(firmId, caseId)
            .OrderBy(entity => entity.Ordinal)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var fieldOutcomes = await ScopedAsync<ExtractionFieldOutcomeEntity>(firmId, caseId)
            .OrderBy(entity => entity.Ordinal)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var medicalVersions = await ScopedAsync<MedicalEvidenceVersionEntity>(firmId, caseId)
            .OrderBy(entity => entity.DocumentId)
            .ThenBy(entity => entity.Version)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var corrections = (await ScopedAsync<ExtractionCorrectionEntity>(firmId, caseId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
            .OrderBy(entity => entity.SuppliedAtUtc)
            .ThenBy(entity => entity.CorrectionId)
            .ToArray();

        var overrides =
            (await ScopedAsync<ExtractionClassificationOverrideEntity>(firmId, caseId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
            .OrderBy(entity => entity.OverriddenAtUtc)
            .ThenBy(entity => entity.OverrideId)
            .ToArray();

        var candidates = (await ScopedAsync<ExtractionDuplicateCandidateEntity>(firmId, caseId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
            .OrderBy(entity => entity.DetectedAtUtc)
            .ThenBy(entity => entity.CandidateId)
            .ToArray();

        var resolutions =
            (await ScopedAsync<ExtractionDuplicateResolutionEntity>(firmId, caseId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
            .OrderBy(entity => entity.ResolvedAtUtc)
            .ThenBy(entity => entity.ResolutionId)
            .ToArray();

        var acceptances = (await ScopedAsync<ExtractionAcceptanceEntity>(firmId, caseId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
            .OrderBy(entity => entity.AcceptedAtUtc)
            .ThenBy(entity => entity.AcceptanceId)
            .ToArray();

        var auditEvents = await ScopedAsync<ExtractionAuditEventEntity>(firmId, caseId)
            .OrderBy(entity => entity.Sequence)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var candidateIds = candidates
            .ToDictionary(candidate => candidate.CandidateId, candidate => candidate.DocumentId);

        var documentIds = versions.Select(entity => entity.DocumentId)
            .Concat(medicalVersions.Select(entity => entity.DocumentId))
            .Concat(corrections.Select(entity => entity.DocumentId))
            .Concat(overrides.Select(entity => entity.DocumentId))
            .Concat(candidates.Select(entity => entity.DocumentId))
            .Concat(acceptances
                .Where(entity => entity.DocumentId is not null)
                .Select(entity => entity.DocumentId!.Value))
            .Distinct()
            .OrderBy(documentId => documentId)
            .ToArray();

        var documents = documentIds
            .Select(documentId => new StoredDocumentExtraction(
                documentId,
                ParticipantOf(versions, medicalVersions, documentId),
                versions
                    .Where(entity => entity.DocumentId == documentId)
                    .Select(entity => Rehydrate(entity, boxValues, fieldOutcomes))
                    .ToArray(),
                medicalVersions
                    .Where(entity => entity.DocumentId == documentId)
                    .Select(entity => Rehydrate(entity, fieldOutcomes))
                    .ToArray(),
                corrections
                    .Where(entity => entity.DocumentId == documentId)
                    .Select(Rehydrate)
                    .ToArray(),
                overrides
                    .Where(entity => entity.DocumentId == documentId)
                    .Select(Rehydrate)
                    .ToArray(),
                candidates
                    .Where(entity => entity.DocumentId == documentId)
                    .Select(Rehydrate)
                    .ToArray(),
                resolutions
                    .Where(entity =>
                        candidateIds.TryGetValue(entity.CandidateId, out var owner) &&
                        owner == documentId)
                    .Select(Rehydrate)
                    .ToArray(),
                acceptances
                    .Where(entity => entity.DocumentId == documentId)
                    .Select(Rehydrate)
                    .ToArray()))
            .ToArray();

        return new StoredCaseExtractions(
            caseId,
            documents,
            acceptances
                .Where(entity => entity.Scope == ExtractionAcceptanceScope.Case)
                .Select(Rehydrate)
                .ToArray(),
            auditEvents.Select(Rehydrate).ToArray());
    }

    private IQueryable<TEntity> ScopedAsync<TEntity>(Guid firmId, Guid caseId)
        where TEntity : class =>
        _context.Set<TEntity>()
            .AsNoTracking()
            .Where(entity =>
                EF.Property<Guid>(entity, "FirmId") == firmId &&
                EF.Property<Guid>(entity, "CaseId") == caseId);

    private static Guid ParticipantOf(
        IReadOnlyList<ExtractionVersionEntity> versions,
        IReadOnlyList<MedicalEvidenceVersionEntity> medicalVersions,
        Guid documentId) =>
        versions.FirstOrDefault(entity => entity.DocumentId == documentId)?.ParticipantId ??
        medicalVersions.FirstOrDefault(entity => entity.DocumentId == documentId)?.ParticipantId ??
        Guid.Empty;

    /// <summary>
    /// Rebuilds one version through the workflow's own factories. A row that
    /// could not pass them would not become a value here — it would throw, which
    /// is the correct outcome for a row this store never wrote.
    /// </summary>
    private static ExtractionVersion Rehydrate(
        ExtractionVersionEntity entity,
        IReadOnlyList<ExtractionBoxValueEntity> boxValues,
        IReadOnlyList<ExtractionFieldOutcomeEntity> fieldOutcomes)
    {
        if (!ClassificationOutcome.TryAutomated(
                entity.AutomatedClassification, out var classification))
        {
            throw new InvalidOperationException(
                "A stored classification is not a defined value.");
        }

        if (entity.OverriddenClassification is { } overridden &&
            entity.OverrideActorReference is { } actor &&
            entity.OverriddenAtUtc is { } overriddenAtUtc)
        {
            if (!classification!.TryOverride(
                    overridden, actor, overriddenAtUtc, out var overriddenOutcome))
            {
                throw new InvalidOperationException(
                    "A stored classification override is not a supported value.");
            }

            classification = overriddenOutcome;
        }

        var boxes = boxValues
            .Where(box =>
                box.DocumentId == entity.DocumentId && box.Version == entity.Version)
            .OrderBy(box => box.Ordinal)
            .Select(box =>
                ExtractionBoxValue.TryCreate(box.BoxCode, box.PrintedValue, out var value)
                    ? value!
                    : throw new InvalidOperationException(
                        "A stored box value is not a permitted box."))
            .ToArray();

        var outcomes = fieldOutcomes
            .Where(outcome =>
                outcome.DocumentId == entity.DocumentId && outcome.Version == entity.Version)
            .OrderBy(outcome => outcome.Ordinal)
            .Select(outcome =>
                FieldValidationOutcome.TryCreate(
                    outcome.FieldKey, outcome.OutcomeKind, out var value)
                    ? value!
                    : throw new InvalidOperationException(
                        "A stored field outcome is not a permitted outcome."))
            .ToArray();

        return new ExtractionVersion(
            entity.ExtractionVersionId,
            entity.DocumentId,
            entity.Version,
            classification!,
            StoredExtractionProvenance.FromStorage(entity.ProviderLabel, entity.Confidence),
            entity.EmployerDisplayName,
            entity.EmployeeDisplayName,
            // Addresses are optional in-memory metadata and are never stored, so
            // a rehydrated version reports them absent rather than inventing one.
            null,
            null,
            boxes,
            outcomes,
            entity.AcceptanceState,
            entity.PrecedingVersionId,
            entity.RecordedAtUtc);
    }

    private static MedicalEvidenceVersion Rehydrate(
        MedicalEvidenceVersionEntity entity,
        IReadOnlyList<ExtractionFieldOutcomeEntity> fieldOutcomes) =>
        new(
            entity.MedicalEvidenceVersionId,
            entity.DocumentId,
            entity.Version,
            entity.ServiceDateAsPrinted,
            entity.GrossAmountAsPrinted,
            entity.CurrencyIsKnown
                ? CurrencyDeclaration.FromPrintedCode(entity.CurrencyCodeAsPrinted)
                : CurrencyDeclaration.Unknown,
            entity.ProviderDisplayName,
            entity.PatientDisplayName,
            entity.ReceiptReference,
            StoredExtractionProvenance.FromStorage(entity.ProviderLabel, entity.Confidence),
            fieldOutcomes
                .Where(outcome =>
                    outcome.DocumentId == entity.DocumentId &&
                    outcome.Version == entity.Version)
                .OrderBy(outcome => outcome.Ordinal)
                .Select(outcome =>
                    FieldValidationOutcome.TryCreate(
                        outcome.FieldKey, outcome.OutcomeKind, out var value)
                        ? value!
                        : throw new InvalidOperationException(
                            "A stored medical field outcome is not a permitted outcome."))
                .ToArray(),
            entity.AcceptanceState,
            entity.PrecedingVersionId,
            entity.RecordedAtUtc);

    private static ExtractionCorrectionRecord Rehydrate(ExtractionCorrectionEntity entity) =>
        new(
            entity.CorrectionId,
            entity.DocumentId,
            entity.Field,
            entity.BoxCode,
            entity.PriorValue,
            entity.CorrectedValue,
            entity.ActorKind,
            entity.ActorReference,
            entity.SuppliedAtUtc,
            entity.PrecedingVersionId);

    private static ClassificationOverrideRecord Rehydrate(
        ExtractionClassificationOverrideEntity entity) =>
        new(
            entity.OverrideId,
            entity.DocumentId,
            entity.AutomatedClassification,
            entity.OverriddenClassification,
            entity.ActorKind,
            entity.ActorReference,
            entity.OverriddenAtUtc);

    private static DuplicateCandidateRecord Rehydrate(
        ExtractionDuplicateCandidateEntity entity) =>
        new(
            entity.CandidateId,
            entity.DocumentId,
            entity.ComparedDocumentId,
            entity.DetectedAtUtc);

    private static DuplicateResolutionRecord Rehydrate(
        ExtractionDuplicateResolutionEntity entity) =>
        new(
            entity.ResolutionId,
            entity.CandidateId,
            entity.Resolution,
            entity.ActorKind,
            entity.ActorReference,
            entity.ResolvedAtUtc);

    private static ExtractionAcceptanceRecord Rehydrate(ExtractionAcceptanceEntity entity) =>
        new(
            entity.AcceptanceId,
            entity.Scope,
            entity.DocumentId,
            entity.AcceptedVersionId,
            entity.ActorKind,
            entity.ActorReference,
            entity.AcceptedAtUtc);

    private static ExtractionAuditEventRecord Rehydrate(ExtractionAuditEventEntity entity) =>
        new(
            entity.EventId,
            entity.Sequence,
            entity.Kind,
            entity.ActorKind,
            entity.ActorReference,
            entity.OccurredAtUtc);

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The identifier is absent.", parameterName);
        }
    }
}
