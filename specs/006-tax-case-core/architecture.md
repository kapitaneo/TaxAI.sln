# Architecture: Tax Case Core (spec 006)

**Feature class: `tax-domain`** (per the PRD's `## Feature classification`). All
tax-domain protections apply: explicit tax-year scope, fail-closed behaviour,
SIN masking, no hardcoded CRA line numbers.

## Approach

Tax Case Core becomes the repository's **second module**: a new .NET 8.0 class
library at `src/TaxCases/` (assembly and root namespace `TaxCases`), with its
test suite at `tests/TaxCases.Tests/`. Both are added to `TaxAI.sln`. Per
CLAUDE.md's dependency rules, `TaxCases` references `TaxLogicEngine` **inward
only** — solely to reuse the three domain value types the PRD mandates
(`ParseResult<T>`, `ValidationError`, `RecipientIdentifier`). It references
nothing else: no ASP.NET Core, no EF Core, no OCR/LLM/cloud SDKs, no I/O.
`TaxLogicEngine` itself is **not modified** by this feature.

`src/TaxCases/TaxCases.csproj` targets `net8.0`, enables nullable reference
types and implicit usings, and has exactly one `ProjectReference`:
`..\TaxLogicEngine\TaxLogicEngine.csproj`. The test project mirrors the existing
test conventions (`Microsoft.NET.Test.Sdk` 17.8.0, xunit 2.5.3,
`xunit.runner.visualstudio` 2.5.3, coverlet.collector 6.0.0) and references only
`..\..\src\TaxCases\TaxCases.csproj`. `TaxLogicEngine` gains no reference back.

Inside the module the layout mirrors the engine's convention:

- **`Models/`** — the `TaxCase` aggregate, its participants, evidence records,
  checklist, readiness result, domain change record, enums, and the caller-input
  descriptor records.
- **`Services/`** — the small pure-policy services: the supported-tax-year set,
  the lifecycle transition table, and the readiness calculator.

There is no `Mappings/` folder and no JSON config in this module: Tax Case Core
carries **no CRA box, line, rate, or mapping** (PRD requirement 16/AC-40), so a
mapping folder here would itself be a boundary violation.

### The aggregate is immutable; operations return outcomes

`TaxCase` is an **immutable aggregate**. Every operation (create, add
participant, attach evidence, answer question, confirm checklist, transition,
reopen) is a pure function that returns
`ParseResult<CaseOperationOutcome>`, where a success outcome carries **the new
`TaxCase` instance** plus **exactly one immutable `CaseChangeRecord`**. A
failure returns `ParseResult<>.Failure` with ≥1 `ValidationError` and — because
the original instance is never touched — "no state mutation on failure"
(requirement 31, AC-33) holds **by construction**, not by discipline. The same
construction makes determinism (AC-35/43) trivially testable: replaying the same
operation sequence twice produces structurally equal outcomes.

Change records are **not accumulated on the case** — the outcome hands each
record to the caller, and persistence/delivery of records is explicitly out of
scope (requirement 29). `CaseChangeRecord` contains only scalar/string fields so
record equality gives the deterministic comparison AC-35 needs. Raw opaque
domain text may be retained in properties where the PRD requires it, but is
never included verbatim in diagnostic output (see **PII-safe domain text versus
diagnostics** below).

`TaxCase` is a **partial class split across one file per operation family**
(`TaxCase.cs` core state + `TaxCase.Create.cs`, `TaxCase.Participants.cs`,
`TaxCase.Evidence.cs`, `TaxCase.Intake.cs`, `TaxCase.Lifecycle.cs`). This is a
deliberate concession to the wave rules in tasks.md: sibling tasks in one wave
must not share a file path, and the partial split lets each operation family be
implemented and tested on its own branch. The Wave-0 contract task creates
**all** of these files (state plumbing implemented; operation bodies
`throw new NotImplementedException()`), so no later task ever adds a file a
sibling references.

### Untrusted input at the edge

Caller input arrives as **descriptor values** (`CreateTaxCaseRequest`,
`ParticipantDescriptor`, `EvidenceDescriptor`, `IntakeAnswerDescriptor`,
`ChecklistDescriptor`). Where the PRD requires an *unknown* value to fail closed
with an error naming it (document categories, AC-11), the descriptor field is a
**string** parsed inside the operation — an enum parameter could never represent
"unknown category" honestly. Validated state stored on the case uses the enums.
A raw SIN enters as `string?` on the descriptor and exists past the validation
boundary **only** as `RecipientIdentifier` (requirement 11, AC-41).

### PII-safe domain text versus diagnostics

TaxCases domain objects are **not serialization contracts**. Application/API
layers must map them to explicitly reviewed output DTOs; API DTOs are out of
scope here. Supported direct `System.Text.Json` serialization is defensive only:
**every caller-controlled string property** is marked `[JsonIgnore]`, including
case, firm, participant, evidence, requirement, and question identifiers plus
`DisplayName`, `Prompt`, `Answer`, `SourceReference`, `CorrelationReference`,
change-record `Detail`, and untrusted descriptor category strings. The values
remain accessible through their explicit in-memory properties where domain
behaviour or audit handling requires them. The only identifier-adjacent strings
that supported JSON may emit are the safe masked projections
`RecipientIdentifier.Masked` and `ParticipantDescriptor.MaskedSin`; neither can
emit the raw SIN.

Every public TaxCases `ToString()` is explicitly overridden. It either omits raw
free text or passes displayed identifiers through the internal deterministic
`SafeDiagnosticText.RedactSinLikeSequences(string?)` helper. Scanning left to
right using only ordinal ASCII comparisons, the helper replaces every
non-overlapping occurrence of (a) nine consecutive ASCII digits, (b) three
three-digit groups separated by literal hyphens, or (c) three three-digit groups
separated by literal spaces. A maximal compact run of nine or more digits is
collapsed once and retains only its final three digits. All three forms use the
same output: `123456789`, `123-456-789`, and `123 456 789` each become
`***-**-789`. Formatted patterns are recognized before compact patterns at the
same position; scanning resumes after the full match. The helper is deterministic
and culture-independent and uses no I/O, clock, environment data, randomness, or
external package. `ValidationError.Message`, `ReadinessReason.Detail`, and every
change-record diagnostic form use only fixed text plus redacted identifiers.
Errors never echo raw rejected free text; when an unknown category must be
identified for AC-11, the message includes only
`SafeDiagnosticText.RedactSinLikeSequences(category)`. This preserves the actual answer,
source reference, correlation reference, and Reopen reason in domain properties
while preventing generated record diagnostics, defensive direct JSON serialization,
errors, and readiness output from emitting a full SIN.

The helper's fixed internal contract is:

```csharp
internal static class SafeDiagnosticText
{
    internal static string? RedactSinLikeSequences(string? value);
}
```

It returns `null` for `null`, returns the original string when no pattern exists,
and otherwise returns the ordinally scanned replacement string described above.

## Data model

All types are new, in `TaxCases.Models` unless noted. No existing
`TaxLogicEngine` type is re-specified or duplicated. **No money fields exist in
this feature** — the case records receipt of evidence, never amounts — so the
`MoneyRounding`/decimal rule is satisfied vacuously and no currency math may be
added without a new spec.

### Enums (fixed sets, per PRD `[ASSUMED]` decisions)

| Enum | Values | Notes |
|---|---|---|
| `RelationshipKind` | `PrimaryTaxpayer`, `SpouseOrCommonLaw`, `Dependant` | exactly one primary; ≤1 spouse; ≥0 dependants |
| `DocumentCategory` | `T4Slip`, `MedicalExpenseEvidence`, `Other` | category names only — **no parsing, no box semantics** |
| `CaseStatus` | `Intake`, `InformationGathering`, `InReview`, `Completed`, `Cancelled` | **no `Archived`** (PRD Fix 3) |
| `CaseChangeKind` | `CaseCreated`, `ParticipantAdded`, `EvidenceAttached`, `QuestionAnswered`, `ChecklistConfirmed`, `StatusChanged`, `CaseReopened` | one per accepted operation |
| `ReadinessState` | `Ready`, `NotReady` | |
| `ReadinessReasonKind` | `ChecklistNotConfigured`, `UnsatisfiedEvidenceRequirement`, `UnansweredIntakeQuestion`, `NoPrimaryTaxpayer` | reason taxonomy for AC-23/24 and participant-scoped requirements |
| `IntakeQuestionScope` | `Case`, `Participant` | case-level questions have no participant; participant questions target exactly one in-case participant |

### Value objects and records

- **`CaseParticipant`** — sealed record: `string ParticipantId` (caller-supplied,
  non-empty, unique within the case), `RelationshipKind Relationship`,
  `string? DisplayName`, `RecipientIdentifier? Sin` (optional, per decision A2).
  `ToString()` is **explicitly overridden** to emit the masked SIN form only —
  C# records auto-generate a property-dumping `ToString()`, and although
  `RecipientIdentifier` masks itself, the override plus the AC-37 sweep test
  give defence in depth. The stored participant does **not** carry its own firm
  id; it inherits the case's (the descriptor may carry one for cross-firm
  rejection — see below).
