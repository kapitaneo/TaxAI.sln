using CaseProcessing.Models;
using CaseProcessing.Services;
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
/// The production caller the tax chain never had.
/// <para>
/// It turns one stored source document into the <c>ValidatedExtraction</c> that
/// starts the existing chain, and it <b>invents no tax truth of its own</b>. The
/// envelope is gated by the existing ingress, the typed slip comes only from the
/// existing processor, and engine errors are surfaced verbatim and in order —
/// never translated, summarized, re-worded, filtered, or interleaved with a
/// message of this module's own.
/// </para>
/// <para>
/// <b>Scope moves inward, never outward.</b> The firm arrives as a
/// <see cref="TrustedFirmId"/> the host already resolved from a revalidated
/// session; the case and document are selectors verified against server-stored
/// state before a byte is read; the participant, the tax year, and the opaque
/// content key are read from that stored state and are never accepted from a
/// caller. There is no argument, member, or command property through which a
/// route value, query string, form field, header, or cookie could supply any of
/// them.
/// </para>
/// <para>
/// <b>It reads no clock, no environment, no current user, and no HTTP
/// context.</b> The instant and the actor reference arrive as arguments and the
/// identifiers arrive from a port, so the same document, case, instant, and
/// provider always produce the same result.
/// </para>
/// <para>
/// <b>It fails closed everywhere.</b> A provider that throws, cancels, exceeds
/// the configured call bound, returns nothing, returns a null box collection, or
/// returns a box with no code each end the operation with nothing stored and
/// nothing thrown to the caller. A missing case, a missing document, and one
/// belonging to another firm all produce the same
/// <see cref="ExtractionResultCode.NotFound"/>.
/// </para>
/// <para>
/// <b>Confidence gates nothing.</b> It is transported onto the existing
/// provenance contract as untrusted attention metadata and is compared against
/// nothing anywhere in this class. The only range check that exists is the one
/// already inside the existing envelope validator.
/// </para>
/// </summary>
public sealed class ExtractionIngestWorkflow : IExtractionIngestWorkflow
{
    private readonly IIntakeStore _intake;
    private readonly IExtractionStore _store;
    private readonly IExtractionContentReader _contentReader;
    private readonly IDocumentIntelligenceProvider _provider;
    private readonly IDocumentExtractionIngress _ingress;
    private readonly T4CaseProcessor _processor;
    private readonly IOpaqueIdentifierSource _identifiers;
    private readonly ExtractionWorkflowOptions _options;

    /// <exception cref="ArgumentNullException">A required collaborator is absent.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A configured value is missing or invalid.</exception>
    public ExtractionIngestWorkflow(
        IIntakeStore intake,
        IExtractionStore store,
        IExtractionContentReader contentReader,
        IDocumentIntelligenceProvider provider,
        IDocumentExtractionIngress ingress,
        T4CaseProcessor processor,
        IOpaqueIdentifierSource identifiers,
        ExtractionWorkflowOptions options)
    {
        ArgumentNullException.ThrowIfNull(intake);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(contentReader);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(ingress);
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(identifiers);

        _intake = intake;
        _store = store;
        _contentReader = contentReader;
        _provider = provider;
        _ingress = ingress;
        _processor = processor;
        _identifiers = identifiers;
        _options = ExtractionWorkflowOptionsGuard.RequireValid(options);
    }

    /// <inheritdoc />
    public async Task<ExtractionResult<DocumentExtractionOutcome>> ExtractAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        ExtractDocumentCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (command is null ||
            !ExtractionWorkflowOptionsGuard.ValidScope(firm, command.CaseId) ||
            command.DocumentId == Guid.Empty ||
            accountantActorReference == Guid.Empty ||
            !ExtractionWorkflowOptionsGuard.IsUtc(nowUtc))
        {
            return Refused(ExtractionResultCode.Invalid);
        }

        try
        {
            return await RunAsync(
                    firm, accountantActorReference, command, nowUtc, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller's own cancellation is the caller's to observe. Anything
            // else that cancels is a failure of this operation, handled below.
            throw;
        }
        catch (Exception)
        {
            // Untrusted provider output, untrusted stored content, and a store
            // that refuses all end the same way: nothing partial, and no
            // exception carrying a value the caller was never entitled to see.
            return Refused(ExtractionResultCode.Invalid);
        }
    }

