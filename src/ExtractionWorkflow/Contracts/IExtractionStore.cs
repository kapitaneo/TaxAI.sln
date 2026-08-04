using ExtractionWorkflow.Models;
using Tenancy.Models;

namespace ExtractionWorkflow.Contracts;

/// <summary>
/// The only way to extraction metadata.
/// <para>
/// Concrete and named for what it stores. There is deliberately no generic
/// repository, no exposed queryable, and no unscoped lookup: a
/// <c>Save&lt;T&gt;</c> is a save nobody can constrain to a tenant, and an
/// exposed queryable hands the caller the ability to write its own unscoped
/// query.
/// </para>
/// <para>
/// <b>Every member appends.</b> There is no update, no delete, no upsert, and no
/// member that rewrites a stored row. A re-extraction, a correction, an
/// override, a duplicate decision, and an acceptance each add a fact; current
/// state is projected from those facts by the workflow rather than written back
/// over them. An immutable history that something can edit is not a history.
/// </para>
/// <para>
/// <b>Every member takes a <see cref="TrustedFirmId"/> as its first argument</b>,
/// with no overload, optional argument, or default that omits it, and every
/// member is additionally scoped to the server-stored case. There is no
/// firm-omitting taxpayer-data operation here: the two digest lookups in the
/// intake store remain the only ones in the system. A foreign-firm read is
/// indistinguishable from a missing one — both return null.
/// </para>
/// <para>
/// The retention boundary arrives as an argument, already computed by the
/// existing <c>RetentionPolicy</c> from the record's own tax year. This contract
/// declares no second retention authority, reads no clock, and derives no
/// boundary of its own.
/// </para>
/// </summary>
public interface IExtractionStore
{
    /// <summary>
    /// Appends one immutable extraction version with its non-SIN box values, its
    /// per-field outcomes, and one structural audit event, in one transaction.
    /// A version number already used for the document loses with
    /// <see cref="ExtractionStoreWriteCode.Conflict"/> and leaves nothing partial.
    /// </summary>
    Task<ExtractionStoreWriteCode> AppendExtractionVersionAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid participantId,
        ExtractionVersion version,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends one immutable medical evidence version and its structural audit
    /// event. Medical evidence is neutral evidence for a future
    /// medical-expense specification; nothing here makes it a tax result.
    /// </summary>
    Task<ExtractionStoreWriteCode> AppendMedicalEvidenceVersionAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid participantId,
        MedicalEvidenceVersion version,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    /// <summary>Appends one immutable correction and its audit event.</summary>
    Task<ExtractionStoreWriteCode> AppendCorrectionAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionCorrectionRecord correction,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends one immutable classification override and its audit event. The
    /// automated classification it names is retained beside it.
    /// </summary>
    Task<ExtractionStoreWriteCode> AppendClassificationOverrideAsync(
        TrustedFirmId firm,
        Guid caseId,
        ClassificationOverrideRecord classificationOverride,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends one probable-duplicate determination and its audit event. The
    /// relationship is between opaque identifiers; nothing derived from taxpayer
    /// data is stored.
    /// </summary>
    Task<ExtractionStoreWriteCode> AppendDuplicateCandidateAsync(
        TrustedFirmId firm,
        Guid caseId,
        DuplicateCandidateRecord candidate,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends the accountant's decision about one flagged candidate. The
    /// determination row is left exactly as it was.
    /// </summary>
    Task<ExtractionStoreWriteCode> AppendDuplicateResolutionAsync(
        TrustedFirmId firm,
        Guid caseId,
        DuplicateResolutionRecord resolution,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends one explicit human acceptance — of a document or of the case —
    /// and its audit event.
    /// </summary>
    Task<ExtractionStoreWriteCode> AppendAcceptanceAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionAcceptanceRecord acceptance,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends one structural audit event without manufacturing another stored
    /// fact. This is the fail-closed outcome for an attempted extraction that
    /// produces no supported extraction path.
    /// </summary>
    Task<ExtractionStoreWriteCode> AppendAuditEventAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionAuditEventRecord auditEvent,
        DateTimeOffset retentionUntilUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads one document's complete immutable history, or null when it does not
    /// exist in this firm's case — the two cases are indistinguishable.
    /// </summary>
    Task<StoredDocumentExtraction?> FindDocumentAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads one case's complete immutable extraction history, or null when it
    /// does not exist in this firm.
    /// </summary>
    Task<StoredCaseExtractions?> FindCaseAsync(
        TrustedFirmId firm,
        Guid caseId,
        CancellationToken cancellationToken);
}
