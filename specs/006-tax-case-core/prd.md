# PRD 006 — Tax Case Core

## Feature classification

**Class: `tax-domain`.**

Tax Case Core is the domain container for a Canadian tax-filing engagement. It
introduces taxpayer-domain concepts (a primary taxpayer, an optional spouse or
common-law partner, dependants), it is scoped to an explicit **tax year**, it
governs which **tax evidence** (T4 employment slips, medical-expense evidence)
belongs to which participant, and it derives a **readiness-for-review** signal
from that taxpayer-domain state. It also carries **PII (participant SINs)** and a
**firm-isolation** boundary.

Even though much of the container is structural, it touches tax-year rules,
taxpayer-domain validation, and taxpayer PII, and it mixes structural work with
tax semantics. Per the classification rule ("if a feature mixes both, it is
`tax-domain`; when unsure, it is `tax-domain`"), this feature is `tax-domain` and
every tax-domain protection below applies as written: explicit tax-year scope,
fail-closed behaviour, SIN masking, one negative acceptance criterion per
fail-closed rule, a tax-year-isolation criterion, and the "no hardcoded CRA line
number in C#" criterion.

This PRD deliberately introduces **no new CRA box, line, rate, threshold, limit,
deadline, or eligibility rule** (see `## CRA sources`). Every genuine tax
determination — who *qualifies* as a common-law partner or an eligible
dependant, which medical expenses are *eligible*, and any box→line mapping — is
pushed out of scope to the already-verified `TaxLogicEngine` and to future specs.
Three questions that would otherwise force an unverifiable CRA claim are raised
as **blocking Class A questions** rather than assumed.

---

## Problem

TaxAI has a deterministic tax engine (`TaxLogicEngine`) that parses T4/T5 slips
and maps boxes to CRA lines, but it has **no unit of work** that an accountant
operates on. There is nothing that says "this bundle of taxpayers, evidence, and
open questions is one engagement, owned by one firm, for one tax year, and here
is whether it is complete enough for a human to review." Without that unit:

- there is no place to attach a taxpayer, their spouse/common-law partner, or
  dependants and keep each participant's data separate;
- there is no honest, *derived* signal for "ready for accountant review" — the
  risk being a writable "ready" flag someone flips while documents are still
  missing, sending an incomplete return to review;
- there is no enforced boundary preventing one firm's engagement from
  referencing another firm's data.

Tax Case Core is that unit: a deterministic, provider-independent aggregate that
owns participants, received evidence categories, missing-document and
unanswered-intake state, lifecycle status, and a readiness result computed from
that state. It is the foundation the later **Document Extraction Contract**, **T4
Case Processor**, and **Accountant Review Package** specs build on (see
`NEXT.md`).

**Tax year in scope: 2025 only.** A tax case is bound to exactly one tax year.
This MVP supports **2025**, the only tax year for which `TaxLogicEngine` has a
mapping config today (`TaxLogicEngine.Mappings.T4LineMap.2025.json`). Per
CLAUDE.md, tax years are never mixed and there is no "latest"/"current" year: a
case whose tax year is not a supported year **fails closed** — it is never
coerced to 2025 and never falls back to an adjacent year. Evidence attached to a
case must carry the **same** tax year as the case. Expanding beyond 2025 is out
of scope (a new supported year is a new config plus a scope change, not a
silent widening).

---

## Current state and reuse analysis

Grounded in the actual tree at `D:\Projects\TaxAI` and `specs/001`–`005`, not
memory. No Tax Case Core code exists today: a grep for `TaxCase`,
`CaseParticipant`, `Dependant`, `Readiness`, `IntakeQuestion` matches only
CLAUDE.md and spec 005 (which *names* "tax cases" as a documented-but-unscaffolded
future module). There is no `src/TaxCases*` project.

### Reused unchanged (do NOT re-specify or duplicate)

- **`RecipientIdentifier`** (`src/TaxLogicEngine/Models/RecipientIdentifier.cs`)
  — the SIN value object. Holds the raw value privately, exposes only `Masked`
  (last 3 digits; a 9-digit SIN renders `***-**-789`), overrides `ToString()` to
  the masked form, and never emits the full value. **This is the only permitted
  representation of a participant SIN in Tax Case Core.** No new SIN/identifier
  type is created.
- **`ParseResult<T>`** (`Models/ParseResult.cs`) — the fail-closed result:
  `Success(value)` carries a value and no errors; `Failure(errors)` carries ≥1
  error and no value; there is no partial state. Every Tax Case Core operation
  that can be rejected (create case, add participant, attach evidence, answer an
  intake question, transition status) returns a `ParseResult<T>`.
- **`ValidationError`** (`Models/ValidationError.cs`) — `{ string Field; string
  Message; }`. Producers keep messages SIN-safe. Reused verbatim for every
  rejection reason.

### Not duplicated, and explicitly out of this feature

- **`T4Slip`, `T4SlipParser`, `T4LineMapper`, `MoneyRounding`,** the T4/T5
  mapping configs and every other engine type — Tax Case Core does **not**
  re-implement parsing, mapping, rounding, or calculation. It records that a T4
  slip was *received* (an evidence category attributed to a participant and a
  tax year); it does not parse it or map it. Parsing/processing a received slip
  is the future **T4 Case Processor** spec, which invokes the engine. Tax Case
  Core carries no CRA line number and no box→line logic.

### Genuinely new (introduced by this feature)

A new domain aggregate and its supporting value types: a tax case, its
participants and their relationship kind, received-evidence records and their
document categories, intake questions and their answered/unanswered state, a
lifecycle status, and a derived readiness result — plus the case service/aggregate
that enforces the invariants below. Concrete type names, the module/project home,
and file layout are Class B `[ASSUMED]` (see Open questions); the module must
obey CLAUDE.md's dependency rules (it may depend inward on `TaxLogicEngine`
domain types; it must **not** depend on ASP.NET Core, EF Core, SQL, OCR/LLM/cloud
SDKs, or any I/O that would make its result non-deterministic).

