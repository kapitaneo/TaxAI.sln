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
Three questions that would otherwise force an unverifiable CRA claim were raised
as blocking Class A questions rather than assumed, and have since been
**resolved by human decision** (see `Resolved Class A decisions`): the core is
purely structural (A1), a participant SIN is optional and always masked (A2),
and readiness is derived from externally supplied requirement sets (A3). No
blocking Class A questions remain.

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
Supporting **2025** only is a **product-scope policy of spec 006**, not a rule
Tax Case Core discovers at runtime: the core stores and validates an explicit
tax year against this spec's supported set, and it does **not** inspect
`TaxLogicEngine` mapping files (e.g.
`TaxLogicEngine.Mappings.T4LineMap.2025.json`) to determine case validity or
readiness. Whether a given engine or mapping config supports a case's year is
verified **independently by the future processors** that invoke the engine, at
the time they run. Per CLAUDE.md, tax years are never mixed and there is no
"latest"/"current" year: a case whose tax year is not a supported year **fails
closed** — it is never coerced to 2025 and **no adjacent-year fallback is
allowed**. Evidence attached to a case must carry the **same** tax year as the
case. Expanding beyond 2025 is out of scope (a new supported year is a scope
change, not a silent widening).

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
  intake question, configure/confirm the checklist, transition status, reopen)
  returns a `ParseResult<T>`.
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
participants and their relationship kind, received-evidence records (each with
its own evidence identity) and their document categories, an explicitly
configured requirement checklist (required document categories and intake
questions) and its answered/unanswered state, a lifecycle status, a derived
readiness result, and an immutable deterministic **domain change record** emitted
by accepted operations — plus the case service/aggregate that enforces the
invariants below. Concrete type names, the module/project home, and file layout
are Class B `[ASSUMED]` (see Open questions); the module must obey CLAUDE.md's
dependency rules (it may depend inward on `TaxLogicEngine` domain types; it must
**not** depend on ASP.NET Core, EF Core, SQL, OCR/LLM/cloud SDKs, or any I/O that
would make its result non-deterministic).

---

## User stories

1. As an **accountant**, I want to open one tax case for one client and one tax
   year, owned by my firm, so that all of that engagement's people, evidence,
   and open questions live in one place.
2. As an **accountant**, I want to add the client as the primary taxpayer and,
   when relevant, one spouse/common-law partner and any dependants, so that a
   coupled or family engagement is one case with clearly separated participants.
3. As an **accountant**, I want to record which evidence I have received (a T4
   slip, medical-expense evidence) against the right participant and tax year —
   including several slips or receipts of the same kind — so that I can see
   exactly what has arrived without the case parsing or judging it.
4. As an **accountant**, I want the case to tell me which required documents are
   still missing and which intake questions are still unanswered, so that I know
   what to chase.
5. As an **accountant**, I want a **derived** "ready for review" signal that I
   cannot simply switch on — and that stays off until I have explicitly
   confirmed what this engagement requires — so that a case never reports ready
   while it is demonstrably incomplete or before its requirements were even
   stated.
6. As a **firm administrator**, I want a case and everything in it to belong to
   exactly one firm, with cross-firm references rejected, so that a case never
   contains or references another firm's data. (Whether a given *caller* may
   read a case is caller authorization — a future application/authentication
   layer, not Tax Case Core.)
7. As a **compliance reviewer**, I want participant SINs masked to the last 3
   digits everywhere — output, logs, errors, serialized form — so that the case
   cannot leak a SIN (CLAUDE.md).
8. As a **compliance reviewer**, I want every accepted lifecycle transition and
   readiness-affecting change to produce an immutable, deterministic domain
   change record, so that how a case reached its current state can be
   reconstructed and audited later.
9. As an **engine maintainer**, I want the case to be deterministic and
   provider-independent (no DB, no OCR, no network, no system clock), so that
   the same inputs always produce the same case state, readiness result, and
   change records.

---

## Functional requirements

Requirements marked `[decided A1/A2/A3]` implement the human-approved Class A
decisions recorded under `Resolved Class A decisions` — they are final, not
pending. `[design]` marks a decision that depends on no CRA fact. `[reuse]`
marks reuse of an existing type.

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
4. **[design]** **Ownership is not authorization.** Tax Case Core enforces
   (a) immutable firm ownership, (b) consistency of every in-case reference with
   the owning firm, and (c) rejection of cross-firm entities/references. It SHALL
   NOT authenticate callers and SHALL NOT decide whether a caller may read a
   case; caller authentication and read authorization belong to a future
   application/authentication layer. This PRD makes **no claim** that the
   in-memory aggregate alone prevents unauthorized reads.
