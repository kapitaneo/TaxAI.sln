# Architecture 014 — Client Intake and Document Upload Flow

**Feature class:** `tax-domain`

## Outcome and task topology

Spec 014 adds one real application module, one additive relational intake
model, one development-only encrypted content adapter, and one Blazor Web App
presentation foundation. It reuses `TaxCases` as the sole case/readiness
authority, `Tenancy` as the trusted-firm and immutable-review boundary,
`Persistence` as the only EF Core project, and `TaxAI.Portal` as the only host
and composition root.

The implementation uses four substantial tasks:

1. establish the Blazor Web App foundation, migrate the existing review
   renderer without changing its query/security contract, and freeze the new
   `ClientIntake` public contract required by the repository's Wave 0 rule;
2. implement the secure intake/link/session/questionnaire/upload backend,
   additive SQL metadata, and the development-only encrypted content adapter;
3. add the client and accountant component UI, endpoint authorization,
   rate-limiting, upload transport, progress, and exact review handoff;
4. run a cross-module security/acceptance proof and record completion.

T01 is not a contract-only scaffold. Its independently valuable product result
is the complete Blazor foundation and review-screen migration required by the
human UI decision. The intake signatures are frozen in that same Wave 0 task
with `NotImplementedException` bodies so T02 and T03 compile against one
approved contract. This reconciles the mandatory contract wave with the PRD's
prohibition on a separate contract microtask.

No implementation task is parallel. The boundaries are security-sensitive and
the later task consumes types and files completed by the earlier task.

## Approach

### One application workflow, not a second tax-case model

Create `src/ClientIntake/` as the application/domain coordination module for
this workflow. It owns:

- the closed intake answer, media, document-status, link-state, audit, and
  handoff vocabularies;
- accountant and client commands and safe result projections;
- the workflow services;
- declared persistence, content-store, and credential-source ports;
- reconstruction of the existing immutable `TaxCase` from one exact
  submission snapshot;
- status and unsubmitted-change projection.

It does not own a competing case aggregate or readiness algorithm. At case
creation it calls `TaxCase.Create`, confirms a checklist made from only
`DocumentCategory.T4Slip` and
`DocumentCategory.MedicalExpenseEvidence`, and transitions the returned case to
`InformationGathering`. At submission/read time it reconstructs the same case,
applies only the answer and document versions referenced by the latest
immutable submission through `AnswerQuestion` and `AttachEvidence`, and reads
`TaxCase.Readiness`. `DocumentCategory.Other` never enters the workflow.

Questionnaire version 1 has at most two server-owned case-level questions:
T4 availability and medical-document availability. A question exists only if
that category was requested. The fixed answer codes are `AvailableNow`,
`WillProvideLater`, and `Unavailable`; there is no free-text answer.

### Scope always moves from a trusted server boundary inward

Accountant operations enter through the existing
`PortalAuthorization.FirmScopedPolicy`. The endpoint reads
`PortalAuthorization.BootstrapStateFor(HttpContext)`, creates
`TrustedFirmId` from that server state, and passes it to
`IAccountantIntakeWorkflow`. No accountant request DTO has a firm member.

The client has two deliberately narrow bootstrap exceptions:

1. a canonical raw bootstrap credential is transiently digested and looked up
   by digest;
2. a protected opaque client-session credential is transiently digested and
   looked up by digest.

Each exception returns only a trusted `FirmId`/`CaseId` scope. It does not
return taxpayer content and cannot enumerate. Every subsequent store read or
write takes that returned firm, case, and the digest being revalidated. This
closes the time-of-check/time-of-use seam: a session lookup followed by a case
read is not enough unless the scoped case operation checks that the same
session is still live.

### Append facts; derive current progress

Case initialization, links, sessions, answer versions, uploaded-document
metadata, upload attempts, submissions, submission references, review
association, and audit events are durable. Answer versions, document records,
submission snapshots, association, and audit events are append-only.

Current answers, per-item status, link state, latest submission, unsubmitted
changes, readiness, and handoff state are projections. In particular there is
no writable `Ready` column. Per-item status uses the PRD precedence:

`Ready` → `Uploaded` → `Rejected` → `Missing` → `Requested`.

Readiness uses only the latest immutable submission. A later saved answer or
accepted upload sets `UnsubmittedChanges` and does not rewrite the submission
or independently change readiness.

The current-link projection uses `Revoked` when `RevokedAtUtc` is present,
otherwise `Spent` when `SpentAtUtc` is present, otherwise `Expired` at
`now >= ExpiresAtUtc`, otherwise `Active`. Reissue makes the replacement the
only current link while retaining every prior row and audit event.

### Opaque bytes cross one narrow content boundary

The HTTP layer streams an upload and never uses `IFormFile`,
`ReadFormAsync`, a `MemoryStream`, a browser filename, or a web-root path. It
validates one configured-size-bounded multipart file, the normalized declared
media kind, and PDF/JPEG/PNG signatures. It then streams the validated content
to `IIntakeContentStore`.

The content adapter:

- creates an opaque content key;
- stores outside `wwwroot`;
- encrypts with the exact version-1 chunk envelope below;
- derives a case-specific HMAC key from a required secret and the
  server-derived firm/case;
- computes the whole-file fingerprint while streaming;
- returns only content key, byte count, and fingerprint.

The SQL commit has a unique `(FirmId, CaseId, Fingerprint)` constraint. Two
concurrent identical uploads may both stage ciphertext, but exactly one SQL
commit wins; the loser removes its unreachable staged object. Any metadata
failure similarly removes the staged object. A content failure produces no
accepted metadata. Content deletion is exposed only for this compensating
cleanup, never as a user route.

Spec 014 supplies only a clearly named development/test filesystem adapter.
It accepts synthetic non-SIN fixtures, requires an external encryption key and
root path, and is registered only in Development/test. The existing
non-Development refusal remains. No Azure or production provider is added.
The adapter lives in `TaxAI.Portal` as host-owned Development infrastructure
behind `IIntakeContentStore`; `Persistence` remains the relational metadata
adapter and does not gain ASP.NET Core, environment, or web-root dependencies.

The development envelope uses only .NET cryptographic primitives:

- a distinct required 256-bit content-encryption master key;
- HKDF-SHA256 with a random 128-bit object salt and fixed versioned info that
  includes the canonical trusted firm, case, and opaque content key;