---

## User stories

1. As an **accountant**, I want to open one tax case for one client and one tax
   year, owned by my firm, so that all of that engagement's people, evidence,
   and open questions live in one place.
2. As an **accountant**, I want to add the client as the primary taxpayer and,
   when relevant, one spouse/common-law partner and any dependants, so that a
   coupled or family engagement is one case with clearly separated participants.
3. As an **accountant**, I want to record which evidence I have received (a T4
   slip, medical-expense evidence) against the right participant and tax year, so
   that I can see what has arrived without the case parsing or judging it.
4. As an **accountant**, I want the case to tell me which required documents are
   still missing and which intake questions are still unanswered, so that I know
   what to chase.
5. As an **accountant**, I want a **derived** "ready for review" signal that I
   cannot simply switch on, so that a case never reports ready while it is
   demonstrably incomplete.
6. As a **firm administrator**, I want a case and everything in it to belong to
   exactly one firm, with cross-firm references rejected, so that one firm can
   never read or attach another firm's data.
7. As a **compliance reviewer**, I want participant SINs masked to the last 3
   digits everywhere — output, logs, errors, serialized form — so that the case
   cannot leak a SIN (CLAUDE.md).
8. As an **engine maintainer**, I want the case to be deterministic and
   provider-independent (no DB, no OCR, no network), so that the same inputs
   always produce the same case state and readiness result.

---

## Functional requirements

Requirements that depend on a **blocking Class A question** are marked
`[pending A1/A2/A3]` and are written to the recommended default so the PRD is
coherent; they are not final until a human answers the matching question (see
Open questions). `[design]` marks a decision that depends on no CRA fact.
`[reuse]` marks reuse of an existing type.

### Case identity, firm ownership, and tax year