    /// <summary>Fixed type identity — this type carries no caller or taxpayer data.</summary>
    public override string ToString() => nameof(ExtractionIngestWorkflow);

    // ----------------------------------------------------------- the run

    private async Task<ExtractionResult<DocumentExtractionOutcome>> RunAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        ExtractDocumentCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        // Step 1 — the four-part scope, against server-stored state. The case
        // read is firm-scoped, so a case in another firm is simply absent; the
        // document must then be one this case actually holds, and the content
        // key comes from that stored record rather than from anyone's request.
        var storedCase = await _intake
            .FindAsync(firm, command.CaseId, cancellationToken)
            .ConfigureAwait(false);

        if (storedCase is null)
        {
            return Refused(ExtractionResultCode.NotFound);
        }

        if (DocumentOf(storedCase, command.DocumentId) is not { } documentMetadata)
        {
            return Refused(ExtractionResultCode.NotFound);
        }

        var caseExtractions = await _store
            .FindCaseAsync(firm, command.CaseId, cancellationToken)
            .ConfigureAwait(false);

        if (caseExtractions is null)
        {
            return Refused(ExtractionResultCode.NotFound);
        }

        var storedDocument = StoredExtractionOf(caseExtractions, command.DocumentId);

        // The one existing retention authority, asked for this record's own year.
        // Nothing here derives a boundary, reads a clock, or declares a second
        // retention rule.
        var retentionUntilUtc = RetentionPolicy.ComputeRetentionUntilUtc(storedCase.TaxYear);
        var sequence = NextSequence(caseExtractions);