- AES-256-GCM per bounded chunk;
- one random 64-bit nonce prefix plus an unsigned big-endian 32-bit chunk
  counter, with overflow refused, giving a unique 96-bit nonce per chunk;
- a 128-bit authentication tag per chunk;
- authenticated additional data containing the envelope magic/version, chunk
  index, plaintext length, and final-chunk flag;
- an opaque temporary ciphertext path followed by same-directory atomic rename
  after the authenticated final chunk; plaintext is never written to disk.

The duplicate fingerprint uses a different required 256-bit master key,
HKDF-SHA256 scope derivation, and HMAC-SHA256 over the complete plaintext while
it streams. Token digest, fingerprint, and content-encryption keys are distinct
configuration values and purpose strings. Test-only internal helpers verify
round-trip, tamper, truncation, reordering, nonce/counter, and plaintext-absence
properties; no production content-read contract or route is introduced.

## Current and target structure

### Current

- `TaxAI.Portal` is an ASP.NET Core minimal host with no component app.
- `ReviewEndpoints.cs` performs the correct R-1/query/projection checks, then
  assembles HTML and CSS with `StringBuilder`.
- `TaxCases` has the immutable case/checklist/answer/evidence/readiness
  behavior, but no I/O or persistence.
- `Tenancy` owns trusted firm/session and immutable review-record contracts.
- `Persistence` references only `Tenancy` and is the only EF Core project.
- no intake link, client session, upload metadata, source-content store,
  answer/submission history, or intake audit implementation exists.

### Target

```text
TaxAI.Portal ───────────────► ClientIntake ───────► TaxCases
      │                            │                    │
      │                            ├──────────────► Tenancy
      │                            └──────────────► TaxLogicEngine
      │
      ├──────────────────────► Tenancy
      └──────────────────────► Persistence ──────► ClientIntake
                                  ├──────────────► TaxCases
                                  └──────────────► Tenancy
```

- `ClientIntake` references exactly `TaxCases`, `Tenancy`, and
  `TaxLogicEngine`. The last is named directly only for the existing
  `ParseResult<T>`/`ValidationError` result vocabulary used while replaying
  `TaxCase`.
- `Persistence` references exactly `ClientIntake`, `TaxCases`, and `Tenancy`;
  it remains the only EF Core project. The explicit `TaxCases` reference is
  required because the frozen intake-store records use the existing
  `DocumentCategory` and `CaseStatus` types. This is the permitted inward
  infrastructure-to-domain direction; Persistence invokes no `TaxCase`
  behavior and maps no `TaxCase` aggregate.
- `TaxAI.Portal` adds `ClientIntake` to its existing inward reference set. It
  continues to reach review persistence only through Tenancy.
- `TaxCases`, `Tenancy`, `CaseReview`, `CaseProcessing`,
  `DocumentExtraction`, and `TaxLogicEngine` do not reference
  `ClientIntake`, `Persistence`, or the portal.

The migration of the review UI in T01 changes no production file in
`Persistence` or `Tenancy`, no review contract, and no schema artifact. The
later Persistence changes are attributable only to the new intake backend.

## Blazor Web App foundation and screen boundaries

### Registration and render modes

T01 adds the framework-provided Blazor Web App services and endpoint, **for
static server rendering only**:

```csharp
services.AddRazorComponents();

app.MapRazorComponents<App>();
```

`App`, `Routes`, `HeadOutlet`, the shared document/layout component, and local
static assets form one foundation. `App`, `Routes`, and the root layout have no
render-mode directive. Static server rendering is therefore the default. There
is no WebAssembly project/package, global interactive render mode, npm tree,
remote script, remote font, or third-party asset.

**[AMENDED — human decision, before T01 merge]** Interactive Server registration
moved from T01 to **T03**. T01 originally called
`AddInteractiveServerComponents` and `AddInteractiveServerRenderMode` so that
T03's island could later opt in. That was reversed: no T01 component declares an
interactive render mode, so the registration provided no current functionality
while materializing four `/_blazor` SignalR endpoints on the host that serves
taxpayer data. Registering a capability nothing uses is still surface.

T03 therefore adds, in one diff: the first interactive island, the
`AddInteractiveServerComponents(options => options.DetailedErrors = false)` and
`AddInteractiveServerRenderMode(/* WebSocket compression disabled */)`
registrations, and the `blazor.web.js` reference in `App`. Every constraint
below on that island — presentation-only, closed local answer codes, no workflow
or store call, authority only in antiforgery-protected POSTs, disabled
compression, `DetailedErrors` false — is unchanged and now lands with the code it
governs.

After T01, `MapRazorComponents<App>()` contributes exactly one framework
endpoint, `/_framework/opaque-redirect`; no `/_blazor` endpoint exists.

The only `@rendermode InteractiveServer` island is the client questionnaire
panel that reveals the applicable upload controls from closed local answer
codes — **added in T03, along with the Interactive Server registration itself**.
It receives only the client browser allowlist. Its event handlers alter
component-local presentation state; they do not call a workflow or store.
Every save, upload, and submit remains an explicit antiforgery-protected HTTP
POST. Interactive Server WebSocket compression is disabled because the circuit
renders security-sensitive state and no compression side channel is needed.

This follows the framework's static-SSR default and per-component render-mode
model:

- [ASP.NET Core Blazor render modes](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0)
- [ASP.NET Core Blazor security](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/?view=aspnetcore-10.0)

### Endpoint-owned authorization and page models

Sensitive `.razor` components are not repositories, route handlers, or
authorization boundaries. Minimal endpoints:

1. authenticate/revalidate the applicable accountant or client session;
2. resolve trusted firm/case scope;
3. call the workflow;
4. map success to a closed page model;
5. return `RazorComponentResult<TComponent>`;
6. return the fixed refusal before a taxpayer component is instantiated.

This retains minimal endpoint control over exact status/body/header behavior
while replacing manual markup:

- [`RazorComponentResult<TComponent>`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.httpresults.razorcomponentresult-1?view=aspnetcore-10.0)

Components may accept typed parameters and perform contextual Razor encoding,
conditional markup, and local closed-state interaction. They may not inject or
call an intake workflow, repository, content store, authorization resolver,
fingerprint service, audit writer, `TaxCase`, parser, mapper, processor, or
review assembler.

### Existing review-screen migration

`ReviewEndpoints.cs` retains:

- exactly `GET /reviews/{recordId:guid}`;
- `PortalAuthorization.FirmScopedPolicy`;
- private non-cache headers;
- trusted firm extraction from request authorization state;
- exactly one
  `IReviewRecordRepository.FindAsync(TrustedFirmId, Guid,
  CancellationToken)` call;