1. **[design]** A tax case SHALL be created with a non-empty **firm identifier**,
   a **tax year**, and an initial **primary taxpayer** participant. A missing or
   blank firm identifier SHALL fail closed with a structured error.
2. **[design]** A case SHALL belong to **exactly one firm**. The owning firm
   SHALL be immutable after creation; any attempt to change/transfer case
   ownership to another firm SHALL fail closed.
3. **[design]** Every participant, evidence record, intake question, and any
   reference attached to a case SHALL carry (or inherit) the **same firm
   identifier** as the case. Attaching anything owned by a different firm SHALL
   fail closed with a structured error (cross-firm reference rejected).
4. **[design/verified-scope]** A case's tax year SHALL be a **supported year
   (2025)**. A case whose tax year is absent or is any other value SHALL fail
   closed — never coerced to 2025, never an adjacent-year fallback (CLAUDE.md).
   A case represents exactly one tax year and SHALL NOT mix years.

### Participants and separation

5. **[design]** A case SHALL have **exactly one** participant with relationship
   kind **PrimaryTaxpayer**. Zero primary taxpayers, or more than one, SHALL fail
   closed.
6. **[design]** A case SHALL have **at most one** participant with relationship
   kind **SpouseOrCommonLaw**. Adding a second SHALL fail closed.
7. **[pending A1]** A case MAY have zero or more participants with relationship
   kind **Dependant**. A dependant is modelled **structurally** — a declared
   relationship on the case — and Tax Case Core SHALL encode **no CRA eligibility
   or definition test** for dependant status, common-law status, or spousal
   status. Any eligibility determination is deferred (see Out of scope, A1).
8. **[design]** Each participant SHALL have a stable within-case identity.
   Adding the **same participant twice** (same within-case identity) SHALL fail
   closed with a structured error.
9. **[design]** Every evidence record and every intake answer SHALL be
   attributed to a specific participant that belongs to the case. Attributing
   evidence or an answer to a participant **not in the case** SHALL fail closed.
   No participant's evidence or answers SHALL be readable as another
   participant's (participant data separation).

### PII (participant SIN)

10. **[pending A2]** A participant SIN SHALL be **optional** at creation (it may
    be unknown at intake). When present it SHALL be wrapped in the reused
    `RecipientIdentifier` and SHALL NEVER be emitted in full by any `ToString()`,
    log line, exception/validation message, readiness reason, or serialized form
    — only the last-3-digits masked form. A missing SIN SHALL NOT block case
    creation.
11. **[reuse]** No `ValidationError.Message` and no readiness reason SHALL
    contain a full SIN. Tax Case Core SHALL introduce no alternative,
    unmasked SIN representation.

### Received evidence and document categories

12. **[design]** The case SHALL record **received evidence** as a document
    **category** attributed to a participant and a tax year. The MVP supported
    categories SHALL include **T4 employment slip** and **medical-expense
    evidence** (plus a generic catch-all `[ASSUMED]`). An **unknown/unsupported
    category** SHALL fail closed with a structured error naming the category.
13. **[design]** Tax Case Core SHALL treat received evidence as an **untrusted,
    unparsed record of receipt** — a category and attribution only. It SHALL NOT
    parse a slip, extract box values, compute eligibility, or apply any CRA
    mapping (that is the engine / future processor). No CRA line number, box→line
    mapping, or tax calculation SHALL appear in Tax Case Core.
14. **[design]** An evidence record's tax year SHALL match the case's tax year.
    Evidence for a **different tax year** than the case SHALL fail closed (tax-year
    isolation) — it is never silently accepted under the case's year.

### Missing documents and intake questions

15. **[pending A3]** A case SHALL track a set of **required document categories**
    and a set of **intake questions**. These required-document and intake-question
    sets SHALL be **inputs to the case** (provided externally by whoever
    configures the engagement), **not** computed by Tax Case Core from CRA return
    requirements. The case SHALL expose which required document categories have
    **no** received evidence ("missing documents") and which intake questions are
    **unanswered**.