- **`ReceivedEvidence`** — sealed record: `string EvidenceId` (caller-supplied,
  non-empty, unique within the case, distinct from category),
  `DocumentCategory Category`, `string ParticipantId`, `int TaxYear` (must equal
  the case's), `string? SourceReference` (opaque). A record of *receipt* only —
  no file bytes, no parsed content, no amounts.
- **`EvidenceRequirement`** — sealed record: `string RequirementId` (stable,
  non-empty, unique within the checklist), `string ParticipantId` (must identify
  a participant in this case), `DocumentCategory Category`, and
  `int MinimumCount` (strictly positive). A requirement is participant-scoped;
  a case-wide category alone is never a requirement.
- **`IntakeQuestion`** — sealed record: `string QuestionId` (non-empty, unique
  within the checklist), `string Prompt` (non-empty),
  `IntakeQuestionScope Scope`, and `string? ParticipantId`. `Case` scope requires
  a null participant; `Participant` scope requires exactly one non-empty
  participant id belonging to the case. Questions enter the case **only** via
  checklist confirmation.
- **`IntakeAnswer`** — sealed record: `string QuestionId`,
  `string? ParticipantId`, `string Answer` (opaque). A case-level question
  requires a null answer participant; a participant-targeted question requires
  the exact target participant. An answer attributed to any other participant
  fails closed.
- **`RequirementChecklist`** — sealed record:
  `IReadOnlyList<EvidenceRequirement> EvidenceRequirements`,
  `IReadOnlyList<IntakeQuestion> Questions`. May be empty — an **explicitly
  confirmed empty checklist** is valid and distinct from "not configured"
  (requirement 19). The configured/unconfigured distinction is the nullability
  of `TaxCase.Checklist` (`RequirementChecklist?`): `null` = never confirmed.
- **`ReviewReadiness`** — sealed record: `ReadinessState State`,
  `IReadOnlyList<ReadinessReason> Reasons`;
  **`ReadinessReason`** — sealed record: `ReadinessReasonKind Kind`,
  `string Detail` (already SIN-safe: names a redacted requirement/question id
  and remaining count, never prompt/answer/free text).
- **`CaseChangeRecord`** — sealed record, **scalar fields only** (so record
  equality is deterministic): `string CaseId`, `CaseChangeKind Kind`,
  `string? ParticipantId`, `string? EvidenceId`, `string? QuestionId`,
  `CaseStatus? PreviousStatus`, `CaseStatus? ResultingStatus`,
  `string? CorrelationReference` and `string? Detail` (e.g. the Reopen reason)
  retain their caller-supplied domain values for equality/audit handling but are
  excluded from supported JSON and diagnostic `ToString()`. **No timestamp
  field exists** — requirement 30; if one is ever needed it arrives as an
  explicit input in a future spec.
- **`CaseOperationOutcome`** — sealed record: `TaxCase Case`,
  `CaseChangeRecord Change`. The success payload of every operation.

### Caller-input descriptors (untrusted)

- **`CreateTaxCaseRequest`** — `string? CaseId` (caller-supplied — the core never
  generates identifiers), `string? FirmId`, `int? TaxYear` (nullable so "absent
  year" fails closed rather than defaulting, AC-4),
  `ParticipantDescriptor? PrimaryTaxpayer`, `string? CorrelationReference`.
- **`ParticipantDescriptor`** — sealed class constructed from
  `string? participantId`, `RelationshipKind relationship`,
  `string? displayName`, `string? sin`, `string? firmId`. The raw SIN is held in
  an **internal non-serialized member**, never a public property; public output
  exposes only `MaskedSin`, and `ToString()` is overridden to use that masked
  value without throwing even for invalid input. On acceptance the raw input is
  wrapped into `RecipientIdentifier` and
  only that engine type enters case state. This avoids the positional-record
  trap where an input DTO's generated `Sin` property/`ToString()`/JSON output
  would expose the raw value. `FirmId`, when present and unequal to the case's,
  causes cross-firm rejection (AC-12).
- **`EvidenceDescriptor`** — `string? EvidenceId`, `string? Category` (**string**,
  parsed; missing/unknown fails closed naming it, AC-11/21), `string? ParticipantId`,
  `int? TaxYear`, `string? SourceReference`, `string? FirmId` (same cross-firm
  rule).
- **`EvidenceRequirementDescriptor`** — `string? RequirementId`,
  `string? ParticipantId`, `string? Category` (parsed with the same exact
  category rules as evidence), and `int? MinimumCount` (nullable so absence is
  rejected rather than defaulted). The participant must already belong to the
  case.
- **`IntakeAnswerDescriptor`** — `string? QuestionId`, `string? ParticipantId`,
  `string? Answer`; a missing/blank answer is rejected and does not count as an
  answer `[ASSUMED — Class B]`.
- **`ChecklistDescriptor`** —
  `IReadOnlyList<EvidenceRequirementDescriptor>? EvidenceRequirements`,
  `IReadOnlyList<IntakeQuestion>? Questions`.
  The lists themselves must be present, but both may be empty
  (confirmed-empty checklist).

### Exact Wave 0 public contract

The following is the **complete public-surface signature manifest Wave 0 must
compile**. Names,
namespaces, nullability, enum values, constructors, property types, and method
signatures are fixed here; later waves implement these bodies without changing
the contract. Records use the shown positional public constructors. A semicolon
after a method denotes its fixed signature (the actual Wave-0 source supplies a
compiling body). Numeric enum values are explicit so contract tests can pin them.

```csharp
namespace TaxCases.Models;

public enum RelationshipKind
{
    PrimaryTaxpayer = 0,
    SpouseOrCommonLaw = 1,
    Dependant = 2
}

public enum DocumentCategory
{
    T4Slip = 0,
    MedicalExpenseEvidence = 1,
    Other = 2
}

public enum CaseStatus
{
    Intake = 0,
    InformationGathering = 1,
    InReview = 2,
    Completed = 3,
    Cancelled = 4
}

public enum CaseChangeKind
{
    CaseCreated = 0,
    ParticipantAdded = 1,
    EvidenceAttached = 2,
    QuestionAnswered = 3,
    ChecklistConfirmed = 4,
    StatusChanged = 5,
    CaseReopened = 6
}

public enum ReadinessState { Ready = 0, NotReady = 1 }

public enum ReadinessReasonKind
{
    ChecklistNotConfigured = 0,
    UnsatisfiedEvidenceRequirement = 1,
    UnansweredIntakeQuestion = 2,
    NoPrimaryTaxpayer = 3
}

public enum IntakeQuestionScope
{
    Case = 0,
    Participant = 1
}

public sealed record CaseParticipant(
    [property: System.Text.Json.Serialization.JsonIgnore] string ParticipantId,
    RelationshipKind Relationship,
    [property: System.Text.Json.Serialization.JsonIgnore] string? DisplayName,
    TaxLogicEngine.Models.RecipientIdentifier? Sin)
{
    public override string ToString();
}

public sealed record ReceivedEvidence(
    [property: System.Text.Json.Serialization.JsonIgnore] string EvidenceId,
    DocumentCategory Category,
    [property: System.Text.Json.Serialization.JsonIgnore] string ParticipantId,
    int TaxYear,
    [property: System.Text.Json.Serialization.JsonIgnore]
    string? SourceReference);

public sealed record EvidenceRequirement(
    [property: System.Text.Json.Serialization.JsonIgnore] string RequirementId,
    [property: System.Text.Json.Serialization.JsonIgnore] string ParticipantId,
    DocumentCategory Category,
    int MinimumCount);

public sealed record IntakeQuestion(
    [property: System.Text.Json.Serialization.JsonIgnore] string QuestionId,
    [property: System.Text.Json.Serialization.JsonIgnore] string Prompt,
    IntakeQuestionScope Scope,
    [property: System.Text.Json.Serialization.JsonIgnore] string? ParticipantId);

public sealed record IntakeAnswer(
    [property: System.Text.Json.Serialization.JsonIgnore] string QuestionId,
    [property: System.Text.Json.Serialization.JsonIgnore] string? ParticipantId,
    [property: System.Text.Json.Serialization.JsonIgnore] string Answer);

public sealed record RequirementChecklist(
    IReadOnlyList<EvidenceRequirement> EvidenceRequirements,
    IReadOnlyList<IntakeQuestion> Questions);

public sealed record ReadinessReason(
    ReadinessReasonKind Kind,
    [property: System.Text.Json.Serialization.JsonIgnore] string Detail);
public sealed record ReviewReadiness(
    ReadinessState State,
    IReadOnlyList<ReadinessReason> Reasons);

public sealed record CaseChangeRecord(
    [property: System.Text.Json.Serialization.JsonIgnore] string CaseId,
    CaseChangeKind Kind,
    [property: System.Text.Json.Serialization.JsonIgnore] string? ParticipantId,
    [property: System.Text.Json.Serialization.JsonIgnore] string? EvidenceId,
    [property: System.Text.Json.Serialization.JsonIgnore] string? QuestionId,
    CaseStatus? PreviousStatus,
    CaseStatus? ResultingStatus,
    [property: System.Text.Json.Serialization.JsonIgnore]
    string? CorrelationReference,
    [property: System.Text.Json.Serialization.JsonIgnore]
    string? Detail);

public sealed record CaseOperationOutcome(TaxCase Case, CaseChangeRecord Change);

public sealed record CreateTaxCaseRequest(
    [property: System.Text.Json.Serialization.JsonIgnore] string? CaseId,
    [property: System.Text.Json.Serialization.JsonIgnore] string? FirmId,
    int? TaxYear,
    ParticipantDescriptor? PrimaryTaxpayer,
    [property: System.Text.Json.Serialization.JsonIgnore]
    string? CorrelationReference);

public sealed class ParticipantDescriptor
{
    public ParticipantDescriptor(
        string? participantId,
        RelationshipKind relationship,
        string? displayName,
        string? sin,
        string? firmId);

    [System.Text.Json.Serialization.JsonIgnore]
    public string? ParticipantId { get; }
    public RelationshipKind Relationship { get; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? DisplayName { get; }
    public string? MaskedSin { get; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? FirmId { get; }
    public override string ToString();
}

public sealed record EvidenceDescriptor(
    [property: System.Text.Json.Serialization.JsonIgnore] string? EvidenceId,
    [property: System.Text.Json.Serialization.JsonIgnore] string? Category,
    [property: System.Text.Json.Serialization.JsonIgnore] string? ParticipantId,
    int? TaxYear,
    [property: System.Text.Json.Serialization.JsonIgnore]
    string? SourceReference,
    [property: System.Text.Json.Serialization.JsonIgnore] string? FirmId);

public sealed record EvidenceRequirementDescriptor(
    [property: System.Text.Json.Serialization.JsonIgnore] string? RequirementId,
    [property: System.Text.Json.Serialization.JsonIgnore] string? ParticipantId,
    [property: System.Text.Json.Serialization.JsonIgnore] string? Category,
    int? MinimumCount);

public sealed record IntakeAnswerDescriptor(
    [property: System.Text.Json.Serialization.JsonIgnore] string? QuestionId,
    [property: System.Text.Json.Serialization.JsonIgnore] string? ParticipantId,
    [property: System.Text.Json.Serialization.JsonIgnore]
    string? Answer);

public sealed record ChecklistDescriptor(
    IReadOnlyList<EvidenceRequirementDescriptor>? EvidenceRequirements,
    IReadOnlyList<IntakeQuestion>? Questions);

public sealed partial class TaxCase
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string CaseId { get; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string FirmId { get; }
    public int TaxYear { get; }
    public CaseStatus Status { get; }
    public IReadOnlyList<CaseParticipant> Participants { get; }
    public IReadOnlyList<ReceivedEvidence> Evidence { get; }
    public RequirementChecklist? Checklist { get; }
    public IReadOnlyList<IntakeAnswer> Answers { get; }
    public ReviewReadiness Readiness { get; }

    public static TaxLogicEngine.Models.ParseResult<CaseOperationOutcome> Create(
        CreateTaxCaseRequest request);
    public TaxLogicEngine.Models.ParseResult<CaseOperationOutcome> AddParticipant(
        ParticipantDescriptor participant,
        string? correlationReference);
    public TaxLogicEngine.Models.ParseResult<CaseOperationOutcome> AttachEvidence(
        EvidenceDescriptor evidence,
        string? correlationReference);
    public TaxLogicEngine.Models.ParseResult<CaseOperationOutcome> ConfirmChecklist(
        ChecklistDescriptor checklist,
        string? correlationReference);
    public TaxLogicEngine.Models.ParseResult<CaseOperationOutcome> AnswerQuestion(
        IntakeAnswerDescriptor answer,
        string? correlationReference);
    public TaxLogicEngine.Models.ParseResult<CaseOperationOutcome> TransitionTo(
        CaseStatus target,
        string? correlationReference);
    public TaxLogicEngine.Models.ParseResult<CaseOperationOutcome> Reopen(
        string? reason,
        string? correlationReference);
}

namespace TaxCases.Services;

public static class SupportedTaxYears
{
    public static IReadOnlyCollection<int> All { get; }
    public static bool IsSupported(int year);
}

public static class CaseStatusTransitions
{
    public static bool IsAllowed(
        TaxCases.Models.CaseStatus from,
        TaxCases.Models.CaseStatus to);
}

public static class ReadinessCalculator
{
    public static TaxCases.Models.ReviewReadiness Calculate(
        TaxCases.Models.TaxCase taxCase);
}
```

In addition to the shown domain members, **every public non-static class or
record with instance state declares its own `public override string ToString()`;
enums and static service classes are excluded** with the safe-diagnostic
behaviour defined earlier; contract tests verify `ToString().DeclaringType` is
the concrete TaxCases type rather than a compiler-generated record formatter.
The internal `SafeDiagnosticText` helper is not public API.

The `[JsonIgnore]` attributes in the manifest are part of the Wave 0 contract,
not implementation suggestions. Reflection tests enumerate every public string
property in `TaxCases` and require `[JsonIgnore]` unless the property is the
explicit safe projection `ParticipantDescriptor.MaskedSin`. Raw string getters
remain public/in-memory exactly where shown; omitting them from defensive JSON
does not erase or transform domain state.

Serialization coverage is exhaustive:

| Caller-controlled string family | Domain locations | Defensive JSON |
|---|---|---|
| Case/firm ids | request, aggregate, change record, participant/evidence descriptors | omitted |
| Participant ids | participant/evidence/requirement/question/answer state and descriptors, change record | omitted |
| Evidence/requirement/question ids | state, descriptors, readiness-derived/change-record fields | omitted |
| Display/prompt/answer/source/correlation/detail text | participant, question, answer, evidence, requests/descriptors, change record | omitted |
| Untrusted category string | evidence and requirement descriptors | omitted |
| Accepted enum/category/year/count/status/scope values | validated state | emitted (not caller text) |
| Participant SIN | `RecipientIdentifier.Masked` / descriptor `MaskedSin` only | masked projection only |

The operation parameters remain non-null reference types where callers are
expected to supply an object; implementations nevertheless validate `null` at
runtime and return `ParseResult<>.Failure` rather than throwing. All accepted
collections are defensively copied to private arrays before storage, and all
exposed collections are immutable/read-only snapshots; caller mutation of an
input list cannot mutate an accepted case.

### The aggregate

- **`TaxCase`** — sealed partial class, **private constructor**, all properties
  get-only: `string CaseId`, `string FirmId` (**immutable; no public member can
  change it** — AC-13 is verified by reflection over the public surface),
  `int TaxYear`, `CaseStatus Status`,
  `IReadOnlyList<CaseParticipant> Participants`,
  `IReadOnlyList<ReceivedEvidence> Evidence`,
  `RequirementChecklist? Checklist` (null = not configured),
  `IReadOnlyList<IntakeAnswer> Answers`, and the **derived, get-only**
  `ReviewReadiness Readiness` property delegating to `ReadinessCalculator`
  (no writable setter anywhere — AC-17). Collections preserve insertion order
  (determinism). The core `TaxCase.cs` file also holds the internal plumbing the
  operation files share: the private constructor, an internal `With(...)` clone
  helper, a `GuardMutable()` helper (returns a `ValidationError` when `Status`
  is `Completed` or `Cancelled` — requirement 26/28), and a change-record
  builder.

## Services & public API

### `TaxCase` operations (in `TaxCases.Models`, partial-class files)

Every rejectable operation returns `ParseResult<CaseOperationOutcome>` (reused
fail-closed convention, AC-42). Every accepted operation emits exactly one
`CaseChangeRecord`; a rejected one emits none and leaves every existing instance
untouched.

| Operation | Signature | Validates (fail-closed) |
|---|---|---|
| Create | `static ParseResult<CaseOperationOutcome> Create(CreateTaxCaseRequest request)` | non-empty `CaseId`/`FirmId`; `TaxYear` present **and** in `SupportedTaxYears` (never coerced, no adjacent-year fallback); primary participant present, relationship `PrimaryTaxpayer`, valid id, and descriptor firm id absent or equal to the case firm; optional SIN wraps into `RecipientIdentifier`; one error per missing field (AC-36). Emits `CaseCreated`. Initial status `Intake`. |
| Add participant | `ParseResult<CaseOperationOutcome> AddParticipant(ParticipantDescriptor p, string? correlationReference)` | case mutable; non-empty `ParticipantId`; no duplicate id; ≤1 `PrimaryTaxpayer` total, ≤1 `SpouseOrCommonLaw` total; descriptor firm id (if any) matches case; SIN optional. Emits `ParticipantAdded`. |
| Attach evidence | `ParseResult<CaseOperationOutcome> AttachEvidence(EvidenceDescriptor e, string? correlationReference)` | case mutable; non-empty `EvidenceId`; no duplicate `EvidenceId` on the case; category parses to a supported `DocumentCategory` (error names the unknown category); `ParticipantId` is in the case; `TaxYear` present and **equal to the case's** (tax-year isolation — never silently adopted); firm id (if any) matches. Emits `EvidenceAttached`. |
| Confirm checklist | `ParseResult<CaseOperationOutcome> ConfirmChecklist(ChecklistDescriptor c, string? correlationReference)` | case mutable; both lists present; each evidence requirement has a non-blank unique `RequirementId`, in-case `ParticipantId`, supported category string, and `MinimumCount > 0`; question ids are non-blank/unique, prompts non-blank, and scope/participant combinations valid (`Case` + null, or `Participant` + in-case id); empty checklist allowed (explicit confirm ≠ unconfigured). Emits `ChecklistConfirmed`. Re-confirmation while non-terminal replaces the checklist and clears answers for removed or retargeted questions `[ASSUMED — Class B]`. |
| Answer question | `ParseResult<CaseOperationOutcome> AnswerQuestion(IntakeAnswerDescriptor a, string? correlationReference)` | case mutable; question id exists in the confirmed checklist; answer is non-blank; a case-level question requires null answer participant, while a participant question requires the exact target participant. Unknown, cross-participant, or incorrectly scoped attribution fails closed. Emits `QuestionAnswered`. Re-answering replaces the prior answer `[ASSUMED — Class B]`. |
| Transition | `ParseResult<CaseOperationOutcome> TransitionTo(CaseStatus target, string? correlationReference)` | transition allowed by `CaseStatusTransitions`; transition **into `InReview` additionally requires `Readiness.State == Ready`** (decision A3); `Completed`/`Cancelled` reject normal transitions. Emits `StatusChanged` with previous and resulting status. |
| Reopen | `ParseResult<CaseOperationOutcome> Reopen(string reason, string? correlationReference)` | only from `Completed` (a `Cancelled` case rejects Reopen — terminal); reason non-empty/non-blank; result status `InformationGathering`; readiness recomputed from state, never restored. Emits `CaseReopened` with the reason in `Detail`. |

### Pure policy services (in `TaxCases.Services`)

- **`SupportedTaxYears`** — static class, the single source of the spec-006
  product-scope policy: `static bool IsSupported(int year)` (true only for
  2025), `static IReadOnlyCollection<int> All`. The case **never** inspects
  `TaxLogicEngine` mapping files to decide validity (requirement 5); adding a
  year here is a deliberate scope change in exactly one place.
- **`CaseStatusTransitions`** — static class:
  `static bool IsAllowed(CaseStatus from, CaseStatus to)`. Encodes `Intake` →
  `InformationGathering` → `InReview` → `Completed`, plus `Cancelled` from any
  non-terminal status; everything else false. Backward `InReview` →
  `InformationGathering` is allowed `[ASSUMED — Class B]` (an accountant sends a
  case back for more information); `Completed` is reachable only from
  `InReview`. Reopen is **not** in this table — it is a distinct guarded
  operation, so `Completed → InformationGathering` via plain `TransitionTo`
  fails closed.
- **`ReadinessCalculator`** — static class:
  `static ReviewReadiness Calculate(TaxCase taxCase)`. Pure derivation
  (decision A3): `Ready` iff the checklist is non-null (explicitly confirmed —
  an unconfigured checklist alone forces `NotReady`), exactly one
  `PrimaryTaxpayer` exists, and every `EvidenceRequirement` is allocated at
  least `MinimumCount` **distinct, not-previously-allocated** evidence records
  matching both its `ParticipantId` and exact `DocumentCategory`. Requirements
  are evaluated in checklist insertion order and matching evidence in evidence
  insertion order, so allocation is deterministic and one record cannot satisfy
  two requirements. Thus separate primary/spouse T4 requirements are isolated,
  a minimum of two needs two evidence identities, participant-A medical evidence
  never satisfies participant B, and `Other` never satisfies `T4Slip` or
  `MedicalExpenseEvidence` (required `Other` uses only `Other`). Every checklist
  question must have an answer with the scope/participant attribution fixed at
  confirmation. `NotReady` carries one SIN-safe `ReadinessReason` per unmet
  requirement/question; missing-evidence detail identifies the redacted
  `RequirementId`, redacted `ParticipantId`, exact category, and remaining
  count, satisfying AC-24 without exposing raw prompt/free text.
  Invalid state cannot be "outstanding" (fail-closed construction means it never
  entered), so requirement 22's last bullet is satisfied structurally.

### Consolidated Class B assumptions

No Class A question remains: A1–A3 are resolved in the PRD. The plan carries
forward these `[ASSUMED]` Class B decisions for one-pass human override:

- module/project names and paths are `TaxCases`, `src/TaxCases/`, and
  `tests/TaxCases.Tests/`;
- the enum/type names and fixed values are exactly those in the Wave 0 contract;
- identifiers and firm ids are opaque, caller-supplied strings; participant
  identities are local to one case;
- evidence categories are `T4Slip`, `MedicalExpenseEvidence`, and `Other`, and
  category inputs must match those names ordinally/case-sensitively;
- evidence requirements have unique opaque ids, target an in-case participant,
  and use a positive minimum; repeated participant/category requirements are
  allowed, but deterministic allocation prevents evidence reuse; required
  `Other` is satisfied only by `Other` evidence;
- evidence is receipt metadata only, with an optional opaque source reference;
- supported case years are exactly `{ 2025 }` and are not inferred from engine
  mapping resources;
- the lifecycle allows the forward path, cancellation from any non-terminal
  state, and `InReview → InformationGathering`; no other backward transition is
  allowed, and `Archived` is absent;
- reconfirming a checklist replaces the prior checklist and clears answers to
  questions no longer present or whose scope/target changed; a case-level
  question accepts only a null answer participant, a participant-level question
  accepts only its exact target, and re-answering replaces its prior answer;
  both actions emit one deterministic change record;
- a missing/blank answer is not an answer and fails closed;
- reopen reasons are opaque non-blank text; correlation/actor references are
  optional opaque caller inputs retained verbatim as domain properties but
  omitted/redacted from diagnostics and supported serialization;
- immutable aggregate operations return the replacement case and exactly one
  change record rather than mutating the original or accumulating events.

### Domain rules from CLAUDE.md applied

- **Per-tax-year isolation** — the case stores one explicit year, validated
  against `SupportedTaxYears`; evidence must match it exactly; no fallback, no
  coercion, no "current year", no clock read.
- **Decimal/half-up currency math** — not applicable: this module contains no
  money field and must stay that way; any amount belongs to the engine or a
  future processor spec.
- **SIN masking** — `RecipientIdentifier` is the only SIN representation;
  every public TaxCases `ToString()` is overridden, raw free-text properties are
  excluded from supported JSON output, and all diagnostic identifiers use
  `SafeDiagnosticText`; no `ValidationError.Message`, readiness reason,
  change-record diagnostic, or public `ToString()` may embed a full SIN (AC-37
  sweeps for any 9-consecutive-digit substring).
- **No hardcoded CRA line numbers** — no line number, box→line logic, or
  mapping-file read exists anywhere in `TaxCases` (AC-40 scans the module's
  source for line-number literals, `DateTime.Now/UtcNow`, and
  `TaxLogicEngine.Mappings` resource names).

## Trade-offs

1. **Immutable aggregate returning outcomes vs. mutable DDD aggregate
   (validate-then-commit).** A conventional mutable aggregate (methods mutate
   `this` on success) was rejected: "rejected operation mutates no state"
   (AC-33) and determinism (AC-35/43) would rest on every future edit
   remembering to validate everything before the first field write. For a type
   whose whole purpose is a fail-closed readiness signal on real tax filings,
   correctness-by-construction beats convention. Cost: allocation per operation
   and a slightly unusual calling style (`outcome.Value.Case` is the new case) —
   acceptable for an in-memory MVP aggregate with no hot path.

2. **New `src/TaxCases/` project vs. homing the types inside `TaxLogicEngine`.**
   Adding a `Cases/` folder to the engine was rejected: the engine's charter
   (its CLAUDE.md) is slip parsing, box→line mapping, rounding — deterministic
   *tax computation*. A case aggregate is a different bounded context
   (engagement workflow), and CLAUDE.md's modular-monolith decision explicitly
   says future modules get `src/<Module>/` homes **when actually built** — this
   spec builds one. Keeping them separate also makes the dependency rule
   (`TaxCases` → `TaxLogicEngine`, never the reverse) a compiler-enforced
   project reference instead of a documented hope.

3. **Operations on the aggregate vs. a separate `TaxCaseService` over an anemic
   data record.** A service-with-DTO design was rejected: it allows a `TaxCase`
   to be constructed in an invalid state (public constructor or init-setters)
   and scatters the invariants across service methods. The private constructor
   plus operations-on-the-aggregate make an invalid `TaxCase` unrepresentable,
   which is exactly what AC-17 ("readiness has no writable setter") and AC-13
   ("no member changes the firm") ask the compiled surface to prove.

4. **Emitting change records per outcome vs. event-sourcing the case.** Full
   event sourcing (state = fold over records) was rejected as over-engineering:
   the PRD explicitly scopes out persistence and delivery of records, and a
   replay/fold model would drag in versioning and upcasting concerns no
   requirement asks for. The outcome-carried record satisfies requirement 29
   with none of that machinery, and an event-sourced persistence layer can still
   be layered on later without changing this contract.

## Risks

- **Fail-open readiness (the flagship risk).** If "no checklist configured"
  were ever treated as "no requirements, therefore Ready", incomplete returns
  would flow to review. Mitigated structurally: `Checklist` is
  `RequirementChecklist?`, `null` means unconfigured, and `ReadinessCalculator`
  short-circuits `null` to `NotReady(ChecklistNotConfigured)` before evaluating
  anything else. AC-23 (negative) and AC-26 (confirmed-empty positive) pin both
  sides; AC-17's reflection test proves no writable bypass exists.

- **Tax-year drift.** The danger is a helpful default ("assume 2025") or an
  adjacent-year fallback creeping in. Mitigated: `TaxYear` is nullable on the
  descriptors so absence is an explicit rejection path, `SupportedTaxYears` is
  the only place a year literal may appear, and AC-4/AC-10/AC-39 are negative
  tests for creation, evidence attachment, and the combined isolation claim.

- **SIN leakage via free text or generated `ToString()`.** C# records auto-print
  every property, and an answer/reason/reference can contain SIN-like text even
  when the participant SIN field is safe. Mitigated in layers: raw SIN becomes
  `RecipientIdentifier` at acceptance; every public type owns a safe override;
  raw free-text properties are excluded from supported JSON; fixed diagnostics
  redact nine-digit runs; and AC-37 tests SINs embedded in participant input,
  answer, prompt, source, correlation, and Reopen reason.

- **Cross-participant readiness leakage.** A case-wide category checklist would
  let one spouse's evidence satisfy the other's requirement. Mitigated by
  participant-scoped `EvidenceRequirement` values, positive minimum counts, and
  deterministic no-reuse allocation. Tests pin separate primary/spouse T4s,
  two-T4 minimums, cross-participant medical isolation, and exact-category
  matching.

- **Hidden non-determinism.** A stray `DateTime.UtcNow`, `Guid.NewGuid()`, or
  environment read would break replay equality and auditability. Mitigated: all
  identifiers and correlation references are caller-supplied by contract, no
  timestamp field exists anywhere, and AC-34/AC-35/AC-43 test it (source scan
  for clock/network/database access plus replay-equality tests).

- **CRA-compliance regression by scope creep.** The nearest future temptation is
  letting the case "peek" at a slip or a mapping to be smarter about readiness.
  Mitigated: evidence is category + identity + attribution only (no amounts, no
  boxes), AC-40 scans the module for CRA line literals and mapping-resource
  reads, and the project reference gives the engine no path back into
  `TaxCases`. Any eligibility rule (spouse/dependant/medical) is contractually
  out of scope until a CRA-verified spec asserts it (decision A1).

- **Parallel-wave merge conflicts on one aggregate.** Six operation families
  live on one class. Mitigated: the partial-class file split is fixed in Wave 0,
  every later task owns exactly one operation file plus its own test files, and
  no two sibling tasks in tasks.md list the same path.

- **Mutable collection aliasing.** `IReadOnlyList<T>` alone does not prevent a
  caller from retaining and mutating the original list. Mitigated by defensive
  array snapshots at every accepted boundary and tests that mutate descriptor
  lists after acceptance and prove case state and readiness are unchanged.

## Migration, verification, and rollback

This is an additive module migration: there is no database, serialized state,
API, or data migration. Wave 0 adds `src/TaxCases/` and
`tests/TaxCases.Tests/`, adds both projects to `TaxAI.sln`, and establishes the
single production dependency `TaxCases → TaxLogicEngine`. The solution and new
project files are the only integration points; `src/TaxLogicEngine/**` and
`tests/TaxLogicEngine.Tests/**` remain unchanged.

Wave 0 also creates `src/TaxCases/CLAUDE.md` and its minimal local `AGENTS.md`
bridge so every implementation wave inherits the module boundary and safety
rules. T09 verifies those instructions without modifying them or claiming
completion. Only T10, after all AC-1 through AC-43 pass, updates `NEXT.md` and
root `CLAUDE.md` project state and then creates `T10.done`. On any T10 failure,
only `verification.md` records the failure; project-state files and the marker
remain untouched.

Every task runs `dotnet test TaxAI.sln` before writing its marker. Final
verification additionally checks the four-project solution, project-reference
direction, all PRD AC-1 through AC-43, forbidden dependency/source scans, no
tracked build output, and a diff proving existing TaxLogicEngine production and
test files are byte-for-byte untouched by spec 006.

Rollback is task/PR-granular because each wave merges before the next. Revert
the failing task's human merge commit; later waves must not start until the
reverted wave is green again. Full feature rollback removes the two new projects
from `TaxAI.sln` and removes only `src/TaxCases/`, `tests/TaxCases.Tests/`, and
spec-006 documentation/status artifacts. No data rollback is needed, and no
TaxLogicEngine behavior, mapping resource, public contract, or test is changed
in either migration or rollback.
