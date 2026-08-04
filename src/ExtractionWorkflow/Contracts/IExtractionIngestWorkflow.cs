using ExtractionWorkflow.Models;
using Tenancy.Models;

namespace ExtractionWorkflow.Contracts;

/// <summary>
/// Everything an authenticated accountant may do to start, re-run, or read one
/// firm-scoped extraction.
/// <para>
/// Every member takes a <see cref="TrustedFirmId"/> as its first argument, and
/// none has an overload, optional argument, or default that omits it. The firm
/// comes from the request's authorization state — never from a route, query,
/// form, body, header, or cookie — which is why no command record in this module
/// has a firm member.
/// </para>
/// <para>
/// The actor reference and the current instant are supplied by the caller for
/// the same reason: this workflow reads no clock, no environment, no current
/// user, and no HTTP context, so its behaviour is a function of its arguments
/// and is testable without a host.
/// </para>
/// <para>
/// A missing case or document and one belonging to another firm produce the same
/// <see cref="ExtractionResultCode.NotFound"/>. Nothing distinguishes them, so a
/// caller cannot learn that a document exists by being refused differently.
/// </para>
/// <para>
/// <b>Extraction is initiated only here, only by an explicit accountant action,
/// and only within one request.</b> There is no member a background worker,
/// hosted service, queue consumer, timer, client route, or anonymous route could
/// call to start one, and nothing in this contract schedules future work.
/// </para>
/// <para>
/// Classification override, correction, duplicate resolution, and acceptance are
/// deliberately absent: each is a review decision rather than an ingest, and
/// declaring a member this task does not implement would leave an unimplemented
/// contract behind.
/// </para>
/// </summary>
public interface IExtractionIngestWorkflow
{
    /// <summary>
    /// Verifies the four-part firm/case/document/content-key scope against
    /// server-stored state, opens a bounded content lease, classifies, extracts,
    /// runs the existing fail-closed chain, appends one immutable version and one
    /// structural audit event, and flags a probable duplicate within the same
    /// firm and case.
    /// <para>
    /// Fails closed throughout. A provider that throws, cancels, returns nothing,
    /// returns a null box collection, or returns a box with no code each produce
    /// a refusal with nothing stored and nothing thrown to the caller. A document
    /// nothing recognized produces no payload, no envelope, no processed
    /// extraction, and no evidence version — one audit event, and that is all.
    /// </para>
    /// </summary>
    Task<ExtractionResult<DocumentExtractionOutcome>> ExtractAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        ExtractDocumentCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Projects one firm-scoped document's current state from its append-only
    /// history. Read-only: it opens no lease, calls no provider, and appends
    /// nothing.
    /// </summary>
    Task<ExtractionResult<DocumentExtractionView>> GetDocumentAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Projects one firm-scoped case's extraction state. Read-only, on the same
    /// terms.
    /// </summary>
    Task<ExtractionResult<CaseExtractionView>> GetCaseAsync(
        TrustedFirmId firm,
        Guid caseId,
        CancellationToken cancellationToken);
}
