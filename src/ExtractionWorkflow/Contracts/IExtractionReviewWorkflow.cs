using ExtractionWorkflow.Models;
using Tenancy.Models;

namespace ExtractionWorkflow.Contracts;

/// <summary>
/// The authenticated, firm-scoped human-review boundary for extraction
/// history. Every mutation is explicit and append-only; no member updates,
/// deletes, reopens, replaces, or resolves a "latest" resource supplied by a
/// caller.
/// </summary>
public interface IExtractionReviewWorkflow
{
    Task<ExtractionResult<DocumentExtractionOutcome>> CorrectAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        ApplyExtractionCorrectionCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<ExtractionResult<DocumentExtractionView>> OverrideClassificationAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        OverrideExtractionClassificationCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<ExtractionResult<DocumentExtractionView>> ResolveDuplicateAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        ResolveExtractionDuplicateCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<ExtractionResult<DocumentExtractionView>> AcceptDocumentAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        AcceptExtractionDocumentCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<ExtractionResult<FinalizedExtractionReview>> FinalizeCaseAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        FinalizeExtractionCaseCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
