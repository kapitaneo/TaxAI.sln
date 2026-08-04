using CaseProcessing.Models;
using CaseProcessing.Services;
using CaseReview.Services;
using ClientIntake.Contracts;
using ClientIntake.Models;
using DocumentExtraction.Models;
using DocumentExtraction.Ports;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using TaxLogicEngine.Models;
using Tenancy.Contracts;
using Tenancy.Models;
using Tenancy.Services;

namespace ExtractionWorkflow.Services;

/// <summary>
/// Coordinates the explicit human-review actions over append-only extraction
/// history. It owns no tax rule: corrected T4 input always returns through the
/// existing envelope gate, parser, mapper, processor, assembler, and tenancy
/// projection in that order.
/// </summary>
public sealed class ExtractionReviewWorkflow : IExtractionReviewWorkflow
{
    private readonly IIntakeStore _intake;
    private readonly IAccountantIntakeWorkflow _accountantIntake;
    private readonly IExtractionStore _store;
    private readonly IExtractionContentReader _contentReader;
    private readonly IDocumentIntelligenceProvider _provider;
    private readonly IDocumentExtractionIngress _ingress;
    private readonly T4CaseProcessor _processor;
    private readonly IReviewRecordRepository _reviewRecords;
    private readonly IOpaqueIdentifierSource _identifiers;
    private readonly ExtractionWorkflowOptions _options;

    public ExtractionReviewWorkflow(
        IIntakeStore intake,
        IAccountantIntakeWorkflow accountantIntake,
        IExtractionStore store,
        IExtractionContentReader contentReader,
        IDocumentIntelligenceProvider provider,
        IDocumentExtractionIngress ingress,
        T4CaseProcessor processor,
        IReviewRecordRepository reviewRecords,
        IOpaqueIdentifierSource identifiers,
        ExtractionWorkflowOptions options)
    {
        ArgumentNullException.ThrowIfNull(intake);
        ArgumentNullException.ThrowIfNull(accountantIntake);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(contentReader);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(ingress);
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(reviewRecords);
        ArgumentNullException.ThrowIfNull(identifiers);

        _intake = intake;
        _accountantIntake = accountantIntake;
        _store = store;
        _contentReader = contentReader;
        _provider = provider;
        _ingress = ingress;
        _processor = processor;
        _reviewRecords = reviewRecords;
        _identifiers = identifiers;
        _options = ExtractionWorkflowOptionsGuard.RequireValid(options);
    }

    public async Task<ExtractionResult<DocumentExtractionOutcome>> CorrectAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        ApplyExtractionCorrectionCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (command is null ||
            !ValidMutation(firm, accountantActorReference, command.CaseId, nowUtc) ||
            command.DocumentId == Guid.Empty ||
            !ValidCorrectionShape(command))
        {
            return ExtractionResult<DocumentExtractionOutcome>.Failure(
                ExtractionResultCode.Invalid);
        }

        try
        {
            return await CorrectCoreAsync(
                    firm, accountantActorReference, command, nowUtc, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ExtractionResult<DocumentExtractionOutcome>.Failure(
                ExtractionResultCode.Invalid);
        }
    }

    public async Task<ExtractionResult<DocumentExtractionView>> OverrideClassificationAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        OverrideExtractionClassificationCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (command is null ||
            !ValidMutation(firm, accountantActorReference, command.CaseId, nowUtc) ||
            command.DocumentId == Guid.Empty ||
            !ClassificationOverridePolicy.IsSupportedTarget(command.TargetClassification))
        {
            return ViewFailure(ExtractionResultCode.Invalid);
        }