16. **[design]** Answering an intake question SHALL require that the question
    belongs to the case; answering a question **not in the case** SHALL fail
    closed. Once answered, a question SHALL count as answered for readiness.

### Readiness (derived, not writable)

17. **[pending A3]** The case SHALL expose a **readiness-for-accountant-review**
    result **derived** from domain state on read. It SHALL have **no public
    writable setter**; it cannot be set true independently of state. Recommended
    predicate: readiness is **Ready** iff the case has exactly one primary
    taxpayer **and** every required document category has ≥1 received evidence
    record **and** every intake question is answered **and** no fail-closed
    validation state is outstanding; otherwise **NotReady**.
18. **[design]** A **NotReady** result SHALL carry SIN-safe reasons (e.g. which
    required document categories are missing, which intake questions are
    unanswered) so the accountant knows what remains, without exposing any SIN.

### Lifecycle / status

19. **[design]** A case SHALL have a **lifecycle status** drawn from a fixed set
    `[ASSUMED]` (recommended: `Intake` → `InformationGathering` → `InReview` →
    `Completed`, with `Cancelled` reachable from any non-terminal status).
    Transitions not permitted by the state machine SHALL fail closed.
20. **[design/pending A3]** Transitioning **into `InReview`** SHALL require the
    derived readiness result to be **Ready**; attempting it while **NotReady**
    SHALL fail closed (a case cannot enter accountant review while demonstrably
    incomplete). `[ASSUMED]` gate — see Open questions.

### Fail-closed and determinism

21. **[reuse]** Every rejectable operation SHALL be **fail-closed** via
    `ParseResult<T>` — a structured error and **no** state mutation on failure.
    There is no partial/best-effort case state.
22. **[design]** Tax Case Core SHALL be **deterministic and side-effect free**:
    the same sequence of operations on the same inputs SHALL always yield the
    same case state and the same readiness result. It performs no I/O, no OCR/LLM
    call, no persistence, and no network access.

---

## Out of scope

Each item is explicitly excluded from this spec:

- **Database / persistence / ORM / EF Core** — no storage; the case is an
  in-memory domain aggregate.
- **API or frontend** — no ASP.NET Core host, no Blazor/UI.
- **Authentication, authorization, invitations** — firm ownership is a domain
  attribute here, not an auth system.
- **Document storage / upload / file bytes** — the case records *that* evidence
  of a category was received, not the file itself.
- **OCR or LLM** — no extraction; received evidence is untrusted and unparsed.
- **PDF generation** and any export format.
- **Tax calculations of any kind** — no taxable-income, credit, CPP/EI, or
  medical-expense-threshold math.
- **Box→CRA-line mapping and slip parsing** — these remain in `TaxLogicEngine`
  (specs 001–004) and the future T4 Case Processor; Tax Case Core carries no CRA
  line number.
- **NETFILE / EFILE / CRA submission.**
- **Any CRA eligibility or definition rule** — who qualifies as a common-law
  partner, an eligible/eligible-dependant, or an eligible medical expense; the
  CRA eligible-medical-expense list; and any threshold. All deferred to a future,
  CRA-verified spec. Encoding any of these here would require canada.ca facts
  that this PRD deliberately does not assert.
- **A shared cross-case "client/person" identity** — a participant is local to
  its case in the MVP; a shared Clients module is a documented future module, not
  built here.
- **Tax years other than 2025.**

---

## Acceptance criteria

Each criterion is a single mechanically-checkable test (Given/When/Then).
Criteria marked `[pending A1/A2/A3]` are written to the recommended default and
finalize when the matching Class A question is answered.

### Happy path

1. **Create a minimal valid case.**
   Given a non-empty firm id, tax year 2025, and a primary taxpayer,
   When the case is created, Then a case is returned with one PrimaryTaxpayer
   participant, that firm id, tax year 2025, and zero errors.