        // Step 2 — classify. The lease bounds how long the bytes are reachable:
        // it is opened, consumed once, and disposed before anything else runs.
        var classificationLease = await _contentReader
            .OpenAsync(
                firm,
                command.CaseId,
                command.DocumentId,
                documentMetadata.ContentKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (classificationLease is null)
        {
            return Refused(ExtractionResultCode.NotFound);
        }

        byte[]? classifiedContent = null;

        await using (classificationLease.ConfigureAwait(false))
        {
            try
            {
                // Classification and extraction use separate leases because a
                // provider consumes a stream once from its current position.
                // Keep this bounded in-memory copy only while the lease is live
                // so the second read can be proved to be the same immutable
                // source before any version or audit event is appended.
                classifiedContent = await ReadSourceAsync(
                        classificationLease.Content, cancellationToken)
                    .ConfigureAwait(false);

                if (classifiedContent is null)
                {
                    return Refused(ExtractionResultCode.Invalid);
                }

                ProviderClassificationResult? claimed;

                using (var source = new MemoryStream(classifiedContent, writable: false))
                {
                    claimed = await BoundedAsync(
                            token => _provider.ClassifyAsync(source, token),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (!ClassificationOutcome.TryAutomated(
                        claimed?.Classification, out var automated))
                {
                    return Refused(ExtractionResultCode.Invalid);
                }

                // An override already recorded for this document governs which
                // path runs; absent one, the automated classification controls.
                // Both values are retained so a disagreement can be shown
                // rather than resolved silently.
                var classification = ExtractionProjection.Effective(
                    automated!, storedDocument);

                return classification.EffectiveClassification switch
                {
                    DocumentClassification.T4Slip => await ExtractT4Async(
                            firm,
                            accountantActorReference,
                            command,
                            storedCase,
                            documentMetadata,
                            caseExtractions,
                            storedDocument,
                            classification,
                            claimed!,
                            classifiedContent,
                            retentionUntilUtc,
                            sequence,
                            nowUtc,
                            cancellationToken)
                        .ConfigureAwait(false),

                    DocumentClassification.MedicalExpenseEvidence =>
                        await ExtractMedicalAsync(
                                firm,
                                accountantActorReference,
                                command,
                                storedCase,
                                documentMetadata,
                                caseExtractions,
                                storedDocument,
                                classification,
                                claimed!,
                                classifiedContent,
                                retentionUntilUtc,
                                sequence,
                                nowUtc,
                                cancellationToken)
                            .ConfigureAwait(false),

                    // Nothing recognized it, and no accountant said otherwise.
                    // No payload, no envelope, no processed extraction, no
                    // evidence version: one audit event, and that is the whole
                    // outcome.
                    _ => await RefuseUnrecognizedAsync(
                            firm,
                            accountantActorReference,
                            command,
                            classification,
                            retentionUntilUtc,
                            sequence,
                            nowUtc,
                            cancellationToken)
                        .ConfigureAwait(false),
                };
            }
            finally
            {
                ClearSource(classifiedContent);
            }
        }
    }

    // ------------------------------------------------------------- the T4 path

    private async Task<ExtractionResult<DocumentExtractionOutcome>> ExtractT4Async(
        TrustedFirmId firm,
        Guid accountantActorReference,
        ExtractDocumentCommand command,
        StoredIntakeCase storedCase,
        IntakeDocumentRecord documentMetadata,
        StoredCaseExtractions caseExtractions,
        StoredDocumentExtraction? storedDocument,
        ClassificationOutcome classification,
        ProviderClassificationResult claimed,
        byte[] classifiedContent,
        DateTimeOffset retentionUntilUtc,
        long sequence,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var lease = await _contentReader
            .OpenAsync(
                firm,
                command.CaseId,
                command.DocumentId,
                documentMetadata.ContentKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (lease is null)
        {
            return Refused(ExtractionResultCode.NotFound);
        }

        ProviderT4Result? providerResult;
        byte[]? extractedContent = null;
        await using (lease.ConfigureAwait(false))
        {
            try
            {
                extractedContent = await ReadSourceAsync(lease.Content, cancellationToken)
                    .ConfigureAwait(false);

                if (extractedContent is null ||
                    !classifiedContent.AsSpan().SequenceEqual(extractedContent))
                {
                    return Refused(ExtractionResultCode.Invalid);
                }

                using var source = new MemoryStream(extractedContent, writable: false);
                providerResult = await BoundedAsync(
                        token => _provider.ExtractT4Async(source, token),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                ClearSource(extractedContent);
            }
        }

        // Step 3 — assemble the payload, gate it, and run the existing chain.
        // The payload exists only inside the continuation, which is the only
        // place Box 12 exists at all: it is never returned, stored, or logged.
        var reachedTheChain = false;

        var processed = ExtractionPayloadBuilder.Build(providerResult, payload =>
        {
            reachedTheChain = true;

            var envelope = new ExtractionEnvelope
            {
                DocumentId = command.DocumentId.ToString("D"),
                Kind = ExtractedDocumentKind.T4Slip,
                FirmId = firm.ToCanonicalString(),
                CaseId = storedCase.CaseId.ToString("D"),
                ParticipantId = storedCase.PrimaryParticipantId.ToString("D"),

                // The server-stored year, and only ever that. What the document
                // showed stays on the payload; the existing validator refuses
                // the pair when they disagree, and neither is coerced.
                TaxYear = storedCase.TaxYear,
                Provenance = new ExtractionProvenance
                {
                    SourceReference = command.DocumentId.ToString("D"),
                    ProviderLabel = claimed.ProviderLabel,
                    Confidence = providerResult!.Confidence,
                },
                T4Payload = payload,
            };

            // The existing gate, reached through the port this feature
            // implements. Validation is synchronous, static, and pure, and the
            // implementation performs no I/O, so the returned task is already
            // complete when it is handed back.
            var validated = _ingress
                .SubmitAsync(envelope, cancellationToken)
                .GetAwaiter()
                .GetResult();

            return validated.IsFailure
                ? ParseResult<ProcessedT4Extraction>.Failure(validated.Errors)
                : _processor.Process(validated.Value);
        });

        if (!reachedTheChain)
        {
            // The provider result was absent or structurally malformed. Nothing
            // reached the engine, so nothing is stored.
            return Refused(ExtractionResultCode.Invalid);
        }

        var version = new ExtractionVersion(
            _identifiers.NewIdentifier(),
            command.DocumentId,
            ExtractionProjection.NextExtractionVersion(storedDocument),
            classification,
            new ExtractionProvenance
            {
                SourceReference = command.DocumentId.ToString("D"),
                ProviderLabel = claimed.ProviderLabel,
                Confidence = providerResult!.Confidence,
            },
            providerResult.EmployerName,
            providerResult.EmployeeName,

            // Absent stays absent. Nothing substitutes, infers, or copies a
            // value in place of one the document did not show.
            providerResult.EmployerAddress,
            providerResult.EmployeeAddress,
            KeepableBoxes(providerResult),
            EngineOutcomes(processed),
            ExtractionAcceptanceState.AwaitingAccountantDecision,
            ExtractionProjection.PrecedingExtractionVersionId(storedDocument),
            nowUtc);

        var write = await _store
            .AppendExtractionVersionAsync(
                firm,
                command.CaseId,
                storedCase.PrimaryParticipantId,
                version,
                retentionUntilUtc,
                Audit(
                    ExtractionAuditKind.ExtractionRecorded,
                    sequence,
                    accountantActorReference,
                    nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        if (write != ExtractionStoreWriteCode.Success)
        {
            return Refused(Map(write));
        }

        var flagged = await FlagAsync(
                firm,
                accountantActorReference,
                command,
                DuplicateCandidateDetector.FindT4Candidate(
                    command.DocumentId, version, caseExtractions),
                retentionUntilUtc,
                sequence + 1,
                nowUtc,
                cancellationToken)
            .ConfigureAwait(false);

        if (flagged != ExtractionStoreWriteCode.Success)
        {
            return Refused(Map(flagged));
        }

        return await ProjectAsync(
                firm,
                command,
                ExtractionRecordedKind.T4ExtractionRecorded,
                classification,
                processed.Errors,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // -------------------------------------------------------- the medical path

    private async Task<ExtractionResult<DocumentExtractionOutcome>> ExtractMedicalAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        ExtractDocumentCommand command,
        StoredIntakeCase storedCase,
        IntakeDocumentRecord documentMetadata,
        StoredCaseExtractions caseExtractions,
        StoredDocumentExtraction? storedDocument,
        ClassificationOutcome classification,
        ProviderClassificationResult claimed,
        byte[] classifiedContent,
        DateTimeOffset retentionUntilUtc,
        long sequence,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var lease = await _contentReader
            .OpenAsync(
                firm,
                command.CaseId,
                command.DocumentId,
                documentMetadata.ContentKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (lease is null)
        {
            return Refused(ExtractionResultCode.NotFound);
        }

        ProviderMedicalResult? providerResult;
        byte[]? extractedContent = null;
        await using (lease.ConfigureAwait(false))
        {
            try
            {
                extractedContent = await ReadSourceAsync(lease.Content, cancellationToken)
                    .ConfigureAwait(false);

                if (extractedContent is null ||
                    !classifiedContent.AsSpan().SequenceEqual(extractedContent))
                {
                    return Refused(ExtractionResultCode.Invalid);
                }

                using var source = new MemoryStream(extractedContent, writable: false);
                providerResult = await BoundedAsync(
                        token => _provider.ExtractMedicalAsync(source, token),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                ClearSource(extractedContent);
            }
        }

        if (providerResult is null)
        {
            return Refused(ExtractionResultCode.Invalid);
        }

        // Structural only. Whether the receipt is eligible, deductible,
        // claimable, reimbursed, or assigned to anyone is not a question this
        // feature asks, and no member here could carry the answer.
        var outcomes = MedicalEvidenceValidator.Validate(providerResult);

        var version = new MedicalEvidenceVersion(
            _identifiers.NewIdentifier(),
            command.DocumentId,
            ExtractionProjection.NextMedicalEvidenceVersion(storedDocument),

            // As printed. Nothing is normalized to a canonical representation,
            // converted between currencies, rounded, or arithmetic.
            providerResult.ServiceDateAsPrinted,
            providerResult.GrossAmountAsPrinted,
            CurrencyDeclaration.FromPrintedCode(providerResult.CurrencyCodeAsPrinted),
            providerResult.ProviderDisplayName,
            providerResult.PatientDisplayName,
            providerResult.ReceiptReference,
            new ExtractionProvenance
            {
                SourceReference = command.DocumentId.ToString("D"),
                ProviderLabel = claimed.ProviderLabel,
                Confidence = providerResult.Confidence,
            },
            outcomes,
            ExtractionAcceptanceState.AwaitingAccountantDecision,
            ExtractionProjection.PrecedingMedicalEvidenceVersionId(storedDocument),
            nowUtc);

        var write = await _store
            .AppendMedicalEvidenceVersionAsync(
                firm,
                command.CaseId,
                storedCase.PrimaryParticipantId,
                version,
                retentionUntilUtc,
                Audit(
                    ExtractionAuditKind.MedicalEvidenceRecorded,
                    sequence,
                    accountantActorReference,
                    nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        if (write != ExtractionStoreWriteCode.Success)
        {
            return Refused(Map(write));
        }

        var flagged = await FlagAsync(
                firm,
                accountantActorReference,
                command,
                DuplicateCandidateDetector.FindMedicalCandidate(
                    command.DocumentId, version, caseExtractions),
                retentionUntilUtc,
                sequence + 1,
                nowUtc,
                cancellationToken)
            .ConfigureAwait(false);

        if (flagged != ExtractionStoreWriteCode.Success)
        {
            return Refused(Map(flagged));
        }

        return await ProjectAsync(
                firm,
                command,
                ExtractionRecordedKind.MedicalEvidenceRecorded,
                classification,
                Array.Empty<ValidationError>(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<ExtractionResult<DocumentExtractionOutcome>> RefuseUnrecognizedAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        ExtractDocumentCommand command,
        ClassificationOutcome classification,
        DateTimeOffset retentionUntilUtc,
        long sequence,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var write = await _store
            .AppendAuditEventAsync(
                firm,
                command.CaseId,
                Audit(
                    ExtractionAuditKind.ExtractionRefused,
                    sequence,
                    accountantActorReference,
                    nowUtc),
                retentionUntilUtc,
                cancellationToken)
            .ConfigureAwait(false);

        if (write != ExtractionStoreWriteCode.Success)
        {
            return Refused(Map(write));
        }

        return await ProjectAsync(
                firm,
                command,
                ExtractionRecordedKind.NothingRecorded,
                classification,
                Array.Empty<ValidationError>(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ExtractionStoreWriteCode> FlagAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        ExtractDocumentCommand command,
        Guid? comparedDocumentId,
        DateTimeOffset retentionUntilUtc,
        long sequence,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (comparedDocumentId is not { } compared)
        {
            return ExtractionStoreWriteCode.Success;
        }

        // Flagged, never resolved. Both documents stay present and unaccepted
        // until an accountant says which they are.
        return await _store
            .AppendDuplicateCandidateAsync(
                firm,
                command.CaseId,
                new DuplicateCandidateRecord(
                    _identifiers.NewIdentifier(),
                    command.DocumentId,
                    compared,
                    nowUtc),
                retentionUntilUtc,
                Audit(
                    ExtractionAuditKind.DuplicateFlagged,
                    sequence,
                    accountantActorReference,
                    nowUtc),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ExtractionResult<DocumentExtractionOutcome>> ProjectAsync(
        TrustedFirmId firm,
        ExtractDocumentCommand command,
        ExtractionRecordedKind recorded,
        ClassificationOutcome classification,
        IReadOnlyList<ValidationError> engineErrors,
        CancellationToken cancellationToken)
    {
        var stored = await _store
            .FindDocumentAsync(firm, command.CaseId, command.DocumentId, cancellationToken)
            .ConfigureAwait(false);

        var view = stored is null
            ? new DocumentExtractionView(
                command.DocumentId,
                classification.AutomatedClassification,
                classification.EffectiveClassification,
                classification.IsOverridden,
                extractionVersionCount: 0,
                medicalEvidenceVersionCount: 0,
                latestVersion: null,
                Array.Empty<FieldValidationOutcome>(),
                ExtractionAcceptanceState.AwaitingAccountantDecision,
                correctionCount: 0,
                hasUnresolvedDuplicateCandidate: false)
            : ExtractionProjection.Document(stored);

        return ExtractionResult<DocumentExtractionOutcome>.Success(
            new DocumentExtractionOutcome(recorded, view, engineErrors));
    }

    /// <summary>
    /// Runs one provider call under the configured bound, returning null for
    /// every failure it can encounter.
    /// <para>
    /// The caller's own cancellation is never swallowed: it propagates, and the
    /// caller observes it. Everything else — a throw, an internal timeout, an
    /// unreadable stream — becomes the same structural refusal, so untrusted
    /// provider output can never reach a caller as an exception.
    /// </para>
    /// </summary>
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
        BoundedAsync(
            token => CopySourceAsync(source, token),
            cancellationToken);

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

    private static void ClearSource(byte[]? source)
    {
        if (source is not null)
        {
            Array.Clear(source);
        }
    }

    /// <summary>
    /// Keeps every extracted box the result shapes are allowed to carry.
    /// <para>
    /// The exclusion is the type's, not this loop's:
    /// <see cref="ExtractionBoxValue.TryCreate"/> refuses the box holding the
    /// social insurance number, so a box loop written here — or anywhere later —
    /// cannot persist one however carelessly it is written.
    /// </para>
    /// </summary>
    private static IReadOnlyList<ExtractionBoxValue> KeepableBoxes(ProviderT4Result providerResult)
    {
        var kept = new List<ExtractionBoxValue>();

        foreach (var box in providerResult.Boxes ?? Array.Empty<ProviderBoxValue?>())
        {
            if (box is not null &&
                ExtractionBoxValue.TryCreate(box.Code, box.Value, out var boxValue))
            {
                kept.Add(boxValue!);
            }
        }

        return kept;
    }

    /// <summary>
    /// One structural outcome per error the existing chain produced, in the
    /// order it produced them, each carrying that error's own field key
    /// verbatim. The correspondence stays one-to-one: nothing is added, dropped,
    /// merged, or re-worded, and no rejection is downgraded to a warning.
    /// </summary>
    private static IReadOnlyList<FieldValidationOutcome> EngineOutcomes(
        ParseResult<ProcessedT4Extraction> processed)
    {
        var outcomes = new List<FieldValidationOutcome>(processed.Errors.Count);

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

    private ExtractionAuditEventRecord Audit(
        ExtractionAuditKind kind,
        long sequence,
        Guid accountantActorReference,
        DateTimeOffset nowUtc) =>
        new(
            _identifiers.NewIdentifier(),
            sequence,
            kind,
            ExtractionActorKind.Accountant,
            accountantActorReference,
            nowUtc);

    private static long NextSequence(StoredCaseExtractions caseExtractions)
    {
        long highest = 0;

        foreach (var auditEvent in caseExtractions.AuditEvents)
        {
            if (auditEvent.Sequence > highest)
            {
                highest = auditEvent.Sequence;
            }
        }

        return highest + 1;
    }

    private static IntakeDocumentRecord? DocumentOf(StoredIntakeCase storedCase, Guid documentId)
    {
        foreach (var document in storedCase.Documents)
        {
            if (document.DocumentId == documentId)
            {
                return document;
            }
        }

        return null;
    }

    private static StoredDocumentExtraction? StoredExtractionOf(
        StoredCaseExtractions caseExtractions,
        Guid documentId)
    {
        foreach (var document in caseExtractions.Documents)
        {
            if (document.DocumentId == documentId)
            {
                return document;
            }
        }

        return null;
    }

    private static ExtractionResult<DocumentExtractionOutcome> Refused(ExtractionResultCode code) =>
        ExtractionResult<DocumentExtractionOutcome>.Failure(code);

    private static ExtractionResultCode Map(ExtractionStoreWriteCode code) =>
        code switch
        {
            ExtractionStoreWriteCode.NotFound => ExtractionResultCode.NotFound,
            ExtractionStoreWriteCode.Conflict => ExtractionResultCode.Conflict,
            _ => ExtractionResultCode.Invalid,
        };
}
