namespace ExtractionWorkflow.Models;

/// <summary>
/// The extraction workflow's own closed classification vocabulary.
/// <para>
/// It is deliberately not a second copy of
/// <c>DocumentExtraction.Models.ExtractedDocumentKind</c>: that enum is the
/// *envelope* discriminator the tax chain consumes, and it is reused unchanged
/// at the envelope boundary. This one answers a different question — what the
/// provider says the bytes are — and it needs a third member the envelope
/// vocabulary must never have, because an envelope is never built for a
/// document nothing recognized.
/// </para>
/// <para>
/// <see cref="Unsupported"/> is the zero value on purpose. A default-valued
/// classification is the one that produces no tax input, so a forgotten
/// assignment fails closed instead of manufacturing a slip.
/// </para>
/// </summary>
public enum DocumentClassification
{
    Unsupported = 0,
    T4Slip = 1,
    MedicalExpenseEvidence = 2
}

/// <summary>
/// Whether a human has accepted one immutable version.
/// <para>
/// There are exactly two states and the zero value is the un-accepted one.
/// Acceptance is always an explicit accountant act, so nothing a machine
/// produces can arrive already accepted. "Superseded" is deliberately absent:
/// a later version supersedes an earlier one by existing, and that is a
/// projection over append-only history rather than a state written back onto a
/// row that is supposed to be immutable.
/// </para>
/// </summary>
public enum ExtractionAcceptanceState
{
    AwaitingAccountantDecision = 0,
    AcceptedByAccountant = 1
}

/// <summary>
/// What happened to one field of one version.
/// <para>
/// This is a structural vocabulary, not a tax one. <see cref="EngineRejected"/>
/// records that the existing engine refused the value — this module never
/// decides that for itself, never downgrades a rejection to a warning, and
/// never invents a plausibility rule of its own.
/// </para>
/// </summary>
public enum FieldOutcomeKind
{
    Extracted = 0,
    Missing = 1,
    Malformed = 2,
    EngineRejected = 3
}

/// <summary>
/// The closed vocabulary of things an accountant may correct.
/// <para>
/// <b>There is no Box 12 member, and there is no member naming a social
/// insurance number in any form.</b> That is the point of declaring the
/// vocabulary as an enum: a correction of the SIN is not refused by a filter
/// somewhere downstream, it is unrepresentable. The same holds for the server
/// derived tax year, the firm/case/participant/document/content-key scope, an
/// engine-produced CRA line, and a validation result — none of them is a member
/// here, so no command can name one.
/// </para>
/// <para>
/// The medical members are exactly the approved medical evidence fields.
/// Provenance, confidence, and validation outcomes are machine-produced facts
/// about an extraction rather than values on the document, so they are not
/// correctable and are not members.
/// </para>
/// </summary>
public enum CorrectionField
{
    T4BoxValue = 0,
    T4BoxCode = 1,
    AddT4Box = 2,
    RemoveT4Box = 3,
    EmployerDisplayName = 4,
    EmployeeDisplayName = 5,
    EmployerAddress = 6,
    EmployeeAddress = 7,
    MedicalServiceDate = 8,
    MedicalGrossAmount = 9,
    MedicalCurrency = 10,
    MedicalProviderDisplayName = 11,
    MedicalPatientDisplayName = 12,
    MedicalReceiptReference = 13
}