2. **Full valid case.**
   Given a 2025 case with a primary taxpayer, one SpouseOrCommonLaw participant,
   one Dependant, one received T4-slip evidence and one medical-expense evidence
   (both attributed to in-case participants and tax year 2025), and all intake
   questions answered,
   When the case state is read, Then all participants, both evidence records, and
   the answered questions are present, correctly attributed, and produce no error.

### Fail-closed rejections (one rule per criterion)

3. **Missing firm identifier.**
   Given a create request with a blank/absent firm id,
   When executed, Then it fails closed with a structured error naming the firm id
   and no case is produced.

4. **Unsupported / missing tax year (year isolation).**
   Given a create request with tax year 2024 (and again 2026, and again absent),
   When executed, Then each fails closed with a tax-year error; the case is never
   coerced to 2025 and no adjacent-year fallback is applied.

5. **No primary taxpayer.**
   Given a create request with no PrimaryTaxpayer participant,
   When executed, Then it fails closed with a structured error and no case.

6. **More than one primary taxpayer.**
   Given an attempt to add a second PrimaryTaxpayer,
   When executed, Then it fails closed and the case still has exactly one.

7. **More than one spouse/common-law.**
   Given a case with one SpouseOrCommonLaw and an attempt to add a second,
   When executed, Then it fails closed.

8. **Duplicate participant.**
   Given an attempt to add a participant whose within-case identity already
   exists,
   When executed, Then it fails closed with a structured error and no duplicate.

9. **Evidence attributed to a non-member participant.**
   Given evidence attributed to a participant not in the case,
   When attached, Then it fails closed with a structured error and no evidence is
   recorded.

10. **Evidence for a different tax year (year isolation).**
    Given a 2025 case and a T4-slip evidence record stated for tax year 2024,
    When attached, Then it fails closed with a tax-year error and is not recorded
    under 2025.

11. **Unknown document category.**
    Given evidence with a category outside the supported set,
    When attached, Then it fails closed with a structured error naming the
    category.

12. **Cross-firm reference.**
    Given a case owned by firm A and an attempt to add a participant/evidence/
    reference carrying firm B,
    When executed, Then it fails closed with a structured error and nothing is
    added.

13. **Cross-firm ownership change.**
    Given a case owned by firm A and an attempt to change/transfer its owning firm
    to firm B,
    When executed, Then it fails closed and the owning firm remains firm A.

14. **Invalid lifecycle transition.**
    Given a case in `Intake` and a transition not permitted by the state machine
    (e.g. `Intake` → `Completed` directly `[ASSUMED]`),
    When executed, Then it fails closed and the status is unchanged.

15. **Enter review while not ready** *(recommended; [pending A3]).*
    Given a case whose derived readiness is NotReady,
    When a transition into `InReview` is attempted, Then it fails closed and the
    status is unchanged.

16. **Answer a question not in the case.**
    Given an answer for an intake question id that is not part of the case,
    When submitted, Then it fails closed with a structured error and no answer is
    recorded.

17. **Readiness has no writable setter.**
    Given the compiled case type,
    When its public surface is inspected, Then there is no public setter/method
    that sets readiness to Ready directly; readiness is computed from state only.

### Boundaries

18. **Primary-only case with no requirements is Ready** *([pending A3]).*
    Given a 2025 case with only a primary taxpayer, an empty required-document
    set, and no intake questions,
    When readiness is read, Then it is Ready with no missing-document or
    unanswered-question reasons.

19. **Missing documents and unanswered questions make it NotReady** *([pending A3]).*
    Given a 2025 case with one required document category and one unanswered
    intake question, both unsatisfied,
    When readiness is read, Then it is NotReady and its reasons name the missing
    category and the unanswered question (SIN-safe).

20. **Satisfying the last requirement flips readiness to Ready** *([pending A3]).*
    Given the case in AC-19, When the required evidence is attached and the
    question answered, Then readiness becomes Ready with no reasons.