- the current browser-safety guard;
- the closed DTO projection and invariant amount formatting;
- the identical empty `404` for missing, foreign, and browser-unsafe records.

Only `Render` and its `StringBuilder`/inline-style implementation disappear.
On success the endpoint returns a typed `ReviewPage` component with one
`ReviewPageModel`. The model has exactly the spec 012 fields and no entity,
firm, case, participant, record, document, session, retention, or non-display
timestamp value. The component is static SSR and read-only. CSS moves to local
global/component-scoped assets.

The review route never enters the general component router, never becomes
interactive, and never loads data from a component lifecycle method. A refused
request therefore cannot render a partial taxpayer page.

The review component parameter contract introduced in T01 is exact:

```csharp
namespace TaxAI.Portal.Components.Pages;

public sealed record ReviewPageModel(
    [property: JsonIgnore] int Year,
    [property: JsonIgnore] int Version,
    [property: JsonIgnore] IReadOnlyList<ReviewSlipModel> Slips);

public sealed record ReviewSlipModel(
    [property: JsonIgnore] string Type,
    [property: JsonIgnore] string Taxpayer,
    [property: JsonIgnore] string Employer,
    [property: JsonIgnore] IReadOnlyList<ReviewBoxModel> Boxes,
    [property: JsonIgnore] IReadOnlyList<ReviewLineModel> Lines,
    [property: JsonIgnore] IReadOnlyList<string> UnmappedCodes);

public sealed record ReviewBoxModel(
    [property: JsonIgnore] string Code,
    [property: JsonIgnore] string Amount);

public sealed record ReviewLineModel(
    [property: JsonIgnore] string Code,
    [property: JsonIgnore] string Amount,
    [property: JsonIgnore] IReadOnlyList<string> SourceBoxes);
```

Each constructor copies collections into read-only snapshots, direct JSON
serialization is defensively omitted, and `ToString()` reports structural
counts only. `ReviewPage` has exactly one required `[Parameter] public
ReviewPageModel Model { get; set; } = default!;` property, also marked
`[EditorRequired]`. The implementation-only intake components
added in T03 use the already-frozen `ClientIntake` view records plus primitive
closed parameters; they introduce no new application/module contract.

### Intake routes

Accountant routes all require `FirmScopedPolicy`:

| Method and route | Purpose |
|---|---|
| `GET /intake-cases/new` | typed create form |
| `POST /intake-cases` | create case/checklist/link atomically |
| `GET /intake-cases/{caseId:guid}` | one firm-scoped progress page |
| `POST /intake-cases/{caseId:guid}/link/reissue` | revoke old link/sessions and issue once |
| `POST /intake-cases/{caseId:guid}/link/revoke` | revoke link and derived sessions |
| `POST /intake-cases/{caseId:guid}/review-association` | associate one exact existing record |

The case selector is an opaque accountant-side selector only. No route accepts
a firm.

Client routes contain no firm/case/participant/document identifier:

| Method and route | Purpose |
|---|---|
| `GET /client-intake/link/{credential}` | read-only validation and typed confirmation |
| `POST /client-intake/redeem` | atomic redemption using protected digest proof |
| `GET /client-intake` | clean client page under session cookie |
| `POST /client-intake/answers` | append closed answer versions |
| `POST /client-intake/documents` | stream one neutral-name multipart upload |
| `POST /client-intake/submissions` | append immutable submission |

The confirmation POST action is fixed. Its only workflow value is a
Data-Protection-protected redemption proof derived from the digest, never the
raw credential or scope. Success redirects to `/client-intake` and writes one
`Secure`, `HttpOnly`, scoped `SameSite` protected opaque session cookie.

The implementation-only component parameter surface is also closed:

| Component | Public parameters |
|---|---|
| `IntakeCaseCreatePage` | none |
| `IntakeCaseIssuedPage` | required `Guid CaseId`; required `DateTimeOffset LinkExpiresAtUtc`; required `string ClientUrl` |
| `IntakeCaseProgressPage` | required `Guid CaseId`; required `AccountantIntakeView Model` |
| `ClientLinkConfirmationPage` | required `int TaxYear`; required `string RedemptionHandle` |
| `ClientIntakePage` | required `ClientIntakeView Model` |
| `ClientQuestionnairePanel` | required `ClientIntakeView Model` |
| `ExpectedDocumentChecklist` | required `IReadOnlyList<ExpectedDocumentView> Items` |
| `IntakeStatusBadge` | required `ExpectedDocumentStatus Status` |

Each required parameter has `[Parameter, EditorRequired]` and no catch-all
attribute dictionary. `ClientUrl` is used only on the one-time issuance result;
`RedemptionHandle` is Data-Protection protected; `CaseId` appears only in
accountant form actions. No client component parameter carries an identifier.

### Upload browser transport

Native multipart submission copies the local filename. To satisfy the stronger
PRD rule, a small tracked local JavaScript module intercepts only the upload
form, builds `FormData`, and appends the selected `File` with the literal
neutral transport name `upload.bin`. It sends the antiforgery token in the
configured request header, allowing the server to validate antiforgery without
calling `ReadFormAsync` and buffering the source file.

The server uses `MultipartReader`, discards every received filename before any
model is created, allows exactly the configured file count, and streams the
file. The script receives no firm/case/content key/fingerprint/document
identifier and performs no validation decision. The endpoint remains
authoritative for size, signature, category/ordinal membership, duplicate
status, and commit. The official framework upload guidance likewise requires
an explicit maximum, an application-generated safe name, a non-web-root
location, allowlist validation, and streaming:

