namespace ClientIntake.Models;

/// <summary>
/// The closed vocabularies of the client-intake workflow.
/// <para>
/// Every one of these is a fixed server-owned set. There is no free-text answer,
/// no caller-supplied status, and no "other" escape hatch that would let an
/// untrusted value become a stored fact. A value outside a declared set is
/// refused rather than coerced.
/// </para>
/// <para>
/// Deliberately absent: a document-category enum and a readiness enum. Those
/// already exist as <c>TaxCases.Models.DocumentCategory</c> and
/// <c>TaxCases.Models.ReadinessState</c>, and a second copy is how two answers
/// about the same taxpayer start to disagree.
/// </para>
/// </summary>
public enum AvailabilityAnswerCode
{
    AvailableNow = 0,
    WillProvideLater = 1,
    Unavailable = 2
}

/// <summary>
/// Per-expected-item progress. This is a projection, never a stored column:
/// there is no writable ready flag anywhere in the intake model.
/// </summary>
public enum ExpectedDocumentStatus
{
    Requested = 0,
    Uploaded = 1,
    Rejected = 2,
    Missing = 3,
    Ready = 4
}

/// <summary>
/// The only content types the upload boundary accepts. Each is verified by
/// signature, never by a declared header or a browser-supplied file name.
/// </summary>
public enum IntakeMediaKind
{
    Pdf = 0,
    Jpeg = 1,
    Png = 2
}

public enum IntakeUploadOutcome
{
    Accepted = 0,
    Rejected = 1,
    Duplicate = 2
}

/// <summary>
/// The derived state of the current bootstrap link. Precedence is revoked, then
/// spent, then expired, then active; it is computed from stored instants rather
/// than stored as its own mutable value.
/// </summary>
public enum IntakeLinkState
{
    Active = 0,
    Expired = 1,
    Revoked = 2,
    Spent = 3
}

public enum IntakeActorKind
{
    Accountant = 0,
    Client = 1
}

/// <summary>
/// The append-only audit vocabulary. An audit event records that something
/// structural happened; it never carries a raw credential, a digest, a file
/// name, a prompt, an amount, or any taxpayer content.
/// </summary>
public enum IntakeAuditKind
{
    CaseCreated = 0,
    ChecklistConfirmed = 1,
    LinkIssued = 2,
    LinkRedeemed = 3,
    LinkRevoked = 4,
    LinkReissued = 5,
    AnswerSaved = 6,
    UploadAccepted = 7,
    UploadRejected = 8,
    DuplicateRejected = 9,
    SubmissionCreated = 10,
    ReadinessChanged = 11,
    ReviewAssociated = 12
}

/// <summary>
/// The workflow's closed outcome vocabulary. It carries no message, no
/// identifier, and no field name — a failure code a caller can read is a code
/// that cannot become an oracle.
/// </summary>
public enum IntakeResultCode
{
    Success = 0,
    Invalid = 1,
    NotFound = 2,
    Conflict = 3,
    Duplicate = 4
}

public enum IntakeStoreWriteCode
{
    Success = 0,
    NotFound = 1,
    Conflict = 2,
    Duplicate = 3
}

/// <summary>
/// Whether the accountant may follow through to the existing read-only review
/// route. <c>Unavailable</c> is deliberately non-oracular: it does not
/// distinguish a deleted record from a foreign one.
/// </summary>
public enum ReviewHandoffState
{
    NotReady = 0,
    AwaitingReviewRecord = 1,
    Available = 2,
    Unavailable = 3
}

/// <summary>
/// Digest domain separation. The two purposes derive different keys from the
/// same secret, so a bootstrap-link digest can never be replayed as a session
/// digest.
/// </summary>
public enum IntakeTokenPurpose
{
    BootstrapLink = 0,
    ClientSession = 1
}