21. **Empty create input.**
    Given a create request with no firm id and no primary taxpayer,
    When executed, Then it fails closed with one structured error per missing
    required field and throws no exception.

### PII

22. **SIN masking in all output.**
    Given a participant whose SIN is `123456789`,
    When the case (and its participants), any `ValidationError.Message`, any
    readiness reason, and any `ToString()`/serialized form are produced,
    Then only `***-**-789` can appear and no 9-consecutive-digit SIN substring is
    present anywhere.

23. **Participant without a SIN is allowed** *([pending A2]).*
    Given a participant created with no SIN,
    When the case is created and read, Then it succeeds with no error and no SIN
    is fabricated or leaked.

### Tax-year isolation

24. **A 2025 case never adopts another year's evidence or rules.**
    Given a 2025 case, When 2024-stated evidence is attached (AC-10) and when a
    non-2025 case creation is attempted (AC-4), Then both fail closed and the 2025
    case's tax year and contents are never altered to another year and never
    validated under another year's rules.

### Data-driven mapping / no hardcoded CRA facts

25. **No CRA line number or box→line mapping in Tax Case Core.**
    Given the compiled Tax Case Core assembly,
    When its source/emitted metadata is inspected, Then it contains no CRA line
    number literal, no box→line mapping, and no tax calculation — such logic lives
    only in `TaxLogicEngine` and its JSON config.

### Reuse / determinism

26. **SIN uses the reused `RecipientIdentifier`.**
    Given a participant SIN, When its type is inspected, Then it is the existing
    `RecipientIdentifier` (no new SIN/identifier type was introduced).

27. **Rejectable operations return the reused `ParseResult<T>`.**
    Given any rejectable operation (create, add participant, attach evidence,
    answer question, transition), When it fails, Then it returns a
    `ParseResult<T>.Failure` with ≥1 `ValidationError` and mutates no state.

28. **Determinism.**
    Given the same sequence of operations on the same inputs applied twice,
    When the two resulting case states and readiness results are compared, Then
    they are equal.

---

## Open questions

### Blocking Class A — human decision required (not assumed)

These determine what constitutes valid taxpayer-domain state, whether an
unverifiable CRA fact would be encoded, and how participant PII is handled — the
categories CLAUDE.md marks Class A. Strong, safe recommendations are given so
each can be confirmed in one pass. I did **not** assume them.

**A1 — Does Tax Case Core encode any CRA eligibility/definition, or is it purely
structural?** Specifically: (a) common-law / spousal status determination, (b)
dependant / eligible-dependant eligibility, (c) medical-expense eligibility, the
CRA eligible-expense list, and any threshold. **Recommended: purely structural.**
The case stores a *declared* relationship kind and an evidence *category* only;
every eligibility determination is deferred to a future, CRA-verified spec. This
is raised rather than assumed because encoding any of (a)–(c) here would require
box/threshold/eligibility facts from canada.ca that this PRD deliberately does
not assert — if the reviewer wants any of these inside core, this becomes a
CRA-fact-bearing feature requiring canada.ca verification.

**A2 — Is a participant SIN optional or required at case/participant creation?**
**Recommended: optional.** A SIN is frequently unknown at early intake; requiring
a real SIN would wrongly block legitimate cases, and requiring the `000000000`
sentinel would be meaningless here. When present the SIN is wrapped in
`RecipientIdentifier` and masked everywhere; it is never emitted in full and
never required by core. "What constitutes a valid participant" is Class A, so
this is confirmed rather than assumed.