        try
        {
            var intakeCase = await _intake
                .FindAsync(firm, command.CaseId, cancellationToken)
                .ConfigureAwait(false);
            if (intakeCase is null ||
                DocumentOf(intakeCase, command.DocumentId) is null)
            {
                return ViewFailure(ExtractionResultCode.NotFound);
            }

            var extractions = await _store
                .FindCaseAsync(firm, command.CaseId, cancellationToken)
                .ConfigureAwait(false);
            if (extractions is null)
            {
                return ViewFailure(ExtractionResultCode.NotFound);
            }

            if (IsFinalized(intakeCase, extractions))
            {
                return ViewFailure(ExtractionResultCode.Conflict);
            }

            var document = StoredExtractionOf(extractions, command.DocumentId);
            var automated = document is null
                ? DocumentClassification.Unsupported
                : AutomatedClassification(document);
            if (!ClassificationOverridePolicy.TryCreate(
                    _identifiers.NewIdentifier(),
                    command.DocumentId,
                    automated,
                    command.TargetClassification,
                    accountantActorReference,
                    nowUtc,
                    out var classificationOverride))
            {
                return ViewFailure(ExtractionResultCode.Invalid);
            }

            var write = await _store.AppendClassificationOverrideAsync(
                    firm,
                    command.CaseId,
                    classificationOverride!,
                    RetentionPolicy.ComputeRetentionUntilUtc(intakeCase.TaxYear),
                    Audit(
                        ExtractionAuditKind.ClassificationOverridden,
                        NextSequence(extractions),
                        accountantActorReference,
                        nowUtc),
                    cancellationToken)
                .ConfigureAwait(false);

            return write == ExtractionStoreWriteCode.Success
                ? await ProjectViewAsync(
                        firm, command.CaseId, command.DocumentId, cancellationToken)
                    .ConfigureAwait(false)
                : ViewFailure(Map(write));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ViewFailure(ExtractionResultCode.Invalid);
        }
    }

    public async Task<ExtractionResult<DocumentExtractionView>> ResolveDuplicateAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        ResolveExtractionDuplicateCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (command is null ||
            !ValidMutation(firm, accountantActorReference, command.CaseId, nowUtc) ||
            command.CandidateId == Guid.Empty ||
            !Enum.IsDefined(command.Resolution))
        {
            return ViewFailure(ExtractionResultCode.Invalid);
        }

        try
        {
            var intakeCase = await _intake.FindAsync(firm, command.CaseId, cancellationToken)
                .ConfigureAwait(false);
            var extractions = await _store.FindCaseAsync(
                    firm, command.CaseId, cancellationToken)
                .ConfigureAwait(false);

            if (intakeCase is null || extractions is null)
            {
                return ViewFailure(ExtractionResultCode.NotFound);
            }

            if (IsFinalized(intakeCase, extractions))
            {
                return ViewFailure(ExtractionResultCode.Conflict);
            }

            var owner = FindCandidateOwner(extractions, command.CandidateId);
            if (owner is null)
            {
                return ViewFailure(ExtractionResultCode.NotFound);
            }

            var candidate = owner.DuplicateCandidates
                .First(item => item.CandidateId == command.CandidateId);

            if (!DuplicateResolutionPolicy.TryCreate(
                    _identifiers.NewIdentifier(),
                    candidate,
                    owner.DuplicateResolutions,
                    command.Resolution,
                    accountantActorReference,
                    nowUtc,
                    out var resolution))
            {
                return ViewFailure(ExtractionResultCode.Conflict);
            }

            var write = await _store.AppendDuplicateResolutionAsync(
                    firm,
                    command.CaseId,
                    resolution!,
                    RetentionPolicy.ComputeRetentionUntilUtc(intakeCase.TaxYear),
                    Audit(
                        ExtractionAuditKind.DuplicateResolved,
                        NextSequence(extractions),
                        accountantActorReference,
                        nowUtc),
                    cancellationToken)
                .ConfigureAwait(false);

            return write == ExtractionStoreWriteCode.Success
                ? await ProjectViewAsync(
                        firm, command.CaseId, owner.DocumentId, cancellationToken)
                    .ConfigureAwait(false)
                : ViewFailure(Map(write));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ViewFailure(ExtractionResultCode.Invalid);
        }
    }

    public async Task<ExtractionResult<DocumentExtractionView>> AcceptDocumentAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        AcceptExtractionDocumentCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (command is null ||
            !ValidMutation(firm, accountantActorReference, command.CaseId, nowUtc) ||
            command.DocumentId == Guid.Empty)
        {
            return ViewFailure(ExtractionResultCode.Invalid);
        }

        try
        {
            var context = await LoadDocumentAsync(
                    firm, command.CaseId, command.DocumentId, cancellationToken)
                .ConfigureAwait(false);

            if (context is null)
            {
                return ViewFailure(ExtractionResultCode.NotFound);
            }

            if (IsFinalized(context.IntakeCase, context.Extractions) ||
                CurrentAcceptedVersionId(context.DocumentExtraction) is not null)
            {
                return ViewFailure(ExtractionResultCode.Conflict);
            }

            if (ExtractionProjection.HasUnresolvedDuplicateCandidate(
                    context.DocumentExtraction) ||
                AcceptableVersionId(context.DocumentExtraction) is not { } versionId)
            {
                return ViewFailure(ExtractionResultCode.Invalid);
            }

            var write = await _store.AppendAcceptanceAsync(
                    firm,
                    command.CaseId,
                    new ExtractionAcceptanceRecord(
                        _identifiers.NewIdentifier(),
                        ExtractionAcceptanceScope.Document,
                        command.DocumentId,
                        versionId,
                        ExtractionActorKind.Accountant,
                        accountantActorReference,
                        nowUtc),
                    RetentionPolicy.ComputeRetentionUntilUtc(context.IntakeCase.TaxYear),
                    Audit(
                        ExtractionAuditKind.DocumentAccepted,
                        NextSequence(context.Extractions),
                        accountantActorReference,
                        nowUtc),
                    cancellationToken)
                .ConfigureAwait(false);

            return write == ExtractionStoreWriteCode.Success
                ? await ProjectViewAsync(
                        firm, command.CaseId, command.DocumentId, cancellationToken)
                    .ConfigureAwait(false)
                : ViewFailure(Map(write));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ViewFailure(ExtractionResultCode.Invalid);
        }
    }

    public async Task<ExtractionResult<FinalizedExtractionReview>> FinalizeCaseAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        FinalizeExtractionCaseCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (command is null ||
            !ValidMutation(firm, accountantActorReference, command.CaseId, nowUtc))
        {
            return FinalizeFailure(ExtractionResultCode.Invalid);
        }

        try
        {
            var intakeCase = await _intake.FindAsync(firm, command.CaseId, cancellationToken)
                .ConfigureAwait(false);
            var extractions = await _store.FindCaseAsync(
                    firm, command.CaseId, cancellationToken)
                .ConfigureAwait(false);

            if (intakeCase is null || extractions is null)
            {
                return FinalizeFailure(ExtractionResultCode.NotFound);
            }

            if (IsFinalized(intakeCase, extractions))
            {
                return FinalizeFailure(ExtractionResultCode.Conflict);
            }

            var accepted = new List<ProcessedT4Extraction?>();

            foreach (var document in extractions.Documents)
            {
                if (EffectiveClassification(document) != DocumentClassification.T4Slip ||
                    CurrentAcceptedVersionId(document) is null)
                {
                    continue;
                }

                var metadata = DocumentOf(intakeCase, document.DocumentId);
                if (metadata is null)
                {
                    return FinalizeFailure(ExtractionResultCode.NotFound);
                }

                var processed = await RebuildT4Async(
                        firm,
                        intakeCase,
                        metadata,
                        document,
                        additionalCorrection: null,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (processed is null || processed.IsFailure)
                {
                    return FinalizeFailure(ExtractionResultCode.Invalid);
                }

                accepted.Add(processed.Value);
            }

            var assembled = ReviewPackageAssembler.Assemble(accepted);
            if (assembled.IsFailure)
            {
                return FinalizeFailure(ExtractionResultCode.Invalid);
            }

            var projected = ReviewPackageProjection.Project(
                firm, assembled.Value!, nowUtc, _identifiers);
            if (projected.IsFailure)
            {
                return FinalizeFailure(ExtractionResultCode.Invalid);
            }

            var retention = RetentionPolicy.ComputeRetentionUntilUtc(intakeCase.TaxYear);
            var caseAcceptance = await _store.AppendAcceptanceAsync(
                    firm,
                    command.CaseId,
                    new ExtractionAcceptanceRecord(
                        _identifiers.NewIdentifier(),
                        ExtractionAcceptanceScope.Case,
                        documentId: null,
                        acceptedVersionId: null,
                        ExtractionActorKind.Accountant,
                        accountantActorReference,
                        nowUtc),
                    retention,
                    Audit(
                        ExtractionAuditKind.CaseAccepted,
                        NextSequence(extractions),
                        accountantActorReference,
                        nowUtc),
                    cancellationToken)
                .ConfigureAwait(false);

            if (caseAcceptance != ExtractionStoreWriteCode.Success)
            {
                return FinalizeFailure(Map(caseAcceptance));
            }

            var saved = await _reviewRecords.SaveAsync(
                    firm, projected.Value!, cancellationToken)
                .ConfigureAwait(false);
            if (saved.IsFailure)
            {
                return FinalizeFailure(ExtractionResultCode.Invalid);
            }

            var associated = await _accountantIntake.AssociateReviewAsync(
                    firm,
                    command.CaseId,
                    saved.Value!.RecordId,
                    accountantActorReference,
                    nowUtc,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!associated.IsSuccess)
            {
                return FinalizeFailure(Map(associated.Code));
            }

            var refreshed = await _store.FindCaseAsync(
                    firm, command.CaseId, cancellationToken)
                .ConfigureAwait(false);
            if (refreshed is null)
            {
                return FinalizeFailure(ExtractionResultCode.NotFound);
            }

            var auditWrite = await _store.AppendAuditEventAsync(
                    firm,
                    command.CaseId,
                    Audit(
                        ExtractionAuditKind.ReviewRecordCreated,
                        NextSequence(refreshed),
                        accountantActorReference,
                        nowUtc),
                    retention,
                    cancellationToken)
                .ConfigureAwait(false);
            if (auditWrite != ExtractionStoreWriteCode.Success)
            {
                return FinalizeFailure(Map(auditWrite));
            }

            return ExtractionResult<FinalizedExtractionReview>.Success(
                new FinalizedExtractionReview(
                    saved.Value.RecordId,
                    saved.Value.PackageVersion,
                    accepted.Count));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return FinalizeFailure(ExtractionResultCode.Invalid);
        }
    }

    public override string ToString() => nameof(ExtractionReviewWorkflow);

    private async Task<ExtractionResult<DocumentExtractionOutcome>> CorrectCoreAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        ApplyExtractionCorrectionCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var context = await LoadDocumentAsync(
                firm, command.CaseId, command.DocumentId, cancellationToken)
            .ConfigureAwait(false);

        if (context is null)
        {
            return CorrectionFailure(ExtractionResultCode.NotFound);
        }

        if (IsFinalized(context.IntakeCase, context.Extractions))
        {
            return CorrectionFailure(ExtractionResultCode.Conflict);
        }

        var latestId = CurrentVersionId(context.DocumentExtraction);
        if (latestId is null)
        {
            return CorrectionFailure(ExtractionResultCode.Invalid);
        }

        var metadata = DocumentOf(context.IntakeCase, command.DocumentId);
        if (metadata is null)
        {
            return CorrectionFailure(ExtractionResultCode.NotFound);
        }

        var effective = EffectiveClassification(context.DocumentExtraction);
        var retention = RetentionPolicy.ComputeRetentionUntilUtc(context.IntakeCase.TaxYear);
        var sequence = NextSequence(context.Extractions);

        if (effective == DocumentClassification.T4Slip)
        {
            return await CorrectT4Async(
                    firm,
                    accountantActorReference,
                    command,
                    context,
                    metadata,
                    latestId.Value,
                    retention,
                    sequence,
                    nowUtc,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (effective == DocumentClassification.MedicalExpenseEvidence)
        {
            return await CorrectMedicalAsync(
                    firm,
                    accountantActorReference,
                    command,
                    context,
                    metadata,
                    latestId.Value,
                    retention,
                    sequence,
                    nowUtc,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return CorrectionFailure(ExtractionResultCode.Invalid);
    }

    private async Task<ExtractionResult<DocumentExtractionOutcome>> CorrectT4Async(
        TrustedFirmId firm,
        Guid actor,
        ApplyExtractionCorrectionCommand command,
        DocumentContext context,
        IntakeDocumentRecord metadata,
        Guid precedingVersionId,
        DateTimeOffset retention,
        long sequence,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!IsT4Field(command.Field))
        {
            return CorrectionFailure(ExtractionResultCode.Invalid);
        }

        var rebuilt = await ReadT4DraftAsync(
                firm,
                context.IntakeCase,
                metadata,
                context.DocumentExtraction,
                cancellationToken)
            .ConfigureAwait(false);
        if (rebuilt is null ||
            !rebuilt.TryApply(command, out var priorValue))
        {
            return CorrectionFailure(ExtractionResultCode.Invalid);
        }

        var corrected = rebuilt.ToProviderResult();
        var processed = ProcessT4(
            firm,
            context.IntakeCase,
            context.DocumentExtraction.ParticipantId,
            metadata.DocumentId,
            corrected,
            LatestProviderLabel(context.DocumentExtraction),
            cancellationToken);
        if (processed is null)
        {
            return CorrectionFailure(ExtractionResultCode.Invalid);
        }

        // A successful processor result is still passed through the existing
        // review assembler. Correction never saves that one-document package;
        // it uses the assembler only as the last unchanged gate in the required
        // re-entry chain. Case-level finalization remains the sole save path.
        processed = ReviewCorrectedExtraction(processed);

        var correction = new ExtractionCorrectionRecord(
            _identifiers.NewIdentifier(),
            metadata.DocumentId,
            command.Field,
            command.BoxCode,
            priorValue,
            command.CorrectedValue,
            ExtractionActorKind.Accountant,
            actor,
            nowUtc,
            precedingVersionId);

        var correctionWrite = await _store.AppendCorrectionAsync(
                firm,
                command.CaseId,
                correction,
                retention,
                Audit(ExtractionAuditKind.CorrectionRecorded, sequence, actor, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);
        if (correctionWrite != ExtractionStoreWriteCode.Success)
        {
            return CorrectionFailure(Map(correctionWrite));
        }

        var classification = EffectiveClassificationOutcome(context.DocumentExtraction);
        var version = new ExtractionVersion(
            _identifiers.NewIdentifier(),
            metadata.DocumentId,
            ExtractionProjection.NextExtractionVersion(context.DocumentExtraction),
            classification,
            Provenance(metadata.DocumentId, LatestProviderLabel(context.DocumentExtraction), corrected.Confidence),
            corrected.EmployerName,
            corrected.EmployeeName,
            corrected.EmployerAddress,
            corrected.EmployeeAddress,
            KeepableBoxes(corrected),
            EngineOutcomes(processed),
            ExtractionAcceptanceState.AwaitingAccountantDecision,
            ExtractionProjection.PrecedingExtractionVersionId(context.DocumentExtraction),
            nowUtc);

        var versionWrite = await _store.AppendExtractionVersionAsync(
                firm,
                command.CaseId,
                context.DocumentExtraction.ParticipantId,
                version,
                retention,
                Audit(ExtractionAuditKind.ExtractionRecorded, sequence + 1, actor, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);
        if (versionWrite != ExtractionStoreWriteCode.Success)
        {
            return CorrectionFailure(Map(versionWrite));
        }

        return await ProjectOutcomeAsync(
                firm,
                command.CaseId,
                command.DocumentId,
                ExtractionRecordedKind.T4ExtractionRecorded,
                processed.Errors,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ExtractionResult<DocumentExtractionOutcome>> CorrectMedicalAsync(
        TrustedFirmId firm,
        Guid actor,
        ApplyExtractionCorrectionCommand command,
        DocumentContext context,
        IntakeDocumentRecord metadata,
        Guid precedingVersionId,
        DateTimeOffset retention,
        long sequence,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!IsMedicalField(command.Field))
        {
            return CorrectionFailure(ExtractionResultCode.Invalid);
        }

        var draft = await ReadMedicalDraftAsync(
                firm,
                context.IntakeCase,
                metadata,
                context.DocumentExtraction,
                cancellationToken)
            .ConfigureAwait(false);
        if (draft is null || !draft.TryApply(command, out var priorValue))
        {
            return CorrectionFailure(ExtractionResultCode.Invalid);
        }

        var corrected = draft.ToProviderResult();
        var outcomes = MedicalEvidenceValidator.Validate(corrected);
        var correction = new ExtractionCorrectionRecord(
            _identifiers.NewIdentifier(),
            metadata.DocumentId,
            command.Field,
            boxCode: null,
            priorValue,
            command.CorrectedValue,
            ExtractionActorKind.Accountant,
            actor,
            nowUtc,
            precedingVersionId);

        var correctionWrite = await _store.AppendCorrectionAsync(
                firm,
                command.CaseId,
                correction,
                retention,
                Audit(ExtractionAuditKind.CorrectionRecorded, sequence, actor, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);
        if (correctionWrite != ExtractionStoreWriteCode.Success)
        {
            return CorrectionFailure(Map(correctionWrite));
        }

        var version = new MedicalEvidenceVersion(
            _identifiers.NewIdentifier(),
            metadata.DocumentId,
            ExtractionProjection.NextMedicalEvidenceVersion(context.DocumentExtraction),
            corrected.ServiceDateAsPrinted,
            corrected.GrossAmountAsPrinted,
            CurrencyDeclaration.FromPrintedCode(corrected.CurrencyCodeAsPrinted),
            corrected.ProviderDisplayName,
            corrected.PatientDisplayName,
            corrected.ReceiptReference,
            Provenance(metadata.DocumentId, LatestProviderLabel(context.DocumentExtraction), corrected.Confidence),
            outcomes,
            ExtractionAcceptanceState.AwaitingAccountantDecision,
            ExtractionProjection.PrecedingMedicalEvidenceVersionId(context.DocumentExtraction),
            nowUtc);

        var versionWrite = await _store.AppendMedicalEvidenceVersionAsync(
                firm,
                command.CaseId,
                context.DocumentExtraction.ParticipantId,
                version,
                retention,
                Audit(
                    ExtractionAuditKind.MedicalEvidenceRecorded,
                    sequence + 1,
                    actor,
                    nowUtc),
                cancellationToken)
            .ConfigureAwait(false);
        if (versionWrite != ExtractionStoreWriteCode.Success)
        {
            return CorrectionFailure(Map(versionWrite));
        }

        return await ProjectOutcomeAsync(
                firm,
                command.CaseId,
                command.DocumentId,
                ExtractionRecordedKind.MedicalEvidenceRecorded,
                Array.Empty<ValidationError>(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ParseResult<ProcessedT4Extraction>?> RebuildT4Async(
        TrustedFirmId firm,
        StoredIntakeCase intakeCase,
        IntakeDocumentRecord metadata,
        StoredDocumentExtraction document,
        ApplyExtractionCorrectionCommand? additionalCorrection,
        CancellationToken cancellationToken)
    {
        var draft = await ReadT4DraftAsync(
                firm, intakeCase, metadata, document, cancellationToken)
            .ConfigureAwait(false);
        if (draft is null ||
            (additionalCorrection is not null &&
             !draft.TryApply(additionalCorrection, out _)))
        {
            return null;
        }

        var providerResult = draft.ToProviderResult();
        var latest = ExtractionProjection.LatestExtractionVersion(document);
        if (latest is null || !MatchesStoredVersion(providerResult, latest))
        {
            return null;
        }

        return ProcessT4(
            firm,
            intakeCase,
            document.ParticipantId,
            metadata.DocumentId,
            providerResult,
            LatestProviderLabel(document),
            cancellationToken);
    }

    private async Task<T4Draft?> ReadT4DraftAsync(
        TrustedFirmId firm,
        StoredIntakeCase intakeCase,
        IntakeDocumentRecord metadata,
        StoredDocumentExtraction document,
        CancellationToken cancellationToken)
    {
        var providerResult = await ReadProviderAsync(
                firm,
                intakeCase.CaseId,
                metadata,
                (source, token) => _provider.ExtractT4Async(source, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (providerResult?.Boxes is null)
        {
            return null;
        }

        var draft = T4Draft.From(providerResult);
        foreach (var correction in document.Corrections)
        {
            if (IsT4Field(correction.Field) && !draft.TryApply(correction))
            {
                return null;
            }
        }

        return draft;
    }

    private async Task<MedicalDraft?> ReadMedicalDraftAsync(
        TrustedFirmId firm,
        StoredIntakeCase intakeCase,
        IntakeDocumentRecord metadata,
        StoredDocumentExtraction document,
        CancellationToken cancellationToken)
    {
        var providerResult = await ReadProviderAsync(
                firm,
                intakeCase.CaseId,
                metadata,
                (source, token) => _provider.ExtractMedicalAsync(source, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (providerResult is null)
        {
            return null;
        }

        var draft = MedicalDraft.From(providerResult);
        foreach (var correction in document.Corrections)
        {
            if (IsMedicalField(correction.Field) && !draft.TryApply(correction))
            {
                return null;
            }
        }

        return draft;
    }

    private ParseResult<ProcessedT4Extraction>? ProcessT4(
        TrustedFirmId firm,
        StoredIntakeCase intakeCase,
        Guid participantId,
        Guid documentId,
        ProviderT4Result providerResult,
        string? providerLabel,
        CancellationToken cancellationToken = default)
    {
        var reachedGate = false;
        var processed = ExtractionPayloadBuilder.Build(providerResult, payload =>
        {
            reachedGate = true;
            var envelope = new ExtractionEnvelope
            {
                DocumentId = documentId.ToString("D"),
                Kind = ExtractedDocumentKind.T4Slip,
                FirmId = firm.ToCanonicalString(),
                CaseId = intakeCase.CaseId.ToString("D"),
                ParticipantId = participantId.ToString("D"),
                TaxYear = intakeCase.TaxYear,
                Provenance = Provenance(
                    documentId, providerLabel, providerResult.Confidence),
                T4Payload = payload,
            };

            var validated = _ingress.SubmitAsync(envelope, cancellationToken)
                .GetAwaiter()
                .GetResult();
            return validated.IsFailure
                ? ParseResult<ProcessedT4Extraction>.Failure(validated.Errors)
                : _processor.Process(validated.Value);
        });

        return reachedGate ? processed : null;
    }

    private async Task<TResult?> ReadProviderAsync<TResult>(
        TrustedFirmId firm,
        Guid caseId,
        IntakeDocumentRecord metadata,
        Func<Stream, CancellationToken, Task<TResult?>> call,
        CancellationToken cancellationToken)
        where TResult : class
    {
        var lease = await _contentReader.OpenAsync(
                firm, caseId, metadata.DocumentId, metadata.ContentKey, cancellationToken)
            .ConfigureAwait(false);
        if (lease is null)
        {
            return null;
        }

        await using (lease.ConfigureAwait(false))
        {
            var bytes = await ReadSourceAsync(lease.Content, cancellationToken)
                .ConfigureAwait(false);
            if (bytes is null)
            {
                return null;
            }

            try
            {
                using var source = new MemoryStream(bytes, writable: false);
                return await BoundedAsync(
                        token => call(source, token), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
    }

    private async Task<DocumentContext?> LoadDocumentAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var intakeCase = await _intake.FindAsync(firm, caseId, cancellationToken)
            .ConfigureAwait(false);
        if (intakeCase is null || DocumentOf(intakeCase, documentId) is null)
        {
            return null;
        }

        var extractions = await _store.FindCaseAsync(firm, caseId, cancellationToken)
            .ConfigureAwait(false);
        var document = extractions is null ? null : StoredExtractionOf(extractions, documentId);

        return extractions is null || document is null
            ? null
            : new DocumentContext(intakeCase, extractions, document);
    }

    private async Task<ExtractionResult<DocumentExtractionView>> ProjectViewAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var stored = await _store.FindDocumentAsync(
                firm, caseId, documentId, cancellationToken)
            .ConfigureAwait(false);
        return stored is null
            ? ViewFailure(ExtractionResultCode.NotFound)
            : ExtractionResult<DocumentExtractionView>.Success(
                ProjectReviewDocument(stored));
    }

    private async Task<ExtractionResult<DocumentExtractionOutcome>> ProjectOutcomeAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        ExtractionRecordedKind kind,
        IReadOnlyList<ValidationError> errors,
        CancellationToken cancellationToken)
    {
        var view = await ProjectViewAsync(
                firm, caseId, documentId, cancellationToken)
            .ConfigureAwait(false);
        return !view.IsSuccess
            ? CorrectionFailure(view.Code)
            : ExtractionResult<DocumentExtractionOutcome>.Success(
                new DocumentExtractionOutcome(kind, view.Value!, errors));
    }

    private async Task<TResult?> BoundedAsync<TResult>(
        Func<CancellationToken, Task<TResult?>> call,
        CancellationToken cancellationToken)
        where TResult : class
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(_options.ProviderCallLimit);

        try
        {
            return await call(bounded.Token).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private Task<byte[]?> ReadSourceAsync(
        Stream source,
        CancellationToken cancellationToken) =>
        BoundedAsync(token => CopySourceAsync(source, token), cancellationToken);

    private static async Task<byte[]?> CopySourceAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();

        try
        {
            await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }
        finally
        {
            if (buffer.TryGetBuffer(out var allocated) && allocated.Array is not null)
            {
                Array.Clear(allocated.Array);
            }
        }
    }

    private ExtractionAuditEventRecord Audit(
        ExtractionAuditKind kind,
        long sequence,
        Guid actor,
        DateTimeOffset nowUtc) =>
        new(
            _identifiers.NewIdentifier(),
            sequence,
            kind,
            ExtractionActorKind.Accountant,
            actor,
            nowUtc);

    private static bool ValidMutation(
        TrustedFirmId firm,
        Guid actor,
        Guid caseId,
        DateTimeOffset nowUtc) =>
        ExtractionWorkflowOptionsGuard.ValidScope(firm, caseId) &&
        actor != Guid.Empty &&
        ExtractionWorkflowOptionsGuard.IsUtc(nowUtc);

    private static bool ValidCorrectionShape(ApplyExtractionCorrectionCommand command)
    {
        if (!Enum.IsDefined(command.Field))
        {
            return false;
        }

        if (IsT4BoxField(command.Field))
        {
            if (string.IsNullOrWhiteSpace(command.BoxCode) ||
                !IsPermittedBoxCode(command.BoxCode))
            {
                return false;
            }

            if (command.Field == CorrectionField.RemoveT4Box)
            {
                return command.CorrectedValue is null;
            }

            return !string.IsNullOrWhiteSpace(command.CorrectedValue) &&
                   (command.Field != CorrectionField.T4BoxCode ||
                    IsPermittedBoxCode(command.CorrectedValue));
        }

        return command.BoxCode is null;
    }

    private static bool IsT4BoxField(CorrectionField field) =>
        field is CorrectionField.T4BoxValue
            or CorrectionField.T4BoxCode
            or CorrectionField.AddT4Box
            or CorrectionField.RemoveT4Box;

    private static bool IsPermittedBoxCode(string? boxCode) =>
        ExtractionBoxValue.TryCreate(boxCode, "structural-probe", out _);

    private static bool IsT4Field(CorrectionField field) =>
        IsT4BoxField(field) ||
        field is CorrectionField.EmployerDisplayName
            or CorrectionField.EmployeeDisplayName
            or CorrectionField.EmployerAddress
            or CorrectionField.EmployeeAddress;

    private static bool IsMedicalField(CorrectionField field) =>
        field is CorrectionField.MedicalServiceDate
            or CorrectionField.MedicalGrossAmount
            or CorrectionField.MedicalCurrency
            or CorrectionField.MedicalProviderDisplayName
            or CorrectionField.MedicalPatientDisplayName
            or CorrectionField.MedicalReceiptReference;

    private static bool IsFinalized(
        StoredIntakeCase intakeCase,
        StoredCaseExtractions extractions) =>
        intakeCase.ReviewAssociation is not null ||
        extractions.CaseAcceptances.Count > 0;

    private static DocumentExtractionView ProjectReviewDocument(
        StoredDocumentExtraction document)
    {
        var effective = EffectiveClassification(document);
        var latestT4 = ExtractionProjection.LatestExtractionVersion(document);
        var latestMedical = ExtractionProjection.LatestMedicalEvidenceVersion(document);
        var currentT4 = effective == DocumentClassification.T4Slip ? latestT4 : null;
        var currentMedical = effective == DocumentClassification.MedicalExpenseEvidence
            ? latestMedical
            : null;

        return new DocumentExtractionView(
            document.DocumentId,
            AutomatedClassification(document),
            effective,
            document.ClassificationOverrides.Count > 0,
            document.Versions.Count,
            document.MedicalEvidenceVersions.Count,
            currentT4?.Version ?? currentMedical?.Version,
            currentT4?.FieldOutcomes ??
                currentMedical?.FieldOutcomes ??
                Array.Empty<FieldValidationOutcome>(),
            CurrentAcceptedVersionId(document) is null
                ? ExtractionAcceptanceState.AwaitingAccountantDecision
                : ExtractionAcceptanceState.AcceptedByAccountant,
            document.Corrections.Count,
            ExtractionProjection.HasUnresolvedDuplicateCandidate(document));
    }

    private static Guid? AcceptableVersionId(StoredDocumentExtraction document)
    {
        if (EffectiveClassification(document) == DocumentClassification.T4Slip)
        {
            var latest = ExtractionProjection.LatestExtractionVersion(document);
            return latest is not null &&
                   latest.Classification.EffectiveClassification ==
                       DocumentClassification.T4Slip &&
                   latest.FieldOutcomes.Count == 0
                ? latest.ExtractionVersionId
                : null;
        }

        if (EffectiveClassification(document) ==
            DocumentClassification.MedicalExpenseEvidence)
        {
            var latest = ExtractionProjection.LatestMedicalEvidenceVersion(document);
            var latestOverride = document.ClassificationOverrides
                .OrderByDescending(item => item.OverriddenAtUtc)
                .FirstOrDefault();
            return latest is not null &&
                   (latestOverride is null ||
                    (latestOverride.OverriddenClassification ==
                         DocumentClassification.MedicalExpenseEvidence &&
                     latest.RecordedAtUtc >= latestOverride.OverriddenAtUtc)) &&
                   !MedicalEvidenceValidator.BlocksAcceptance(latest.FieldOutcomes)
                ? latest.MedicalEvidenceVersionId
                : null;
        }

        return null;
    }

    private static Guid? CurrentAcceptedVersionId(StoredDocumentExtraction document)
    {
        var current = CurrentVersionId(document);
        if (current is null)
        {
            return null;
        }

        return document.Acceptances.Any(
            acceptance =>
                acceptance.Scope == ExtractionAcceptanceScope.Document &&
                acceptance.AcceptedVersionId == current)
            ? current
            : null;
    }

    private static Guid? CurrentVersionId(StoredDocumentExtraction document) =>
        EffectiveClassification(document) switch
        {
            DocumentClassification.T4Slip =>
                ExtractionProjection.LatestExtractionVersion(document)?.ExtractionVersionId,
            DocumentClassification.MedicalExpenseEvidence =>
                ExtractionProjection.LatestMedicalEvidenceVersion(document)
                    ?.MedicalEvidenceVersionId,
            _ => null,
        };

    private static DocumentClassification EffectiveClassification(
        StoredDocumentExtraction document)
    {
        var latestOverride = document.ClassificationOverrides
            .OrderByDescending(item => item.OverriddenAtUtc)
            .FirstOrDefault();
        return latestOverride?.OverriddenClassification ??
               AutomatedClassification(document);
    }

    private static DocumentClassification AutomatedClassification(
        StoredDocumentExtraction document)
    {
        var latestT4 = ExtractionProjection.LatestExtractionVersion(document);
        var latestMedical = ExtractionProjection.LatestMedicalEvidenceVersion(document);

        if (latestT4 is null)
        {
            return latestMedical is null
                ? DocumentClassification.Unsupported
                : DocumentClassification.MedicalExpenseEvidence;
        }

        if (latestMedical is null || latestT4.RecordedAtUtc >= latestMedical.RecordedAtUtc)
        {
            return latestT4.Classification.AutomatedClassification;
        }

        return DocumentClassification.MedicalExpenseEvidence;
    }

    private static ClassificationOutcome EffectiveClassificationOutcome(
        StoredDocumentExtraction document)
    {
        ClassificationOutcome.TryAutomated(
            AutomatedClassification(document), out var automated);
        return ExtractionProjection.Effective(automated!, document);
    }

    private static string? LatestProviderLabel(StoredDocumentExtraction document)
    {
        var t4 = ExtractionProjection.LatestExtractionVersion(document);
        var medical = ExtractionProjection.LatestMedicalEvidenceVersion(document);

        if (t4 is null)
        {
            return medical?.Provenance?.ProviderLabel;
        }

        return medical is null || t4.RecordedAtUtc >= medical.RecordedAtUtc
            ? t4.Provenance?.ProviderLabel
            : medical.Provenance?.ProviderLabel;
    }

    private static ExtractionProvenance Provenance(
        Guid documentId,
        string? providerLabel,
        decimal? confidence) =>
        new()
        {
            SourceReference = documentId.ToString("D"),
            ProviderLabel = providerLabel,
            Confidence = confidence,
        };

    private static IReadOnlyList<ExtractionBoxValue> KeepableBoxes(
        ProviderT4Result providerResult)
    {
        var kept = new List<ExtractionBoxValue>();

        foreach (var box in providerResult.Boxes ?? Array.Empty<ProviderBoxValue?>())
        {
            if (box is not null &&
                ExtractionBoxValue.TryCreate(box.Code, box.Value, out var keptBox))
            {
                kept.Add(keptBox!);
            }
        }

        return kept;
    }

    private static bool MatchesStoredVersion(
        ProviderT4Result providerResult,
        ExtractionVersion version)
    {
        if (!string.Equals(
                providerResult.EmployerName,
                version.EmployerDisplayName,
                StringComparison.Ordinal) ||
            !string.Equals(
                providerResult.EmployeeName,
                version.EmployeeDisplayName,
                StringComparison.Ordinal) ||
            !string.Equals(
                providerResult.EmployerAddress,
                version.EmployerAddress,
                StringComparison.Ordinal) ||
            !string.Equals(
                providerResult.EmployeeAddress,
                version.EmployeeAddress,
                StringComparison.Ordinal))
        {
            return false;
        }

        var keepable = KeepableBoxes(providerResult);
        if (keepable.Count != version.BoxValues.Count)
        {
            return false;
        }

        for (var index = 0; index < keepable.Count; index++)
        {
            if (!string.Equals(
                    keepable[index].Code,
                    version.BoxValues[index].Code,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    keepable[index].Value,
                    version.BoxValues[index].Value,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<FieldValidationOutcome> EngineOutcomes(
        ParseResult<ProcessedT4Extraction> processed)
    {
        var outcomes = new List<FieldValidationOutcome>();

        foreach (var error in processed.Errors)
        {
            if (FieldValidationOutcome.TryCreate(
                    error.Field, FieldOutcomeKind.EngineRejected, out var outcome))
            {
                outcomes.Add(outcome!);
            }
        }

        return outcomes;
    }

    private static ParseResult<ProcessedT4Extraction> ReviewCorrectedExtraction(
        ParseResult<ProcessedT4Extraction> processed)
    {
        if (processed.IsFailure)
        {
            return processed;
        }

        var reviewed = ReviewPackageAssembler.Assemble(
            new ProcessedT4Extraction?[] { processed.Value });
        return reviewed.IsFailure
            ? ParseResult<ProcessedT4Extraction>.Failure(reviewed.Errors)
            : processed;
    }

    private static IntakeDocumentRecord? DocumentOf(
        StoredIntakeCase intakeCase,
        Guid documentId) =>
        intakeCase.Documents.FirstOrDefault(item => item.DocumentId == documentId);

    private static StoredDocumentExtraction? StoredExtractionOf(
        StoredCaseExtractions extractions,
        Guid documentId) =>
        extractions.Documents.FirstOrDefault(item => item.DocumentId == documentId);

    private static StoredDocumentExtraction? FindCandidateOwner(
        StoredCaseExtractions extractions,
        Guid candidateId) =>
        extractions.Documents.FirstOrDefault(
            document => document.DuplicateCandidates.Any(
                candidate => candidate.CandidateId == candidateId));

    private static long NextSequence(StoredCaseExtractions extractions) =>
        extractions.AuditEvents.Count == 0
            ? 1
            : extractions.AuditEvents.Max(item => item.Sequence) + 1;

    private static ExtractionResultCode Map(ExtractionStoreWriteCode code) =>
        code switch
        {
            ExtractionStoreWriteCode.NotFound => ExtractionResultCode.NotFound,
            ExtractionStoreWriteCode.Conflict => ExtractionResultCode.Conflict,
            _ => ExtractionResultCode.Invalid,
        };

    private static ExtractionResultCode Map(IntakeResultCode code) =>
        code switch
        {
            IntakeResultCode.NotFound => ExtractionResultCode.NotFound,
            IntakeResultCode.Conflict => ExtractionResultCode.Conflict,
            _ => ExtractionResultCode.Invalid,
        };

    private static ExtractionResult<DocumentExtractionOutcome> CorrectionFailure(
        ExtractionResultCode code) =>
        ExtractionResult<DocumentExtractionOutcome>.Failure(code);

    private static ExtractionResult<DocumentExtractionView> ViewFailure(
        ExtractionResultCode code) =>
        ExtractionResult<DocumentExtractionView>.Failure(code);

    private static ExtractionResult<FinalizedExtractionReview> FinalizeFailure(
        ExtractionResultCode code) =>
        ExtractionResult<FinalizedExtractionReview>.Failure(code);

    private sealed class DocumentContext
    {
        public DocumentContext(
            StoredIntakeCase intakeCase,
            StoredCaseExtractions extractions,
            StoredDocumentExtraction documentExtraction)
        {
            IntakeCase = intakeCase;
            Extractions = extractions;
            DocumentExtraction = documentExtraction;
        }

        public StoredIntakeCase IntakeCase { get; }

        public StoredCaseExtractions Extractions { get; }

        public StoredDocumentExtraction DocumentExtraction { get; }
    }

    private sealed class T4Draft
    {
        private readonly List<ProviderBoxValue> _boxes;

        private T4Draft(ProviderT4Result source)
        {
            Year = source.Year;
            EmployerName = source.EmployerName;
            EmployerAddress = source.EmployerAddress;
            EmployeeName = source.EmployeeName;
            EmployeeAddress = source.EmployeeAddress;
            ExemptCppQpp = source.ExemptCppQpp;
            ExemptEi = source.ExemptEi;
            ExemptPpip = source.ExemptPpip;
            Confidence = source.Confidence;
            _boxes = source.Boxes!
                .Where(item => item is not null)
                .Select(item => new ProviderBoxValue
                {
                    Code = item!.Code,
                    Value = item.Value,
                    Confidence = item.Confidence,
                })
                .ToList();
        }

        private int? Year { get; }
        private string? EmployerName { get; set; }
        private string? EmployerAddress { get; set; }
        private string? EmployeeName { get; set; }
        private string? EmployeeAddress { get; set; }
        private bool? ExemptCppQpp { get; }
        private bool? ExemptEi { get; }
        private bool? ExemptPpip { get; }
        private decimal? Confidence { get; }

        public static T4Draft From(ProviderT4Result source) => new(source);

        public bool TryApply(
            ApplyExtractionCorrectionCommand command,
            out string? priorValue)
        {
            priorValue = null;

            if (IsT4BoxField(command.Field))
            {
                return TryApplyBox(
                    command.Field, command.BoxCode, command.CorrectedValue, out priorValue);
            }

            switch (command.Field)
            {
                case CorrectionField.EmployerDisplayName:
                    priorValue = EmployerName;
                    EmployerName = command.CorrectedValue;
                    return true;
                case CorrectionField.EmployeeDisplayName:
                    priorValue = EmployeeName;
                    EmployeeName = command.CorrectedValue;
                    return true;
                case CorrectionField.EmployerAddress:
                    priorValue = EmployerAddress;
                    EmployerAddress = command.CorrectedValue;
                    return true;
                case CorrectionField.EmployeeAddress:
                    priorValue = EmployeeAddress;
                    EmployeeAddress = command.CorrectedValue;
                    return true;
                default:
                    return false;
            }
        }

        public bool TryApply(ExtractionCorrectionRecord correction)
        {
            if (IsT4BoxField(correction.Field))
            {
                return TryApplyBox(
                    correction.Field,
                    correction.BoxCode,
                    correction.CorrectedValue,
                    out _);
            }

            switch (correction.Field)
            {
                case CorrectionField.EmployerDisplayName:
                    EmployerName = correction.CorrectedValue;
                    return true;
                case CorrectionField.EmployeeDisplayName:
                    EmployeeName = correction.CorrectedValue;
                    return true;
                case CorrectionField.EmployerAddress:
                    EmployerAddress = correction.CorrectedValue;
                    return true;
                case CorrectionField.EmployeeAddress:
                    EmployeeAddress = correction.CorrectedValue;
                    return true;
                default:
                    return false;
            }
        }

        public ProviderT4Result ToProviderResult() =>
            new()
            {
                Year = Year,
                EmployerName = EmployerName,
                EmployerAddress = EmployerAddress,
                EmployeeName = EmployeeName,
                EmployeeAddress = EmployeeAddress,
                ExemptCppQpp = ExemptCppQpp,
                ExemptEi = ExemptEi,
                ExemptPpip = ExemptPpip,
                Boxes = _boxes.ToArray(),
                Confidence = Confidence,
            };

        private bool TryApplyBox(
            CorrectionField field,
            string? boxCode,
            string? correctedValue,
            out string? priorValue)
        {
            priorValue = null;

            if (string.IsNullOrWhiteSpace(boxCode) ||
                !IsPermittedBoxCode(boxCode))
            {
                return false;
            }

            if (field == CorrectionField.AddT4Box)
            {
                _boxes.Add(new ProviderBoxValue
                {
                    Code = boxCode,
                    Value = correctedValue,
                });
                return true;
            }

            var index = _boxes.FindIndex(
                box => string.Equals(box.Code, boxCode, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            var existing = _boxes[index];
            priorValue = field == CorrectionField.T4BoxCode
                ? existing.Code
                : existing.Value;

            switch (field)
            {
                case CorrectionField.T4BoxValue:
                    _boxes[index] = new ProviderBoxValue
                    {
                        Code = existing.Code,
                        Value = correctedValue,
                        Confidence = existing.Confidence,
                    };
                    return true;
                case CorrectionField.T4BoxCode:
                    if (!IsPermittedBoxCode(correctedValue))
                    {
                        return false;
                    }

                    _boxes[index] = new ProviderBoxValue
                    {
                        Code = correctedValue,
                        Value = existing.Value,
                        Confidence = existing.Confidence,
                    };
                    return true;
                case CorrectionField.RemoveT4Box:
                    _boxes.RemoveAt(index);
                    return true;
                default:
                    return false;
            }
        }
    }

    private sealed class MedicalDraft
    {
        private MedicalDraft(ProviderMedicalResult source)
        {
            ServiceDate = source.ServiceDateAsPrinted;
            GrossAmount = source.GrossAmountAsPrinted;
            Currency = source.CurrencyCodeAsPrinted;
            ProviderName = source.ProviderDisplayName;
            PatientName = source.PatientDisplayName;
            ReceiptReference = source.ReceiptReference;
            Confidence = source.Confidence;
        }

        private string? ServiceDate { get; set; }
        private string? GrossAmount { get; set; }
        private string? Currency { get; set; }
        private string? ProviderName { get; set; }
        private string? PatientName { get; set; }
        private string? ReceiptReference { get; set; }
        private decimal? Confidence { get; }

        public static MedicalDraft From(ProviderMedicalResult source) => new(source);

        public bool TryApply(
            ApplyExtractionCorrectionCommand command,
            out string? priorValue) =>
            TryApply(command.Field, command.CorrectedValue, out priorValue);

        public bool TryApply(ExtractionCorrectionRecord correction) =>
            TryApply(correction.Field, correction.CorrectedValue, out _);

        public ProviderMedicalResult ToProviderResult() =>
            new()
            {
                ServiceDateAsPrinted = ServiceDate,
                GrossAmountAsPrinted = GrossAmount,
                CurrencyCodeAsPrinted = Currency,
                ProviderDisplayName = ProviderName,
                PatientDisplayName = PatientName,
                ReceiptReference = ReceiptReference,
                Confidence = Confidence,
            };

        private bool TryApply(
            CorrectionField field,
            string? correctedValue,
            out string? priorValue)
        {
            priorValue = null;

            switch (field)
            {
                case CorrectionField.MedicalServiceDate:
                    priorValue = ServiceDate;
                    ServiceDate = correctedValue;
                    return true;
                case CorrectionField.MedicalGrossAmount:
                    priorValue = GrossAmount;
                    GrossAmount = correctedValue;
                    return true;
                case CorrectionField.MedicalCurrency:
                    priorValue = Currency;
                    Currency = correctedValue;
                    return true;
                case CorrectionField.MedicalProviderDisplayName:
                    priorValue = ProviderName;
                    ProviderName = correctedValue;
                    return true;
                case CorrectionField.MedicalPatientDisplayName:
                    priorValue = PatientName;
                    PatientName = correctedValue;
                    return true;
                case CorrectionField.MedicalReceiptReference:
                    priorValue = ReceiptReference;
                    ReceiptReference = correctedValue;
                    return true;
                default:
                    return false;
            }
        }
    }
}
