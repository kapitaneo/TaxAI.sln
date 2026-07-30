using System.Text.Json.Serialization;
using TaxCases.Models;

namespace ClientIntake.Models;

/// <summary>
/// The workflow's safe outputs: the one-time issuance results and the two
/// read projections.
/// <para>
/// These are the records that decide what a browser can ever see, so the
/// allowlist is the type. A field that is not here cannot be rendered by
/// forgetting to exclude it; it has to be added on purpose, in a diff, against
/// the PRD's field list.
/// </para>
/// <para>
/// Identifiers, credentials, digests, and taxpayer text are
/// <c>[JsonIgnore]</c>-marked and every <c>ToString()</c> is structural. There
/// is no JSON API in this spec — the attribute is defence in depth against a
/// future one, and against a diagnostic sink that serializes whatever it is
/// handed.
/// </para>
/// </summary>
public sealed record CreatedIntakeCase(
    [property: JsonIgnore] Guid CaseId,
    [property: JsonIgnore] IntakeSecret LinkCredential,
    DateTimeOffset LinkExpiresAtUtc)
{
    public override string ToString() => nameof(CreatedIntakeCase);
}

public sealed record IssuedIntakeLink(
    [property: JsonIgnore] IntakeSecret LinkCredential,
    DateTimeOffset LinkExpiresAtUtc)
{
    public override string ToString() => nameof(IssuedIntakeLink);
}

/// <summary>
/// The proof a client's confirmation POST carries instead of the raw credential.
/// It is derived from the digest, so a redemption cannot be replayed from a
/// browser history entry or a referrer header.
/// </summary>
public sealed record IntakeRedemptionProof(
    [property: JsonIgnore] IntakeTokenDigest LinkDigest,
    DateTimeOffset LinkExpiresAtUtc)
{
    public override string ToString() => nameof(IntakeRedemptionProof);
}

/// <summary>
/// The result of a read-only link inspection: the tax year the client is being
/// asked about, and nothing that identifies the firm, case, or taxpayer.
/// </summary>
public sealed record IntakeLinkInspection(
    int TaxYear,
    [property: JsonIgnore] IntakeRedemptionProof Proof)
{
    public override string ToString() => nameof(IntakeLinkInspection);
}

public sealed record RedeemedIntakeSession(
    [property: JsonIgnore] IntakeSecret SessionCredential,
    DateTimeOffset AbsoluteExpiresAtUtc)
{
    public override string ToString() => nameof(RedeemedIntakeSession);
}

/// <summary>
/// One server-owned questionnaire question. The prompt is authored by the
/// server, carried in memory, and contextually encoded by the component; it is
/// never serialized and never accepted from a caller.
/// </summary>
public sealed record IntakeQuestionView(
    DocumentCategory Category,
    [property: JsonIgnore] string Prompt,
    IReadOnlyList<AvailabilityAnswerCode> Options,
    AvailabilityAnswerCode? CurrentAnswer)
{
    public IReadOnlyList<AvailabilityAnswerCode> Options { get; } =
        Array.AsReadOnly((Options ?? throw new ArgumentNullException(nameof(Options))).ToArray());

    public override string ToString() =>
        $"{nameof(IntakeQuestionView)}({Category}, options: {Options.Count})";
}

/// <summary>
/// One expected checklist slot. The status is derived from immutable history
/// using the PRD precedence; it is never a stored column.
/// </summary>
public sealed record ExpectedDocumentView(
    DocumentCategory Category,
    int Ordinal,
    ExpectedDocumentStatus Status)
{
    public override string ToString() =>
        $"{nameof(ExpectedDocumentView)}({Category}, {Ordinal}, {Status})";
}

/// <summary>
/// Everything a client browser may know. Deliberately absent: display name,
/// firm, tenant, case, participant, document, user, membership, session,
/// content, fingerprint, review, and audit identifiers.
/// </summary>
public sealed record ClientIntakeView(
    int TaxYear,
    IReadOnlyList<IntakeQuestionView> Questions,
    IReadOnlyList<ExpectedDocumentView> ExpectedDocuments,
    int? LatestSubmissionVersion,
    bool UnsubmittedChanges)
{
    public IReadOnlyList<IntakeQuestionView> Questions { get; } =
        Array.AsReadOnly((Questions ?? throw new ArgumentNullException(nameof(Questions))).ToArray());

    public IReadOnlyList<ExpectedDocumentView> ExpectedDocuments { get; } =
        Array.AsReadOnly(
            (ExpectedDocuments ?? throw new ArgumentNullException(nameof(ExpectedDocuments))).ToArray());

    public override string ToString() =>
        $"{nameof(ClientIntakeView)}(questions: {Questions.Count}, "
        + $"documents: {ExpectedDocuments.Count}, unsubmitted: {UnsubmittedChanges})";
}

/// <summary>
/// The accountant progress allowlist.
/// <para>
/// The display name is the one approved display-PII field and is
/// <c>[JsonIgnore]</c>-marked; the review record identifier is present only so
/// the component can build the existing authorized
/// <c>/reviews/{recordId:guid}</c> URL when the handoff is available, and it is
/// never otherwise rendered.
/// </para>
/// </summary>
public sealed record AccountantIntakeView(
    [property: JsonIgnore] string PrimaryTaxpayerDisplayName,
    int TaxYear,
    CaseStatus CaseStatus,
    ReadinessState Readiness,
    int ExpectedT4Count,
    int ExpectedMedicalExpenseCount,
    IReadOnlyList<ExpectedDocumentView> ExpectedDocuments,
    int AnsweredQuestionCount,
    int ExpectedQuestionCount,
    int? LatestSubmissionVersion,
    bool UnsubmittedChanges,
    IntakeLinkState LinkState,
    DateTimeOffset LinkIssuedAtUtc,
    DateTimeOffset LinkExpiresAtUtc,
    DateTimeOffset? LinkRevokedAtUtc,
    DateTimeOffset? LinkSpentAtUtc,
    ReviewHandoffState HandoffState,
    [property: JsonIgnore] Guid? ReviewRecordId)
{
    public IReadOnlyList<ExpectedDocumentView> ExpectedDocuments { get; } =
        Array.AsReadOnly(
            (ExpectedDocuments ?? throw new ArgumentNullException(nameof(ExpectedDocuments))).ToArray());

    public override string ToString() =>
        $"{nameof(AccountantIntakeView)}({CaseStatus}, {Readiness}, {LinkState}, "
        + $"{HandoffState}, documents: {ExpectedDocuments.Count})";
}