**A3 — What defines readiness, and where do the required-document / intake sets
come from?** **Recommended:** the required-document categories and intake
questions are **inputs to the case** (provided externally, not computed by core
from CRA return requirements); readiness is derived as *(exactly one primary
taxpayer) AND (every required document category has ≥1 received evidence) AND
(every intake question answered) AND (no outstanding fail-closed state)*;
readiness has no writable setter; and entering `InReview` requires readiness =
Ready. This is Class A because a wrong readiness predicate can present an
incomplete return as review-ready. The alternative (core computes the required
set from CRA return requirements) would encode unverified tax facts and is
deferred.

### `[ASSUMED]` — Class B (one-pass human override)

- `[ASSUMED]` **Module home / project:** a new `src/TaxCases/` project (name and
  location assumed) houses Tax Case Core, per CLAUDE.md's "future modules get
  `src/<Module>/` homes when built"; it depends inward on `TaxLogicEngine` domain
  types only and on none of ASP.NET Core, EF Core, SQL, OCR/LLM/cloud/email SDKs.
- `[ASSUMED]` **Type names** (e.g. `TaxCase`, `CaseParticipant`, a participant
  `RelationshipKind` enum, `ReceivedEvidence`, a `DocumentCategory` enum,
  `IntakeQuestion`/answer types, a `CaseStatus` enum, a `ReviewReadiness` result
  type) follow the existing naming conventions; exact names are for `/plan`.
- `[ASSUMED]` **Relationship kinds** are exactly `PrimaryTaxpayer`,
  `SpouseOrCommonLaw`, `Dependant` for the MVP.
- `[ASSUMED]` **Supported document categories** are `T4Slip`,
  `MedicalExpenseEvidence`, and a generic `Other`; an unrecognized category fails
  closed.
- `[ASSUMED]` **Lifecycle states / transitions:** `Intake` →
  `InformationGathering` → `InReview` → `Completed`, with `Cancelled` reachable
  from any non-terminal state; any other transition fails closed.
- `[ASSUMED]` **Rejectable operations return `ParseResult<T>`** with
  `ValidationError`s, mirroring the engine's fail-closed convention, rather than
  throwing.
- `[ASSUMED]` **Participants are local to the case** — no shared cross-case
  "client/person" identity in the MVP (that is the future Clients module).
- `[ASSUMED]` **Firm identifier** is an opaque non-empty value (string/GUID) on
  the case; how firms are administered is out of scope.
- `[ASSUMED]` **Evidence is a record of receipt** (category + participant + tax
  year + optional opaque source reference), not file bytes and not parsed
  content.
- `[ASSUMED]` **Spec folder** is `specs/006-tax-case-core/` (001–005 taken).
- `[ASSUMED]` **Tax year 2025 only** for the MVP; any other year fails closed
  (mirrors the engine and CLAUDE.md).

---

## CRA sources

**No new external tax facts are asserted by this PRD.** Tax Case Core introduces
no CRA box number, line number, rate, threshold, limit, deadline, gross-up, or
credit percentage, and no eligibility rule. The only tax facts it *relies on*
already exist and were verified against canada.ca in prior, cited specs, and are
reused unchanged rather than re-asserted here:

| Reused fact (not re-asserted here) | Verified in | Source | Tax year | Retrieved |
|---|---|---|---|---|
| T4 slip semantics and box definitions (used only as an evidence *category* name; no parsing/mapping in this feature) | spec 003 `prd.md`, spec 004 `prd.md` | canada.ca T4 employer & individual pages (URLs recorded in those PRDs) | 2025 | 2026-07-09 / 2026-07-11 |
| T4 box→CRA-line mapping (remains in `TaxLogicEngine` JSON config; not touched here) | spec 004 `prd.md` | canada.ca T4 individuals page (URL recorded there) | 2025 | 2026-07-11 |

Any genuine CRA fact that Tax Case Core would otherwise need (common-law /
dependant eligibility definitions; the eligible-medical-expense list and its
threshold) is **not** asserted — it is surfaced as blocking Class A question A1
and deferred to a future spec that must verify it against canada.ca at that time.
This PRD did not resolve any source conflict by its own judgement, and no
unverified tax fact entered it.