5. **[design/verified-scope]** A case's tax year SHALL be a **supported year
   (2025)** — a product-scope policy of this spec, stored and validated as an
   explicit year on the case. A case whose tax year is absent or is any other
   value SHALL fail closed — never coerced to 2025, and **no adjacent-year
   fallback** (CLAUDE.md). A case represents exactly one tax year and SHALL NOT
   mix years. Tax Case Core SHALL NOT inspect `TaxLogicEngine` mapping files to
   determine case validity or readiness; whether an engine/config supports the
   case's year is verified independently by the future processors that invoke
   the engine.

### Participants and separation

6. **[design]** A case SHALL have **exactly one** participant with relationship
   kind **PrimaryTaxpayer**. Zero primary taxpayers, or more than one, SHALL fail
   closed.
7. **[design]** A case SHALL have **at most one** participant with relationship
   kind **SpouseOrCommonLaw**. Adding a second SHALL fail closed.
8. **[decided A1]** A case MAY have zero or more participants with relationship
   kind **Dependant**. Tax Case Core is **purely structural**: it stores
   *declared* participant relationships and evidence categories only, and SHALL
   perform **no CRA eligibility determination** for spouse/common-law status,
   dependant status, or medical expenses. Any eligibility determination is
   deferred to a future, CRA-verified spec (see Out of scope).
9. **[design]** Each participant SHALL have a stable within-case identity.
   Adding the **same participant twice** (same within-case identity) SHALL fail
   closed with a structured error.
10. **[design]** Every evidence record and every intake answer SHALL be
    attributed to a specific participant that belongs to the case. Attributing
    evidence or an answer to a participant **not in the case** SHALL fail closed.
    No participant's evidence or answers SHALL be readable as another
    participant's (participant data separation).

### PII (participant SIN)

11. **[decided A2]** A participant SIN SHALL be **optional** at creation (it may
    be unknown at intake). When present it SHALL be wrapped in the reused
    `RecipientIdentifier` and SHALL NEVER be emitted in full by any `ToString()`,
    log line, exception/validation message, readiness reason, domain change
    record, or serialized form — only the last-3-digits masked form. A missing
    SIN SHALL NOT block case creation.
12. **[reuse]** No `ValidationError.Message`, no readiness reason, and no domain
    change record SHALL contain a full SIN. Tax Case Core SHALL introduce no
    alternative, unmasked SIN representation.

### Received evidence, evidence identity, and document categories

13. **[design]** The case SHALL record **received evidence** as a document
    **category** attributed to a participant and a tax year. The MVP supported
    categories SHALL include **T4 employment slip** and **medical-expense
    evidence** (plus a generic catch-all `[ASSUMED]`). An **unknown/unsupported
    category** SHALL fail closed with a structured error naming the category.
14. **[design]** Every evidence record SHALL carry an **evidence identity** that
    is **distinct from its category**. The identity SHALL be **stable and
    non-empty**; an evidence record with a missing or blank identity SHALL fail
    closed and record nothing.
15. **[design]** The case SHALL allow **multiple distinct evidence records of the
    same category**, including multiple distinct T4-slip evidence records for
    the **same participant** and multiple distinct medical-expense evidence
    records. Attaching an evidence record whose **evidence identity already
    exists on the case** SHALL fail closed as a duplicate and record nothing.
16. **[design]** Tax Case Core SHALL treat received evidence as an **untrusted,
    unparsed record of receipt** — an identity, category, and attribution only.
    It SHALL NOT parse a slip, extract box values, compute eligibility, or apply
    any CRA mapping (that is the engine / future processor). No CRA line number,
    box→line mapping, or tax calculation SHALL appear in Tax Case Core.
17. **[design]** An evidence record's tax year SHALL match the case's tax year.
    Evidence for a **different tax year** than the case SHALL fail closed
    (tax-year isolation) — it is never silently accepted under the case's year.

### Requirement checklist (required documents and intake questions)

