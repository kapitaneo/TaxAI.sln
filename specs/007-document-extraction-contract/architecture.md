# Architecture: Document Extraction Contract (spec 007)

**Feature class: `tax-domain`** (from the approved PRD's `## Feature
classification`). This design implements the PRD as revised by human review on
2026-07-13: MVP scope is **T4 slip extraction + generic evidence (initially
medical-expense evidence)**; T5 is deferred; attribution is case-scoped;
the ingress port is an inward application boundary; PII is handled by
**omission**, not masking.

## Approach

A new, deliberately small module `src/DocumentExtraction/` (net8.0, nullable
enabled) with test project `tests/DocumentExtraction.Tests/`, added to
`TaxAI.sln`. Its only production project reference is `TaxLogicEngine` — it
reuses `T4RawInput`/`T4RawBox`, `ParseResult<T>`, and `ValidationError` and
nothing else. It does **not** reference `TaxCases` (per the PRD revision, no
`SafeDiagnosticText` reuse: diagnostics *omit* untrusted values rather than
echo-and-redact them, so no masking helper is needed or permitted here).

The module has three parts, mirroring the repo's Models/Services layout plus a
`Ports/` folder for the application boundary:

1. **Models** (`DocumentExtraction.Models`) — the untrusted
   `ExtractionEnvelope` (loose, all-nullable, like `T4RawInput` itself), its
   `ExtractionProvenance`, the `ExtractedDocumentKind` discriminator, the
   validator-constructed `ValidatedExtraction` success value, and the
   `ExtractionFields` fixed-field-name constants used in every
   `ValidationError.Field`.
2. **Ports** (`DocumentExtraction.Ports`) — `IDocumentExtractionIngress`, the
   inward application boundary. A provider adapter **depends on this contract
   and calls the port**; the port is **implemented by a future application
   layer**, never by the adapter and never in this spec. No implementation of
   the port ships in spec 007.
3. **Services** (`DocumentExtraction.Services`) — the pure, static
   `ExtractionEnvelopeValidator` (the single fail-closed gate every envelope
   passes through) and the `SupportedExtractionTaxYears` year policy
   (2025 only, module-local so no `TaxCases` coupling is created).

Dataflow: *provider adapter (future, out of scope)* → builds an
`ExtractionEnvelope` → calls `IDocumentExtractionIngress.Submit` →
*application implementation (future, out of scope)* invokes
`ExtractionEnvelopeValidator.Validate` → `ParseResult<ValidatedExtraction>` →
*T4 Case Processor (future)* takes the raw payload from a success and performs
the actual parse (`T4SlipParser`, including Box 12 → `RecipientIdentifier`)
and box→line mapping. Nothing in this module parses, types, normalizes,
redacts, converts, maps, rounds, or calculates; the payload passes through
byte-identical. The module is deterministic: no clock, I/O, network,
persistence, environment, or provider SDK.

TaxLogicEngine and TaxCases are **not modified** in any way; the full existing
suite must stay green (PRD AC-29).

## Data model

All types below are new, in `src/DocumentExtraction/`. No engine or TaxCases
type is changed. No JSON mapping config is added — this module owns **no**
box→CRA-line data (PRD AC-26); mappings remain exclusively in
`TaxLogicEngine/Mappings/`.

### `ExtractedDocumentKind` (enum, `Models/ExtractedDocumentKind.cs`)

```csharp
public enum ExtractedDocumentKind
{
    T4Slip = 1,
    GenericEvidence = 2,
}
```

- Values start at 1 so `default(ExtractedDocumentKind)` (0) is undefined and
  fails closed like any other undefined value.
- **No `T5Slip` value exists** — T5 is deferred by approved scope; a caller
  passing a casted unknown value (e.g. `(ExtractedDocumentKind)99`) is
  rejected as an unsupported kind (PRD AC-4), the same way spec 006 proves
  `Archived` absent by the value not existing.

### `ExtractionProvenance` (sealed class, `Models/ExtractionProvenance.cs`)

```csharp
public sealed class ExtractionProvenance
{
    [JsonIgnore] public string? SourceReference { get; init; }
    [JsonIgnore] public string? ProviderLabel { get; init; }
    public decimal? Confidence { get; init; }
    public override string ToString(); // structural facts only, see PII rules
}
```

- `Confidence` is **`decimal?`** per the human-approved PRD: absent allowed;
  `0m`–`1m` inclusive accepted; outside fails closed; **no low-confidence
  threshold exists**. `decimal` avoids NaN/infinity/float-equality ambiguity.
- A sealed **class**, not a record: records auto-generate a `ToString()` that
  prints every property (the exact leak the engine's CLAUDE.md warns about);
  a class makes the safe override the only printable form.

### `ExtractionEnvelope` (sealed class, `Models/ExtractionEnvelope.cs`)

The untrusted input. Everything nullable/loose on purpose — it is unvalidated
upstream data, exactly like `T4RawInput`:

```csharp
public sealed class ExtractionEnvelope
{
    [JsonIgnore] public string? DocumentId { get; init; }
    public ExtractedDocumentKind? Kind { get; init; }
    [JsonIgnore] public string? FirmId { get; init; }
    [JsonIgnore] public string? CaseId { get; init; }
    [JsonIgnore] public string? ParticipantId { get; init; }
    public int? TaxYear { get; init; }
    public ExtractionProvenance? Provenance { get; init; }
    [JsonIgnore] public T4RawInput? T4Payload { get; init; }
    public override string ToString(); // structural facts only
}
```

- The attribution tuple is **FirmId + CaseId + ParticipantId + TaxYear**
  (case-scoped per the PRD revision; `ParticipantId` is case-local and never
  meaningful without `CaseId`).
- The payload slot is a single typed `T4RawInput?`. Generic evidence has **no
  payload property at all** — its required `Provenance.SourceReference` *is*
  the opaque reference to the stored document. A non-null `T4Payload` on a
  `GenericEvidence` envelope is a payload/kind mismatch (fail closed), as is a
  null `T4Payload` on a `T4Slip` envelope.

### `ValidatedExtraction` (sealed class, `Models/ValidatedExtraction.cs`)

The success value. Constructor is **internal** so only the validator can
create one — a `ValidatedExtraction` existing *is* the proof the envelope
passed the gate:

```csharp
public sealed class ValidatedExtraction
{
    [JsonIgnore] public string DocumentId { get; }
    public ExtractedDocumentKind Kind { get; }
    [JsonIgnore] public string FirmId { get; }
    [JsonIgnore] public string CaseId { get; }
    [JsonIgnore] public string ParticipantId { get; }
    public int TaxYear { get; }
    [JsonIgnore] public string SourceReference { get; }
    [JsonIgnore] public string? ProviderLabel { get; }
    public decimal? Confidence { get; }
    [JsonIgnore] public T4RawInput? T4Payload { get; } // non-null iff Kind == T4Slip
    public override string ToString(); // structural facts only
}
```

- The raw payload is exposed **programmatically only** (PRD requirement 21):
  the property is readable in memory for the downstream processor, `[JsonIgnore]`
  for supported direct JSON serialization, and absent from `ToString()`.
- Still untrusted: it is a `T4RawInput`, never a `T4Slip`/`TaxLineEntry`; no
  `RecipientIdentifier` is created or populated anywhere in this module.

### `ExtractionFields` (static class, `Models/ExtractionFields.cs`)

The **fixed field names** every `ValidationError.Field` must use (PRD
requirement 20 / AC-23), declared once as constants so tests can assert the
closed set mechanically:

```csharp
public static class ExtractionFields
{
    public const string Envelope = "Envelope";
    public const string DocumentId = "DocumentId";
    public const string Kind = "Kind";
    public const string FirmId = "FirmId";
    public const string CaseId = "CaseId";
    public const string ParticipantId = "ParticipantId";
    public const string TaxYear = "TaxYear";
    public const string SourceReference = "SourceReference";
    public const string Confidence = "Confidence";
    public const string Payload = "Payload";
    public const string PayloadYear = "PayloadYear";
    public const string PayloadContent = "PayloadContent";
}
```

### PII / diagnostics rules binding every model (omission, not redaction)

- The raw `T4RawInput` payload **may contain an unmasked SIN in Box 12** and
  may be retained unchanged in memory. It is **omitted entirely** — never
  echoed, never masked — from every `ToString()`, every
  `ValidationError.Message`, every diagnostic, and supported direct JSON
  serialization (`[JsonIgnore]`). No box value, identity text, or
  9-consecutive-digit substring may appear in any rendered output (PRD AC-22).
- Every **caller-controlled string** (`DocumentId`, `FirmId`, `CaseId`,
  `ParticipantId`, `SourceReference`, `ProviderLabel`) is likewise
  `[JsonIgnore]` and excluded from `ToString()` and error messages — errors
  identify fields by `ExtractionFields` constants only (PRD AC-23). This
  follows the spec 006 defensive-serialization precedent and is what lets the
  module carry PII-bearing references without a masking helper.
- `ToString()` on every public type emits **fixed structural facts only**
  (kind, tax year, whether a payload is present, box count, confidence) —
  never a caller string, never payload content.
- **No new SIN property or SIN type is introduced**, and the module contains
  no construction of `RecipientIdentifier` — Box 12 conversion remains
  `T4SlipParser`'s job downstream (PRD AC-24).

## Services & public API

### `IDocumentExtractionIngress` (`Ports/IDocumentExtractionIngress.cs`)

```csharp
namespace DocumentExtraction.Ports;

public interface IDocumentExtractionIngress
{
    ParseResult<ValidatedExtraction> Submit(ExtractionEnvelope envelope);
}
```

- The **inward application boundary** (PRD Fix 2): a provider adapter depends
  on this contract and **calls** `Submit`; the interface is **implemented by a
  future application layer** (which will also verify case-belongs-to-firm,
  participant-belongs-to-case, and caller authorization — none of which is
  this module). **No implementation ships in spec 007.** The module never
  depends on provider infrastructure.

### `ExtractionEnvelopeValidator` (`Services/ExtractionEnvelopeValidator.cs`)

```csharp
namespace DocumentExtraction.Services;

public static class ExtractionEnvelopeValidator
{
    public static ParseResult<ValidatedExtraction> Validate(ExtractionEnvelope? envelope);
}
```

The single deterministic, pure, fail-closed gate. Responsibility: envelope
well-formedness and attribution **presence** — nothing else. It never
authenticates a caller, never queries Tax Case Core, and proves no membership.
Rules, in fixed evaluation order (which also fixes error ordering, making
determinism mechanically testable):

1. **Null envelope** → single error (`Envelope`), no exception.
2. **Presence/non-blankness** (null, empty, or whitespace-only all fail, each
   with its own `ExtractionFields` name; independent checks are all reported
   in one pass — one error per missing field, PRD AC-19):
   `DocumentId`, `FirmId`, `CaseId`, `ParticipantId`,
   `Provenance.SourceReference` (a null `Provenance` reports the
   `SourceReference` error — the reference is required for **every** kind).
3. **Kind**: absent (`null`) or not a defined `ExtractedDocumentKind` value →
   error (`Kind`) naming the kind field. This is where a casted T5/unknown
   kind dies (PRD AC-4).
4. **Envelope tax year**: absent → error; not exactly 2025 (per
   `SupportedExtractionTaxYears`) → error. Never coerced; no adjacent-year
   fallback (PRD AC-9/10).
5. **Payload/kind coherence** (PRD AC-13): `T4Slip` with null `T4Payload` →
   error (`Payload`); `GenericEvidence` with non-null `T4Payload` → error
   (`Payload`).
6. **T4-only, when kind is `T4Slip` and payload present:**
   - **Payload year presence**: `T4Payload.Year` null → error (`PayloadYear`)
     (PRD AC-11).
   - **Year coherence**: `T4Payload.Year != TaxYear` → error (`PayloadYear`),
     neither value coerced (PRD AC-12). Tax-year isolation only — no box
     inspection.
   - **Empty extraction** (PRD AC-14, mechanical definition): `Boxes` is null
     or empty **and** `EmployerName`, `EmployerAddress`, `EmployeeName`,
     `EmployeeAddress` are each null/empty/whitespace → error
     (`PayloadContent`). No judgement about box codes or values — a payload
     with one box of garbage passes the envelope gate (AC-21); the engine
     rejects it downstream.
7. **Confidence**: when `Provenance.Confidence` has a value outside
   `[0m, 1m]` inclusive → error (`Confidence`). `0m`, `1m`, and `null` are
   accepted; low confidence never rejects (PRD AC-15/16).
8. **Success**: constructs the `ValidatedExtraction` carrying the trimmed?
   — **no**: carrying the references **exactly as supplied** (opaque,
   caller-supplied; the contract never normalizes them) and the payload
   **reference-identical** (same `T4RawInput` instance — never cloned,
   reordered, or deduplicated, so duplicate box codes survive, PRD AC-18).

Error messages are fixed template strings containing **no caller value and no
payload value** (e.g. `"CaseId is required and must be non-blank."`).

Applicable CLAUDE.md domain rules and how they land here: **per-tax-year
isolation** → rules 4 and 6 (fail closed, no fallback); **SIN handling** →
omission contract above (no full SIN can be emitted because no caller/payload
value is ever emitted); **decimal for money math** → not applicable, no money
math exists here, and `Confidence` (metadata, not currency) is `decimal` by
human decision; **no hardcoded CRA line numbers** → module contains none and
never reads mapping resources.

### `SupportedExtractionTaxYears` (`Services/SupportedExtractionTaxYears.cs`)

```csharp
namespace DocumentExtraction.Services;

public static class SupportedExtractionTaxYears
{
    public static IReadOnlyList<int> All { get; } // [2025]
    public static bool IsSupported(int taxYear);  // taxYear == 2025
}
```

Module-local year policy (mirrors the `TaxCases.SupportedTaxYears` *pattern*
without referencing that project). A new supported year is a code change to
exactly one place — a scope change, never a silent widening. It deliberately
does **not** inspect `TaxLogicEngine` mapping resources (PRD requirement 12).

### Module instructions

`src/DocumentExtraction/CLAUDE.md` (approved boundary, public-contract
inventory, PII-omission rules, 2025 policy, forbidden dependencies) and a
pointer-only `src/DocumentExtraction/AGENTS.md`, following the spec 005/006
scope-split precedent. They are created in Wave 0 so every later task is bound
by them.

## Trade-offs

1. **One loose envelope + discriminator + validator vs. a polymorphic envelope
   hierarchy** (abstract `ExtractionEnvelope` with `T4SlipEnvelope` /
   `GenericEvidenceEnvelope` subclasses). Rejected: the envelope is untrusted
   upstream data, and a subclass hierarchy encodes "the kind and payload
   already cohere" into the type system — i.e., it delegates a fail-closed
   check to the untrusted adapter and makes the mismatch unrepresentable
   instead of **rejectable** (we must be able to receive and reject a
   generic-evidence envelope carrying a slip payload, PRD AC-13). A single
   loose class also matches the engine's own `T4RawInput` philosophy
   ("everything nullable/loose because it is unvalidated upstream data") and
   keeps every rejection in one deterministic validator.

2. **Static pure validator vs. an injectable `IExtractionEnvelopeValidator`
   instance service.** Rejected the instance form: the validator has no
   dependencies, no configuration, and no state — it is a pure function, and
   the DI seam this feature genuinely needs is the **ingress port**, which is
   already an interface the future application layer implements (and can wrap
   around the static validator). Adding a second interface today would be
   speculative surface area; extracting one later is a non-breaking addition.

3. **Referencing `TaxCases` to reuse `SafeDiagnosticText` (echo-and-redact
   diagnostics) vs. omission with no masking helper.** Rejected the reference —
   and this is also the human-reviewed PRD decision: diagnostics that *never
   carry* untrusted values are strictly safer than diagnostics that echo and
   then mask them (masking has to win every time; omission wins by
   construction), and it keeps this module's dependency set to exactly
   `TaxLogicEngine`, avoiding a lateral module coupling CLAUDE.md's
   modular-monolith rules discourage.

4. **A single typed `T4Payload` slot vs. an `object? Payload`.** Rejected
   `object`: with only one payload-bearing kind in scope, a typed
   `T4RawInput?` makes the payload/kind coherence check trivial and
   compile-safe, and adding a future `T5Payload` slot (deferred scope change)
   is an additive, non-breaking envelope change gated by a new enum value.
   `object` would invite runtime type sniffing and weaken AC-13's mechanics.

## Risks

- **PII leak through a forgotten output path** (record auto-`ToString`,
  serializer touching the payload, an error message interpolating a caller
  value). *Mitigation:* sealed classes (no record auto-`ToString`) with
  overridden structural-facts-only `ToString()`; `[JsonIgnore]` on the payload
  and every caller-controlled string; fixed-template error messages taking no
  value arguments; a mandatory PII matrix (SIN `123456789`, `123-456-789`,
  `123 456 789` planted in Box 12, identity fields, and every caller
  reference) asserted absent from every `ToString()`/error/JSON output in both
  the contract wave and the end-to-end wave.
- **Wrong-year or year-incoherent extraction entering a 2025 case.**
  *Mitigation:* envelope year must equal 2025 and `T4RawInput.Year` must be
  present and equal it; separate negative tests for absent envelope year,
  2024/2026 envelope years, absent payload year, and mismatch; no coercion
  path exists in the code (the validator never writes a year).
- **The contract being mistaken for authorization** (treating a validated
  extraction as proof the participant belongs to the case/firm).
  *Mitigation:* presence-only validation is stated in the port's and
  validator's XML docs and the module CLAUDE.md; the PRD, architecture, and
  verification record all name membership/authorization as the future
  application workflow's job; no Tax Case Core query exists to create the
  illusion.
- **Scope creep into parsing** (a helpful validator starting to inspect box
  values, normalize SINs, or read mapping configs — i.e., extraction becoming
  tax truth). *Mitigation:* boundary tests scan the module source/assembly for
  forbidden tokens (CRA line literals, mapping-resource names,
  `RecipientIdentifier` construction, `T4SlipParser`/`T4LineMapper` usage,
  clock/I-O/network APIs) exactly as spec 006's T09 did; AC-21 pins that an
  engine-rejectable box value still passes the envelope gate.
- **Dependency drift** (someone adds a `TaxCases` or infrastructure
  reference). *Mitigation:* a boundary test asserts the csproj reference set
  is exactly `TaxLogicEngine`; AC-29 makes it a release gate; the reverse
  direction (engine → this module) is asserted absent as well.
- **CRA compliance:** this module asserts **no CRA fact** — its only tax
  semantics are the 2025 year policy and the reuse of already-verified T4
  structures as an opaque payload. The risk of a stale/incorrect CRA claim is
  therefore delegated to the specs that own those claims (003/004) and to the
  future T4 Case Processor, which must verify its own facts against canada.ca.