- [Upload files in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0)
- [ASP.NET Core Blazor file uploads](https://learn.microsoft.com/en-us/aspnet/core/blazor/file-uploads?view=aspnetcore-10.0)

## Services & public API — exact Wave 0 contract

There is no JSON API in this spec. Public module records are in-process
contracts. Identifier, secret, digest, taxpayer text, content key,
and fingerprint properties are `[JsonIgnore]`; every public
instance has a structural `ToString()` that emits no value. Portal component
models are never serialized. Contract tests assert the exact property and JSON
omission sets.

The public signatures below are frozen in T01. T01's new workflow constructors
and methods throw `NotImplementedException`; T02 replaces those bodies without
changing signatures.

### Closed enums

```csharp
namespace ClientIntake.Models;

public enum AvailabilityAnswerCode
{
    AvailableNow = 0,
    WillProvideLater = 1,
    Unavailable = 2
}

public enum ExpectedDocumentStatus
{
    Requested = 0,
    Uploaded = 1,
    Rejected = 2,
    Missing = 3,
    Ready = 4
}

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

public enum ReviewHandoffState
{
    NotReady = 0,
    AwaitingReviewRecord = 1,
    Available = 2,
    Unavailable = 3
}

public enum IntakeTokenPurpose
{
    BootstrapLink = 0,
    ClientSession = 1
}
```

No new document-category or readiness enum is created.
`TaxCases.Models.DocumentCategory`, `CaseStatus`, and `ReadinessState` are
reused.

### Commands, configuration, result, and view records

```csharp
namespace ClientIntake.Models;

public sealed record CreateIntakeCaseCommand(
    [property: JsonIgnore] string? PrimaryTaxpayerDisplayName,
    int? TaxYear,
    int? ExpectedT4Count,
    int? ExpectedMedicalExpenseCount);

public sealed record IntakeAnswerInput(
    DocumentCategory? Category,
    AvailabilityAnswerCode? Answer);

public sealed record SaveIntakeAnswersCommand(
    IReadOnlyList<IntakeAnswerInput>? Answers);

public sealed record IntakeUploadCommand(
    DocumentCategory? Category,
    int? Ordinal,
    IntakeMediaKind? DeclaredMediaKind,
    long? DeclaredLength);

public sealed record IntakeWorkflowOptions(
    TimeSpan LinkLifetime,
    TimeSpan ClientSessionIdleLifetime,
    long MaximumFileBytes,
    int MaximumFilesPerRequest);

public sealed class IntakeSecret
{
    public static bool TryParse(string? candidate, out IntakeSecret? value);
    [JsonIgnore] public string Reveal();
    public override string ToString();
}

public sealed record IntakeTokenDigest(
    [property: JsonIgnore] IReadOnlyList<byte> Bytes)
{
    public override string ToString();
}

public sealed class IntakeResult<T>
{
    public IntakeResultCode Code { get; }
    [JsonIgnore] public T? Value { get; }
    public bool IsSuccess { get; }
    public static IntakeResult<T> Success(T value);
    public static IntakeResult<T> Failure(IntakeResultCode code);
    public override string ToString();
}

public sealed record CreatedIntakeCase(
    [property: JsonIgnore] Guid CaseId,
    [property: JsonIgnore] IntakeSecret LinkCredential,
    DateTimeOffset LinkExpiresAtUtc);

public sealed record IssuedIntakeLink(
    [property: JsonIgnore] IntakeSecret LinkCredential,
    DateTimeOffset LinkExpiresAtUtc);

public sealed record IntakeRedemptionProof(
    [property: JsonIgnore] IntakeTokenDigest LinkDigest,
    DateTimeOffset LinkExpiresAtUtc);

public sealed record IntakeLinkInspection(
    int TaxYear,
    [property: JsonIgnore] IntakeRedemptionProof Proof);

public sealed record RedeemedIntakeSession(
    [property: JsonIgnore] IntakeSecret SessionCredential,
    DateTimeOffset AbsoluteExpiresAtUtc);

public sealed record IntakeQuestionView(
    DocumentCategory Category,
    [property: JsonIgnore] string Prompt,
    IReadOnlyList<AvailabilityAnswerCode> Options,
    AvailabilityAnswerCode? CurrentAnswer);

public sealed record ExpectedDocumentView(
    DocumentCategory Category,
    int Ordinal,
    ExpectedDocumentStatus Status);

public sealed record ClientIntakeView(
    int TaxYear,
    IReadOnlyList<IntakeQuestionView> Questions,
    IReadOnlyList<ExpectedDocumentView> ExpectedDocuments,
    int? LatestSubmissionVersion,
    bool UnsubmittedChanges);

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
    [property: JsonIgnore] Guid? ReviewRecordId);
```

Every record in this command/result/view block declares
`public override string ToString()`; each implementation emits only its type
identity plus closed counts/enums and never a caller string, identifier, secret,
digest, prompt, or URL.

`IntakeSecret.TryParse` accepts only the canonical unpadded base64url encoding
of exactly 32 bytes and never trims or normalizes. `IntakeTokenDigest` requires
exactly 32 bytes, copies the input, exposes no mutable array, uses
`CryptographicOperations.FixedTimeEquals` for value equality, and never prints
or serializes its bytes.

`PrimaryTaxpayerDisplayName` and question prompts are carried in memory only
and are never serialized directly. The accountant component contextually
encodes the display name. `ReviewRecordId` is used only to build the existing
authorized accountant review URL when the handoff is available.

### Workflow ports and services

```csharp
namespace ClientIntake.Contracts;

public interface IAccountantIntakeWorkflow
{
    Task<IntakeResult<CreatedIntakeCase>> CreateAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        CreateIntakeCaseCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<IntakeResult<AccountantIntakeView>> GetProgressAsync(
        TrustedFirmId firm,
        Guid caseId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<IntakeResult<IssuedIntakeLink>> ReissueLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid accountantActorReference,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<IntakeResult<AccountantIntakeView>> RevokeLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid accountantActorReference,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<IntakeResult<AccountantIntakeView>> AssociateReviewAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid recordId,
        Guid accountantActorReference,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}

public interface IClientIntakeWorkflow
{
    Task<IntakeResult<IntakeLinkInspection>> InspectLinkAsync(
        IntakeSecret credential,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<IntakeResult<RedeemedIntakeSession>> RedeemLinkAsync(
        IntakeRedemptionProof proof,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<IntakeResult<ClientIntakeView>> GetAsync(
        IntakeSecret sessionCredential,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<IntakeResult<ClientIntakeView>> SaveAnswersAsync(
        IntakeSecret sessionCredential,
        SaveIntakeAnswersCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<IntakeResult<ClientIntakeView>> UploadAsync(
        IntakeSecret sessionCredential,
        IntakeUploadCommand command,
        Stream content,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<IntakeResult<ClientIntakeView>> SubmitAsync(
        IntakeSecret sessionCredential,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}

public interface IIntakeCredentialSource
{
    Guid CreateIdentifier();
    IntakeSecret CreateCredential();
    IntakeTokenDigest Digest(
        IntakeSecret credential,
        IntakeTokenPurpose purpose);
}
```

```csharp
namespace ClientIntake.Services;

public sealed class AccountantIntakeWorkflow : IAccountantIntakeWorkflow
{
    public AccountantIntakeWorkflow(
        IIntakeStore store,
        IIntakeCredentialSource secretSource,
        IReviewRecordRepository reviewRecords,
        IntakeWorkflowOptions options);

    // Implements every IAccountantIntakeWorkflow method exactly.
}

public sealed class ClientIntakeWorkflow : IClientIntakeWorkflow
{
    public ClientIntakeWorkflow(
        IIntakeStore store,
        IIntakeContentStore contentStore,
        IIntakeCredentialSource secretSource,
        IntakeWorkflowOptions options);

    // Implements every IClientIntakeWorkflow method exactly.
}
```

The portal supplies the accountant actor reference only from
`BootstrapState.User.UserId` after R-1 and supplies the current UTC instant.
Neither value is bound from the request. The workflows do not read the system
clock, environment, current user, or HTTP context.

### Persistence and content ports

The storage records are closed immutable records in
`ClientIntake.Models.IntakeStorageRecords.cs`. Their exact constructors are:

```csharp
public sealed record IntakeExpectedItemRecord(
    [property: JsonIgnore] Guid ExpectedItemId,
    DocumentCategory Category,
    int Ordinal);

public sealed record IntakeLinkRecord(
    [property: JsonIgnore] Guid LinkId,
    [property: JsonIgnore] IntakeTokenDigest CredentialDigest,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? SpentAtUtc,
    DateTimeOffset? RevokedAtUtc);

public sealed record IntakeAnswerVersionRecord(
    DocumentCategory Category,
    int Version,
    AvailabilityAnswerCode Answer,
    IntakeActorKind ActorKind,
    [property: JsonIgnore] Guid ActorReference,
    DateTimeOffset RecordedAtUtc);

public sealed record IntakeDocumentRecord(
    [property: JsonIgnore] Guid DocumentId,
    [property: JsonIgnore] Guid ExpectedItemId,
    DocumentCategory Category,
    int Version,
    [property: JsonIgnore] string ContentKey,
    IntakeMediaKind MediaKind,
    long ByteCount,
    [property: JsonIgnore] IReadOnlyList<byte> Fingerprint,
    DateTimeOffset UploadedAtUtc);

public sealed record IntakeUploadAttemptRecord(
    [property: JsonIgnore] Guid AttemptId,
    [property: JsonIgnore] Guid ExpectedItemId,
    IntakeUploadOutcome Outcome,
    DateTimeOffset AttemptedAtUtc);

public sealed record IntakeSubmissionAnswerReference(
    DocumentCategory Category,
    int AnswerVersion);

public sealed record IntakeSubmissionDocumentReference(
    [property: JsonIgnore] Guid DocumentId);

public sealed record IntakeSubmissionRecord(
    int Version,
    IReadOnlyList<IntakeSubmissionAnswerReference> Answers,
    IReadOnlyList<IntakeSubmissionDocumentReference> Documents,
    IntakeActorKind ActorKind,
    [property: JsonIgnore] Guid ActorReference,
    DateTimeOffset SubmittedAtUtc);

public sealed record IntakeReviewAssociationRecord(
    [property: JsonIgnore] Guid RecordId,
    DateTimeOffset AssociatedAtUtc);

public sealed record IntakeAuditEventRecord(
    [property: JsonIgnore] Guid EventId,
    long Sequence,
    IntakeAuditKind Kind,
    IntakeActorKind ActorKind,
    [property: JsonIgnore] Guid ActorReference,
    [property: JsonIgnore] Guid? ExpectedItemId,
    DateTimeOffset OccurredAtUtc);

public sealed record StoredIntakeCase(
    [property: JsonIgnore] TrustedFirmId Firm,
    [property: JsonIgnore] Guid CaseId,
    [property: JsonIgnore] Guid PrimaryParticipantId,
    [property: JsonIgnore] string PrimaryTaxpayerDisplayName,
    int TaxYear,
    CaseStatus CaseStatus,
    long ConcurrencyVersion,
    IReadOnlyList<IntakeExpectedItemRecord> ExpectedItems,
    IntakeLinkRecord CurrentLink,
    IReadOnlyList<IntakeAnswerVersionRecord> AnswerVersions,
    IReadOnlyList<IntakeDocumentRecord> Documents,
    IReadOnlyList<IntakeUploadAttemptRecord> UploadAttempts,
    IReadOnlyList<IntakeSubmissionRecord> Submissions,
    IntakeReviewAssociationRecord? ReviewAssociation);

public sealed record NewIntakeCase(
    StoredIntakeCase Case,
    IReadOnlyList<IntakeAuditEventRecord> AuditEvents);

public sealed record IntakeLinkScope(
    [property: JsonIgnore] TrustedFirmId Firm,
    [property: JsonIgnore] Guid CaseId,
    [property: JsonIgnore] IntakeTokenDigest LinkDigest,
    DateTimeOffset ExpiresAtUtc);

public sealed record IntakeClientScope(
    [property: JsonIgnore] TrustedFirmId Firm,
    [property: JsonIgnore] Guid CaseId,
    [property: JsonIgnore] Guid SessionId,
    [property: JsonIgnore] IntakeTokenDigest SessionDigest,
    DateTimeOffset AbsoluteExpiresAtUtc,
    DateTimeOffset IdleExpiresAtUtc,
    DateTimeOffset EvaluationAtUtc);

public sealed record NewIntakeSession(
    [property: JsonIgnore] Guid SessionId,
    [property: JsonIgnore] IntakeTokenDigest SessionDigest,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset AbsoluteExpiresAtUtc,
    DateTimeOffset IdleExpiresAtUtc);

public sealed record StagedIntakeContent(
    [property: JsonIgnore] string ContentKey,
    long ByteCount,
    [property: JsonIgnore] IReadOnlyList<byte> Fingerprint);
```

Every collection constructor copies into a read-only snapshot, and every type
declares `public override string ToString()` with structural safe output.

```csharp
namespace ClientIntake.Contracts;

public interface IIntakeStore
{
    Task<IntakeStoreWriteCode> CreateAsync(
        NewIntakeCase intakeCase,
        CancellationToken cancellationToken);

    Task<IntakeLinkScope?> FindLinkScopeAsync(
        IntakeTokenDigest linkDigest,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<IntakeStoreWriteCode> RedeemLinkAsync(
        IntakeLinkScope linkScope,
        NewIntakeSession session,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    Task<IntakeClientScope?> FindClientScopeAsync(
        IntakeTokenDigest sessionDigest,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<StoredIntakeCase?> FindAsync(
        TrustedFirmId firm,
        Guid caseId,
        CancellationToken cancellationToken);

    Task<StoredIntakeCase?> FindForClientAsync(
        IntakeClientScope scope,
        CancellationToken cancellationToken);

    Task<IntakeStoreWriteCode> ReissueLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        long expectedConcurrencyVersion,
        IntakeLinkRecord replacement,
        IReadOnlyList<IntakeAuditEventRecord> auditEvents,
        CancellationToken cancellationToken);

    Task<IntakeStoreWriteCode> RevokeLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        long expectedConcurrencyVersion,
        DateTimeOffset revokedAtUtc,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    Task<IntakeStoreWriteCode> AppendAnswersAsync(
        IntakeClientScope scope,
        long expectedConcurrencyVersion,
        DateTimeOffset renewedIdleExpiresAtUtc,
        IReadOnlyList<IntakeAnswerVersionRecord> answers,
        IReadOnlyList<IntakeAuditEventRecord> auditEvents,
        CancellationToken cancellationToken);

    Task<IntakeStoreWriteCode> AppendUploadAttemptAsync(
        IntakeClientScope scope,
        long expectedConcurrencyVersion,
        DateTimeOffset renewedIdleExpiresAtUtc,
        IntakeUploadAttemptRecord attempt,
        IntakeDocumentRecord? document,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    Task<IntakeStoreWriteCode> AppendSubmissionAsync(
        IntakeClientScope scope,
        long expectedConcurrencyVersion,
        DateTimeOffset renewedIdleExpiresAtUtc,
        IntakeSubmissionRecord submission,
        IReadOnlyList<IntakeAuditEventRecord> auditEvents,
        CancellationToken cancellationToken);

    Task<IntakeStoreWriteCode> AssociateReviewAsync(
        TrustedFirmId firm,
        Guid caseId,
        long expectedConcurrencyVersion,
        IntakeReviewAssociationRecord association,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken);
}

public interface IIntakeContentStore
{
    Task<StagedIntakeContent> StageAsync(
        TrustedFirmId firm,
        Guid caseId,
        Stream content,
        long maximumBytes,
        CancellationToken cancellationToken);

    Task DeleteIfExistsAsync(
        TrustedFirmId firm,
        Guid caseId,
        string contentKey,
        CancellationToken cancellationToken);
}
```

`FindLinkScopeAsync` and `FindClientScopeAsync` are the only firm-omitting
members. `FindForClientAsync` and every client write require the scope returned
by the latter and revalidate its live session condition in the same database
operation. A successful client mutation advances the supplied idle boundary to
`min(now + configured idle lifetime, absolute expiry)`; GET does not touch it.
There is no generic repository, queryable, update/delete of
immutable history, unscoped case lookup, raw credential store, or raw-content
read member.

## Data model — relational intake metadata

One additive migration, planned as
`20260730000000_AddClientIntake`, creates only new intake tables:

| Table | Purpose and important constraints |
|---|---|
| `IntakeCases` | PK `(FirmId, CaseId)`; participant, 2025, approved display name, lifecycle, questionnaire version, concurrency token |
| `IntakeExpectedItems` | PK includes `(FirmId, CaseId)`; unique `(FirmId, CaseId, Category, Ordinal)`; only T4/medical check constraint |
| `IntakeLinks` | tenant/case FK; digest only; one filtered active link per case; exempt unique digest lookup index |
| `IntakeClientSessions` | tenant/case/link FK; digest only; idle/absolute/revocation; exempt unique digest lookup index |
| `IntakeAnswerVersions` | append-only; unique `(FirmId, CaseId, Category, Version)`; closed answer/category checks |
| `IntakeDocuments` | append-only metadata; unique `(FirmId, CaseId, Fingerprint)` and `(FirmId, CaseId, ExpectedItemId, Version)` |
| `IntakeUploadAttempts` | append-only structural accepted/rejected/duplicate fact; no filename/content |
| `IntakeSubmissions` | append-only unique `(FirmId, CaseId, Version)` |
| `IntakeSubmissionAnswers` | immutable exact answer-version references under the same firm/case |
| `IntakeSubmissionDocuments` | immutable exact document references under the same firm/case |
| `IntakeReviewAssociations` | one immutable association per `(FirmId, CaseId)`; unique `(FirmId, RecordId)` |
| `IntakeAuditEvents` | append-only sequence per firm/case; closed kind/actor and safe opaque references only |

Every tenant-owned PK, FK, lookup, and uniqueness constraint leads with
`FirmId` and then `CaseId`, except the two explicitly approved digest lookup
indexes. Those indexes contain no taxpayer value and return scope rather than
accepting it. Required relationships use composite keys so a foreign case,
item, answer, document, submission, or association cannot be adopted by an
unscoped child FK.

The migration and snapshot do not alter the existing review, firm, user,
membership, or authentication-session tables. No SIN column, partial value,
presence flag, filename, raw credential, cookie, document content, amount,
prompt, content snippet, or free text exists in the new schema.

## Operation flows

### Create and issue

1. Endpoint obtains R-1 trusted firm and validates fixed form fields and
   antiforgery.
2. Workflow validates display name with `TaxpayerTextGuard`, the existing
   maximum, and the stronger compact/hyphenated/space/masked SIN-shape guard.
3. It creates opaque case/participant/item/link/audit IDs and one 256-bit
   credential, then stores only its keyed digest.
4. It creates and validates the existing 2025 `TaxCase`, confirms the explicit
   checklist, and transitions to `InformationGathering`.
5. `IIntakeStore.CreateAsync` commits case, items, link, and the case-created,
   checklist-confirmed, and link-issued audit events in one transaction.
6. Only after success does the endpoint combine the fixed link prefix and raw
   credential with a required, validated absolute HTTPS public-origin
   configuration. It never trusts `Host`, forwarded headers, or request input
   to create the link. It renders that URL once in a non-cacheable component;
   the URL is never recoverable from progress.

### Link inspection and redemption

1. Rate limiting runs before token parsing/store access.
2. GET strictly parses the canonical high-entropy credential, digests it,
   calls `FindLinkScopeAsync`, and returns either the common empty `404` or a
   typed confirmation with a protected digest proof.
3. POST validates antiforgery, unprotects the proof, and calls
   `RedeemLinkAsync`.
4. The SQL transaction conditionally changes one still-active, unspent,
   unexpired link to spent, creates one digest-only session bounded by the link,
   and appends the audit event.
5. A losing replay returns the same `NotFound` result. Success writes the
   protected session cookie and redirects to the clean route.

Reissue atomically revokes every old active link and all derived sessions
before inserting the replacement. Revoke performs the same invalidation
without replacement. Neither operation deletes history.

### Answers and submission

Each client operation performs rate-limit and antiforgery checks, resolves the
session digest, then revalidates that exact session during its scoped case
read/write. Answer saving rejects null, duplicate category entries, unrequested
categories, `Other`, undefined values, and stale concurrency; changed answers
append the next category version and audit event.

Submission resolves current versions on the server, creates the next version
under a unique constraint, and stores exact answer/document references. It
selects at most the latest accepted document version for each distinct expected
item; one slot can never contribute two evidence records, and superseded
versions remain immutable. It replays only those references through `TaxCase`,
derives readiness, and appends a readiness audit event only if the derived
state differs from the prior submission. A concurrent next-version winner
leaves the loser with no partial snapshot.

### Upload and duplicate handling

The portal binds category and ordinal only as untrusted presentation values;
the workflow resolves the server-owned expected item in the session-derived
case. It rejects an unrequested or foreign item before content storage.

For an otherwise valid signature/media/size stream:

1. stage encrypted content and compute the scoped keyed fingerprint;
2. create the next immutable document version for that item;
3. atomically append attempt, document metadata, and audit under the live
   session and concurrency guard;
4. on duplicate/conflict/failure, delete the staged object;
5. on duplicate, append only the scoped fixed duplicate attempt/audit if the
   live-case commit can do so without exposing another scope.

The document becomes `ReceivedEvidence` only when a submission references it.
No upload invokes extraction, parsing, mapping, review assembly, or save of a
review record.

### Progress and review handoff

Accountant progress loads only
`IIntakeStore.FindAsync(trustedFirm, caseId)`. `null` becomes the same empty
`404` for missing and foreign. The application projects display name, fixed
counts, statuses, questionnaire count, link state, submission version,
unsubmitted changes, readiness, safe instants, and handoff state.

Association first loads the exact candidate through
`IReviewRecordRepository.FindAsync(trustedFirm, recordId)`. It requires exact
canonical equality between the persisted review `CaseId` and the intake case
ID, then appends one immutable association. It never calls `SaveAsync` and
rejects replacement.

On progress, a handoff URL is emitted only when:

- latest-submission `TaxCase.Readiness` is `Ready`;
- an immutable association exists; and
- the exact associated review record can still be found under the same trusted
  firm and still names the same case.

Ready with no record renders `AwaitingReviewRecord`; an unavailable association
renders one non-oracular unavailable state. The link targets the unchanged
`/reviews/{recordId:guid}` route.

## HTTP security and safe refusal

- Link/session/intake responses use `Cache-Control: no-store`, `Pragma:
  no-cache`, `Referrer-Policy: no-referrer`, `X-Content-Type-Options: nosniff`,
  a restrictive local-only CSP, and no third-party request.
- Request/body/form logging remains disabled. `IntakeSecret`,
  `IntakeTokenDigest`, rate partition keys, content keys, and fingerprints have
  fixed redacted `ToString()` output. The current Critical logging floor is not
  lowered.
- Public link, client-session, answer, upload, and submission policies use
  required positive finite permit/window/queue/concurrency settings.
  Partition objects contain only a keyed digest plus a network partition and
  have safe structural rendering.
- Rate limiting precedes existence checks. The same fixed `429` writer and
  headers handle valid and invalid resources.
- The same empty `404` writer handles malformed, unknown, expired, revoked,
  spent, replayed, foreign, missing, and inconsistent client resources.
- Missing and foreign accountant cases are byte-identical empty `404`.
  Authentication/R-1 failures retain existing `401`/`403`.
- Every state change is non-GET and antiforgery-protected. GET/HEAD never
  updates session activity or application state.
- Configuration has no compiled fallback. Missing/invalid lifetimes, byte/file
  limits, rate/concurrency limits, token-digest key, fingerprint key, content
  encryption key, development content path, or absolute HTTPS public origin
  fails startup. The configured origin has no user info, path other than `/`,
  query, or fragment.
- Interactive circuit state is never authoritative. The circuit cannot invoke
  a workflow; protected form POSTs always re-enter endpoint authorization.
  Revocation therefore takes effect on the next operation even if the local
  circuit remains connected.

## PII and browser allowlists

The only raw-byte exception is the encrypted source object. It may contain SIN
and must never be described as SIN-free.

Every structured surface—SQL rows, content metadata, DTOs, component state,
HTML, URLs other than the raw opaque bootstrap credential, cookies, logs,
metrics, traces, errors, filenames, audit records, diagnostics, fixtures—has no
SIN in any form. There is no SIN-named field. Malicious SIN-shaped display text
is rejected before `TaxCase.Create`, persistence, audit, or rendering and is
never echoed or masked.

Client component state contains only:

- TaxAI brand;
- 2025;
- server-owned prompts and closed options;
- T4/medical labels and ordinals;
- expected-item statuses;
- upload controls and fixed messages;
- framework antiforgery state.

The confirmation page additionally contains only the protected redemption
proof. No client model contains display name, firm, tenant, case, participant,
document, user, membership, session, content, fingerprint, review, or audit
identifier.

The accountant progress model is the exact FR-38 allowlist. The one-time
issuance response additionally carries the raw URL once. The review component
retains the exact spec 012 allowlist.

## Validation and tests

Each implementation task ships its own unit, boundary, relational, component,
and HTTP tests. T04 adds only the genuinely cross-module proof that cannot be
owned by one earlier boundary.

Required evidence includes:

- exact public contract and JSON omission tests;
- `TaxCase` reuse and readiness replay tests;
- display-name and 2025-only fail-closed tests;
- link entropy/digest/expiry/atomic replay/reissue/revocation tests;
- session idle/absolute/revocation/store-failure tests;
- immutable answers, submissions, audit, and association tests;
- relational composite-key, uniqueness, tenant-isolation, migration, pending
  model, and no-SIN schema tests;
- streaming signature/size/count/filename/content-failure/cleanup tests;
- case-scoped duplicate and concurrent duplicate tests;
- all status precedence and unsubmitted-change projections;
- R-1, server-derived firm, client scope, antiforgery, method, rate-limit, safe
  `404`, fixed `429`, cache/security-header, and no-log tests;
- component parameter/property allowlists, contextual encoding, render-mode
  inventory, no manual HTML assembly, local asset, no third-party request, and
  presentation-only source boundaries;
- review query/DTO/order/amount/empty-state/non-cache/404/session-revocation
  regression;
- exact review handoff without `SaveAsync`, extraction, processing, or review
  construction;
- full specs 001–013 regression.

Every task ends with:

```powershell
dotnet build TaxAI.sln
dotnet test TaxAI.sln
pwsh ./scripts/Invoke-SolutionTests.ps1
```

The PowerShell command is the authoritative suite gate. New tests raise
manifest floors; floors are never lowered.

## Migration sequence and compatibility

1. T01 introduces the contract project/test project and the static-SSR-only
   Blazor infrastructure, migrates review rendering, and proves zero
   Persistence/Tenancy/schema drift.
2. T02 implements the contract, adds the ClientIntake reference to Persistence,
   adds the one intake-only migration, and wires backend services without
   exposing routes.
3. T03 adds the portal reference/routes/components/assets, the Interactive
   Server registration and client script, and exercises the complete UI.
4. T04 runs the cross-module proof and updates active repository guidance.

Existing namespaces, assemblies, `IReviewRecordRepository`,
`PersistedReviewRecord`, migration history, review route, health route, shell
route, identity/session behavior, and review DTO semantics remain compatible.
No existing migration is edited or regenerated.

## Trade-offs

### Endpoint-owned component results vs data-loading routable components

Routable components that inject repositories or authorization services were
rejected. They blur the point at which R-1/safe-404 decisions occur and make
Interactive Server circuit state easy to mistake for request authority.
Endpoint-owned `RazorComponentResult` keeps the existing security flow and
passes only a closed model to presentation.

### A real `ClientIntake` module vs expanding Tenancy or TaxCases

Putting workflow I/O in `TaxCases` was rejected because that module is
deterministic and provider-independent. Putting case/question/upload behavior
in `Tenancy` was rejected because Tenancy owns trusted scope and persisted
review contracts, not intake orchestration. A real application module can
reuse both without reversing dependencies.

### Normalized immutable rows vs a JSON event payload

A generic JSON event stream would reduce table count, but it would move
schema/foreign-scope enforcement into deserialization and make exact answer and
document references harder to constrain. Normalized append-only rows provide
firm/case-leading keys and database-enforced referential integrity. The extra
tables are justified by immutable history and replay.

### Separate encrypted content vs SQL BLOBs

SQL BLOBs were rejected because they would put SIN-bearing opaque source
content inside the intentionally SIN-free relational model, increase accidental
query/diagnostic exposure, and mix content retention with review metadata.
The separate port keeps bytes inaccessible to EF and exposes no read URL.

### Narrow Interactive Server island vs global interactivity

Global Interactive Server would make read-only review/progress routes circuit
surfaces and widen authorization/revocation testing. Pure static SSR would be
sufficient for authoritative form posts but less usable for immediate adaptive
reveal. One presentation-only client island provides that local behavior while
all authority remains in explicit server POSTs.

### Browser-neutralized multipart vs trusting server disposal alone

Server-only filename disposal would still transmit the original filename and
violate the PRD. The local upload module supplies a fixed filename and header
antiforgery, while the server independently discards any received filename and
validates all content. This is a small necessary browser transport helper, not
a client business layer.

## Risks

| Risk | Mitigation |
|---|---|
| a caller-controlled firm/case/item crosses a tenant | trusted firm type, closed digest lookups, composite scoped keys, session revalidation in each read/write, foreign=missing tests |
| link GET is consumed by a prefetcher | GET is read-only; protected proof + antiforgery POST performs atomic spend |
| raw token leaks in logs/referrers | Critical logging floor, no request logging, no-referrer headers, safe token types, clean redirect, no audit of raw/digest |
| replay or concurrent next version | conditional SQL updates, concurrency token, unique link/session/submission/fingerprint constraints |
| document bytes or filenames reach SQL/temp/web root | manual streaming multipart parser, header antiforgery, neutral client filename, outside-root encrypted staging, no `IFormFile` |
| custom content envelope is implemented incorrectly | versioned encrypt-then-authenticate chunk format, independent round-trip/tamper/truncation tests, Development-only registration, no production claim |
| duplicate detection becomes a cross-case oracle | scope-derived HMAC fingerprint plus firm/case unique key; other-scope content has identical external behavior |
| readiness drifts from `TaxCases` | reconstruct and call existing operations/calculator; no stored ready Boolean or second calculator |
| component/circuit becomes authorization state | presentation-only dependency scan, endpoint-owned operations, explicit POST + antiforgery + per-operation session validation |
| Interactive Server compression leaks state | no free text/identifiers in circuit model and WebSocket compression disabled |
| UI migration changes review semantics | same endpoint/query/projection code, component-only renderer change, exact spec 012 regression suite, T01 storage-neutral diff check |
| a display name smuggles a SIN | existing length/digit-run guard plus fixed compact/hyphen/space/masked-shape rejection before construction; no echo |
| retention is silently invented | no intake/blob purge or retention field; non-Development refusal; later human-approved retention/deployment spec required |
| tax semantics enter upload/intake | only fixed categories/availability; no content read, amount, CRA line, parser, mapper, calculation, extraction, or review creation |

## Rollback and recovery

T01 is code/assets only and can be reverted without data work; the old review
renderer can be restored from version control if the component migration fails
before merge.

T02's migration is additive and is never applied automatically by the
application. Before any environment has intake data, its generated down
migration can remove only the new intake tables. Once intake metadata or
content exists, destructive rollback is forbidden: disable the new routes,
retain encrypted content and immutable history, and use a forward fix after a
human data decision. Existing review/identity tables require no rollback.

No production deployment is part of this spec, so there is no Azure resource,
slot, or production database rollback.

## Tax behavior unchanged

Spec 014 changes no parser, mapping JSON, mapper, calculation, rounding,
review-package contract, CRA line/box behavior, or supported-tax-year list.
It uses the already-approved 2025 `TaxCase` behavior and treats uploaded T4 and
medical files as opaque evidence. OCR, LLM extraction, tax calculation, T1
generation, NETFILE/EFILE, Azure provisioning, and production deployment remain
absent.