18. **[decided A3]** The case SHALL track a set of **required document
    categories** and a set of **intake questions**. These required-document and
    intake-question sets SHALL be **explicit inputs supplied outside Tax Case
    Core** (provided by whoever configures the engagement), **not** computed by
    Tax Case Core from CRA return requirements. The case SHALL expose which
    required document categories have no satisfying evidence ("missing
    documents") and which intake questions are **unanswered**.
19. **[design]** The case SHALL distinguish **checklist not configured** from
    **checklist explicitly configured and confirmed** — including an
    **explicitly confirmed empty checklist**. A newly created case SHALL start
    with an **unconfigured** checklist, and SHALL NOT be treated as if an empty
    checklist had been confirmed. A checklist SHALL only become configured
    through an explicit configure/confirm operation.
20. **[design]** A required document category SHALL count as **satisfied** if and
    only if the case holds **at least one valid evidence record of that exact
    category**. An evidence record of the generic/`Other` category — or of any
    unsupported category — SHALL NEVER satisfy a T4-slip or
    medical-expense-evidence requirement.
21. **[design]** Answering an intake question SHALL require that the question
    belongs to the case; answering a question **not in the case** SHALL fail
    closed. Once answered, a question SHALL count as answered for readiness.

### Readiness (derived, not writable)

22. **[decided A3]** The case SHALL expose a **readiness-for-accountant-review**
    result **derived** from domain state on read. It SHALL have **no public
    writable setter**; it cannot be set Ready independently of state. Readiness
    SHALL be **Ready** iff **all** of the following hold; otherwise
    **NotReady**:
    - the requirement checklist has been **explicitly configured/confirmed**
      (an unconfigured checklist ⇒ NotReady, regardless of anything else);
    - the case has exactly one primary taxpayer;
    - every required document category is satisfied per requirement 20;
    - every intake question is answered;
    - no fail-closed validation state is outstanding.
    An **explicitly confirmed empty checklist** MAY yield Ready when every other
    condition above passes. A newly created case SHALL NOT become Ready merely
    because no requirements were provided.
23. **[design]** A **NotReady** result SHALL carry SIN-safe reasons (e.g. the
    checklist is not configured, which required document categories are missing,
    which intake questions are unanswered) so the accountant knows what remains,
    without exposing any SIN.

### Lifecycle / status

24. **[design]** A case SHALL have a **lifecycle status** drawn from a fixed set
    `[ASSUMED]` (recommended: `Intake` → `InformationGathering` → `InReview` →
    `Completed`, with `Cancelled` reachable from any non-terminal status).
    Transitions not permitted by the state machine SHALL fail closed.
    **`Archived` is NOT a Tax Case Core lifecycle status in this spec** —
    archival is a future persistence/application concern (see Out of scope).
25. **[decided A3]** Transitioning **into `InReview`** SHALL require the derived
    readiness result to be **Ready**; attempting it while **NotReady** SHALL fail
    closed (a case cannot enter accountant review while demonstrably
    incomplete).
26. **[design]** A **`Completed`** case SHALL reject participant, evidence,
    intake, checklist, and normal lifecycle mutations: each such attempt SHALL
    fail closed with a structured error and change no state.
27. **[design]** A `Completed` case MAY be reopened **only** through an explicit
    **Reopen** operation carrying a **non-empty reason**. A successful Reopen
    SHALL move the case to **`InformationGathering`**, and readiness SHALL be
    recalculated from the current case state (never restored from a stored
    flag). A Reopen with a missing or blank reason SHALL fail closed and the
    case SHALL remain `Completed`.
28. **[design]** **`Cancelled` is terminal in this MVP**: a `Cancelled` case
    SHALL reject all mutations **and** SHALL reject Reopen; each attempt SHALL
    fail closed and the case SHALL remain `Cancelled`.

### Deterministic auditability

29. **[design]** Every **accepted** lifecycle transition and every **accepted**
    readiness-affecting operation (create case, add participant, attach
    evidence, answer an intake question, configure/confirm the checklist,
    reopen) SHALL produce an **immutable, deterministic domain change record**
    (domain event). The record SHALL identify:
    - the case;
    - the operation/change kind;
    - the affected participant, evidence record, or intake question, where
      applicable;
    - the previous and resulting lifecycle state, where applicable;
    - an externally supplied correlation/actor reference, where provided.
    A **rejected** operation SHALL produce **no** domain change record and no
    state mutation. Persistence and delivery of these records are out of scope.
30. **[design]** Tax Case Core SHALL NOT read the system clock, a database, the
    current user, or the network. A timestamp, if required later, SHALL be
    supplied externally as an explicit input; the core SHALL never generate one
    itself.

### Fail-closed and determinism

31. **[reuse]** Every rejectable operation SHALL be **fail-closed** via
    `ParseResult<T>` — a structured error and **no** state mutation on failure.
    There is no partial/best-effort case state.
32. **[design]** Tax Case Core SHALL be **deterministic and side-effect free**:
    the same sequence of operations on the same inputs SHALL always yield the
    same case state, the same readiness result, and the same domain change
    records. It performs no I/O, no OCR/LLM call, no persistence, and no network
    access.

---

## Out of scope

Each item is explicitly excluded from this spec:

- **Database / persistence / ORM / EF Core** — no storage; the case is an
  in-memory domain aggregate.
- **API or frontend** — no ASP.NET Core host, no Blazor/UI.
- **Authentication, authorization, invitations** — firm ownership is a domain
  attribute here, not an auth system. **Caller authentication and read
  authorization** (who may open/read a case) belong to a future
  application/authentication layer; Tax Case Core enforces ownership and
  reference consistency only (requirement 4).
- **Archival** — `Archived` is not a lifecycle status in this spec; archiving a
  case is a future persistence/application concern.
- **Persistence and delivery of domain change records** — the core emits them
  deterministically (requirement 29); storing, forwarding, or subscribing to
  them is a future concern.
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
  CRA-verified spec (per resolved decision A1). Encoding any of these here would
  require canada.ca facts that this PRD deliberately does not assert.
- **A shared cross-case "client/person" identity** — a participant is local to
  its case in the MVP; a shared Clients module is a documented future module, not
  built here.
- **Tax years other than 2025.**

---

## Acceptance criteria

Each criterion is a single mechanically-checkable test (Given/When/Then).

### Happy path

1. **Create a minimal valid case.**
   Given a non-empty firm id, tax year 2025, and a primary taxpayer,
   When the case is created, Then a case is returned with one PrimaryTaxpayer
   participant, that firm id, tax year 2025, and zero errors.

2. **Full valid case.**
   Given a 2025 case with a primary taxpayer, one SpouseOrCommonLaw participant,
   one Dependant, an explicitly confirmed checklist, one received T4-slip
   evidence and one medical-expense evidence (each with a distinct evidence
   identity, both attributed to in-case participants and tax year 2025), and all
   intake questions answered,
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

15. **Enter review while not ready.**
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

### Evidence identity and multiplicity

18. **Multiple T4 records for the same participant.**
    Given a 2025 case and two T4-slip evidence records with **distinct** evidence
    identities, both attributed to the same in-case participant,
    When both are attached, Then both succeed and the case holds both records,
    each retrievable by its own identity.

19. **Multiple medical-expense records.**
    Given a 2025 case and two medical-expense evidence records with **distinct**
    evidence identities,
    When both are attached, Then both succeed and the case holds both records.

20. **Duplicate evidence identity rejected.**
    Given a case holding an evidence record with identity `E1`,
    When a second evidence record with the **same** identity `E1` is attached,
    Then it fails closed with a structured error and the case still holds exactly
    one record with identity `E1`.

21. **Missing/blank evidence identity rejected.**
    Given an evidence record with a missing or blank evidence identity,
    When attached, Then it fails closed with a structured error and no evidence is
    recorded.

22. **`Other` never satisfies a specific requirement.**
    Given a case whose confirmed checklist requires the T4-slip category and the
    medical-expense-evidence category, and an attached evidence record of the
    generic `Other` category,
    When readiness is read, Then both required categories are still reported
    missing and readiness is NotReady.

### Checklist configuration and readiness (fail closed by default)

23. **Unconfigured checklist ⇒ NotReady.**
    Given a newly created 2025 case with a primary taxpayer and **no** checklist
    configuration,
    When readiness is read, Then it is NotReady and its reasons include that the
    requirement checklist has not been configured.

24. **Configured checklist with missing items ⇒ NotReady.**
    Given a 2025 case whose explicitly confirmed checklist has one required
    document category and one intake question, both unsatisfied,
    When readiness is read, Then it is NotReady and its reasons name the missing
    category and the unanswered question (SIN-safe).

25. **Satisfying the last requirement flips readiness to Ready.**
    Given the case in AC-24, When a valid evidence record of the required
    category is attached and the question answered, Then readiness becomes Ready
    with no reasons.

26. **Explicitly confirmed empty checklist may be Ready.**
    Given a 2025 case with a primary taxpayer whose checklist has been
    **explicitly confirmed as empty** and every other readiness condition
    passes,
    When readiness is read, Then it is Ready with no missing-document or
    unanswered-question reasons.

### Terminal states and reopen

27. **Mutation after `Completed` fails closed.**
    Given a `Completed` case,
    When adding a participant, attaching evidence, answering an intake question,
    configuring the checklist, and making a normal lifecycle transition are each
    attempted, Then each attempt fails closed with a structured error and the
    case state (including status `Completed`) is unchanged.

28. **Successful reopen.**
    Given a `Completed` case,
    When Reopen is invoked with a non-empty reason, Then it succeeds, the status
    becomes `InformationGathering`, and readiness is recalculated from the
    current case state rather than restored from any stored value.

29. **Invalid reopen (blank reason).**
    Given a `Completed` case,
    When Reopen is invoked with a missing or blank reason, Then it fails closed
    with a structured error and the status remains `Completed`.

30. **`Cancelled` is terminal.**
    Given a `Cancelled` case,
    When any mutation and a Reopen are each attempted, Then each fails closed and
    the status remains `Cancelled`.

### Deterministic auditability

31. **Accepted transition produces a domain change record.**
    Given an accepted lifecycle transition invoked with an externally supplied
    correlation/actor reference,
    When it completes, Then exactly one immutable domain change record is
    produced identifying the case, the operation kind, the previous and the
    resulting lifecycle state, and the supplied correlation/actor reference.

32. **Accepted readiness-affecting operation produces a domain change record.**
    Given an accepted attach-evidence operation (and likewise an accepted
    add-participant, answer-question, and checklist-confirmation),
    When it completes, Then a domain change record is produced identifying the
    case, the operation kind, and the affected evidence record / participant /
    question.

33. **Rejected operation produces no change record.**
    Given any operation that fails closed,
    When it completes, Then no domain change record is produced and no state is
    mutated.

34. **No clock, database, user, or network access.**
    Given the Tax Case Core source/assembly,
    When inspected, Then it contains no read of the system clock (e.g.
    `DateTime.Now`/`DateTime.UtcNow`), no database access, no current-user
    lookup, and no network access; any timestamp appears only as an externally
    supplied input parameter.

35. **Change records are deterministic.**
    Given the same sequence of operations on the same inputs applied twice,
    When the two produced sequences of domain change records are compared, Then
    they are equal.

### Boundaries

36. **Empty create input.**
    Given a create request with no firm id and no primary taxpayer,
    When executed, Then it fails closed with one structured error per missing
    required field and throws no exception.

### PII

37. **SIN masking in all output.**
    Given a participant whose SIN is `123456789`,
    When the case (and its participants), any `ValidationError.Message`, any
    readiness reason, any domain change record, and any `ToString()`/serialized
    form are produced,
    Then only `***-**-789` can appear and no 9-consecutive-digit SIN substring is
    present anywhere.

38. **Participant without a SIN is allowed.**
    Given a participant created with no SIN,
    When the case is created and read, Then it succeeds with no error and no SIN
    is fabricated or leaked.

### Tax-year isolation

39. **A 2025 case never adopts another year's evidence or rules.**
    Given a 2025 case, When 2024-stated evidence is attached (AC-10) and when a
    non-2025 case creation is attempted (AC-4), Then both fail closed and the 2025
    case's tax year and contents are never altered to another year and never
    validated under another year's rules.

### Data-driven mapping / no hardcoded CRA facts

40. **No CRA line number, box→line mapping, or mapping-file inspection in Tax
    Case Core.**
    Given the compiled Tax Case Core assembly,
    When its source/emitted metadata is inspected, Then it contains no CRA line
    number literal, no box→line mapping, no tax calculation, and no read of a
    `TaxLogicEngine` mapping/config file — such logic lives only in
    `TaxLogicEngine` and its JSON config.

### Reuse / determinism

41. **SIN uses the reused `RecipientIdentifier`.**
    Given a participant SIN, When its type is inspected, Then it is the existing
    `RecipientIdentifier` (no new SIN/identifier type was introduced).

42. **Rejectable operations return the reused `ParseResult<T>`.**
    Given any rejectable operation (create, add participant, attach evidence,
    answer question, configure/confirm checklist, transition, reopen), When it
    fails, Then it returns a `ParseResult<T>.Failure` with ≥1 `ValidationError`
    and mutates no state.

43. **Determinism.**
    Given the same sequence of operations on the same inputs applied twice,
    When the two resulting case states and readiness results are compared, Then
    they are equal.

---

## Open questions

### Resolved Class A decisions (human-approved; no blocking questions remain)

These were raised as blocking Class A questions in the first draft of this PRD
and have been decided by a human reviewer. They are recorded here as final
decisions, applied throughout the requirements and acceptance criteria above.

**A1 — resolved: Tax Case Core is purely structural.** It stores declared
participant relationships and evidence categories only. It performs **no CRA
eligibility determination** for spouse/common-law status, dependants, or medical
expenses. Every such determination is deferred to a future, CRA-verified spec
(see Out of scope). Consequently this PRD asserts no new canada.ca fact.

**A2 — resolved: a participant SIN is optional.** When present, it MUST use the
existing `RecipientIdentifier` and remain masked everywhere — output, logs,
errors, readiness reasons, domain change records, and serialized forms. A
missing SIN never blocks case creation.

**A3 — resolved: required-document categories and intake questions are explicit
inputs supplied outside Tax Case Core** (not computed by the core from CRA
return requirements). Readiness is **derived** from case state, has **no
writable setter**, and entering `InReview` requires Ready. Refined by the
checklist-configuration rule (requirement 19): readiness additionally requires
that the checklist has been explicitly configured/confirmed, so an unconfigured
case is NotReady and never becomes Ready merely because no requirements were
provided.

### `[ASSUMED]` — Class B (one-pass human override)

- `[ASSUMED]` **Module home / project:** a new `src/TaxCases/` project (name and
  location assumed) houses Tax Case Core, per CLAUDE.md's "future modules get
  `src/<Module>/` homes when built"; it depends inward on `TaxLogicEngine` domain
  types only and on none of ASP.NET Core, EF Core, SQL, OCR/LLM/cloud/email SDKs.
- `[ASSUMED]` **Type names** (e.g. `TaxCase`, `CaseParticipant`, a participant
  `RelationshipKind` enum, `ReceivedEvidence`, a `DocumentCategory` enum,
  `IntakeQuestion`/answer types, a `CaseStatus` enum, a `ReviewReadiness` result
  type, and the domain change record / event types) follow the existing naming
  conventions; exact names are for `/plan`.
- `[ASSUMED]` **Relationship kinds** are exactly `PrimaryTaxpayer`,
  `SpouseOrCommonLaw`, `Dependant` for the MVP.
- `[ASSUMED]` **Supported document categories** are `T4Slip`,
  `MedicalExpenseEvidence`, and a generic `Other`; an unrecognized category fails
  closed.
- `[ASSUMED]` **Lifecycle states / transitions:** `Intake` →
  `InformationGathering` → `InReview` → `Completed`, with `Cancelled` reachable
  from any non-terminal state; any other transition fails closed. `Archived` is
  not a status (human-directed, Fix 3).
- `[ASSUMED]` **Rejectable operations return `ParseResult<T>`** with
  `ValidationError`s, mirroring the engine's fail-closed convention, rather than
  throwing.
- `[ASSUMED]` **Participants are local to the case** — no shared cross-case
  "client/person" identity in the MVP (that is the future Clients module).
- `[ASSUMED]` **Firm identifier** is an opaque non-empty value (string/GUID) on
  the case; how firms are administered is out of scope.
- `[ASSUMED]` **Evidence is a record of receipt** (evidence identity + category
  + participant + tax year + optional opaque source reference), not file bytes
  and not parsed content.
- `[ASSUMED]` **Evidence identity is an opaque, caller-supplied, non-empty
  value.** The core never generates identifiers itself (generation — e.g. a new
  GUID — would be a non-deterministic side effect); uniqueness is enforced
  within the case.
- `[ASSUMED]` **The Reopen reason is opaque, non-empty text.** The core
  validates only that it is non-empty; its content is recorded on the resulting
  domain change record and not otherwise interpreted.
- `[ASSUMED]` **Correlation/actor reference is an opaque, optional,
  externally supplied value** on operations; the core records it verbatim on the
  domain change record when provided and never derives an actor itself.
- `[ASSUMED]` **Spec folder** is `specs/006-tax-case-core/` (001–005 taken).
- `[ASSUMED]` **Tax year 2025 only** for the MVP; any other year fails closed
  (mirrors the engine and CLAUDE.md). This is a product-scope policy of this
  spec, not something the core derives from mapping files (requirement 5).

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
threshold) is **not** asserted — per resolved decision **A1**, the core is
purely structural and every such determination is deferred to a future spec that
must verify it against canada.ca at that time. This PRD did not resolve any
source conflict by its own judgement, and no unverified tax fact entered it.
