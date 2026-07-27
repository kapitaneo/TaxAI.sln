# Architecture: Accountant Review Package

Implements [prd.md](prd.md) (spec 009), **feature class `tax-domain`**, tax year
**2025 only**. This plan introduces no CRA line number, no box-to-line rule, and
no new mapping fact. It consumes only the successful output of spec 008 and
preserves that output without recomputing tax truth.

## Approach

Create one deterministic module, `src/CaseReview/`, with two responsibilities:

1. assemble one immutable review package from one or more
   `ProcessedT4Extraction` values; and
2. record exactly one immutable human decision on that exact package.

The module is a downstream review boundary, not another tax engine. It does not
parse a slip, obtain a mapping config, map a box, aggregate money, round an
amount, authorize a reviewer, mutate a `TaxCase`, persist anything, or render a
document.

The new module is required by the existing boundaries:

- `CaseProcessing` owns one processed extraction and its public contract is
  closed by spec 008.
- `TaxCases` owns engagement state and may depend only on `TaxLogicEngine`.
- `TaxLogicEngine` must not learn about workflow modules.
- `DocumentExtraction` carries untrusted input and is not referenced directly.

The dependency graph is therefore:

```text
CaseReview ──┬──► CaseProcessing ──┬──► DocumentExtraction
             │                     └──► TaxLogicEngine
             └──► TaxLogicEngine

TaxCases                 (no reference in either direction)
```

The two production references from `CaseReview` are exactly:

- `CaseReview → CaseProcessing`, for `ProcessedT4Extraction`; and
- `CaseReview → TaxLogicEngine`, for `T4Slip`, `TaxLineEntry`,
  `T4SlipBoxAmounts`, `ParseResult<T>`, and `ValidationError`.

There is no direct `CaseReview → DocumentExtraction` reference. No existing
project references `CaseReview`.

### Project-area impact

| Area | Change |
|---|---|
| `CaseReview/Models` | new immutable package, reviewed-content, decision, sign-off, enum, and fixed-field types |
| `CaseReview/Services` | new assembler and 2025-only supported-year policy |
| `TaxLogicEngine/Models` | reused unchanged |
| `TaxLogicEngine/Services` | only `T4SlipBoxAmounts.Enumerate` is called; everything else is reused upstream through spec 008 |
| `Mappings/` | untouched; no new or changed JSON |
| `Prompts/` | untouched; no OCR or LLM behavior |
| Existing business modules | untouched; no reverse reference |

### Assembly pipeline and fixed error order

`ReviewPackageAssembler.Assemble` materializes the input exactly once and
preserves enumeration order. It follows these stages:

| Stage | Rule | Failure behaviour |
|---|---|---|
| 1 | Input collection is non-null | one `Extractions` error, return |
| 2 | Collection is non-empty | one `Extractions` error, return |
| 3 | No element is null | one `Extraction` error, return |
| 4 | Inspect all elements without normalization | accumulate at most one error per rule in the fixed order below |
| 5 | If stage 4 produced errors | return them in their fixed order |
| 6 | Build reviewed slips in supplied order | success with no sign-off |

Stage 4 evaluates every rule, then emits errors in this exact order:

1. `FirmId` mismatch;
2. `CaseId` mismatch;
3. `ParticipantId` mismatch;
4. `TaxYear` mismatch;
5. any unsupported `TaxYear`;
6. duplicate `DocumentId`.

Comparisons are ordinal and exact. Nothing is trimmed, normalized, case-folded,
coerced, sorted, deduplicated by content, or filtered. Each rule produces at
most one fixed-message error regardless of how many elements violate it. The
supported-year predicate is consulted for every non-null element, including
paths that also contain a year mismatch; this makes the AC-43 structural guard
unconditional.

After validation, each `ReviewedSlip` is built as follows:

- `DocumentId`, `SourceReference`, and `TaxYear` are echoed exactly.
- `Slip` is the exact same `T4Slip` reference.
- line entries are copied value-for-value, preserving order, line number,
  amount, and source-box-code order;
- populated box amounts are materialized once, in the exact order returned by
  `T4SlipBoxAmounts.Enumerate`;
- `UnmappedBoxCodes` are copied in their existing order, unfiltered and
  unclassified.

The copies are defensive snapshots, not recomputation. Copying a
`TaxLineEntry` and its `SourceBoxCodes` only freezes the existing values; it
does not map, sum, round, label, or otherwise reinterpret them.

### Sign-off pipeline and fixed error order

`AccountantReviewPackage.RecordDecision` returns a replacement package and
never mutates the original.

1. A null descriptor returns one `Decision` error.
2. A package that already has a sign-off returns one `SignOff` error. The second
   descriptor is not evaluated because no second decision can be accepted.
3. Otherwise, independent descriptor errors accumulate in this exact order:
   undefined `Decision`, blank `ReviewerReference`, blank `DecidedAtReference`,
   missing required `Note`, then missing/negative
   `UnmappedBoxAcknowledgement`.
4. On zero errors, create one `ReviewSignOff` and return a replacement package
   that reuses the original package's immutable reviewed-content snapshot.

`bool? UnmappedBoxesAcknowledged` deliberately has three states:

- `null`: acknowledgement absent;
- `false`: acknowledgement explicitly negative;
- `true`: acknowledgement affirmative.

When the decision is `Approved` and any reviewed slip has at least one unmapped
code, only `true` succeeds. `null` and `false` are separate fail-closed rules
with separate fixed messages. `ChangesRequested` and `Rejected` do not require
the acknowledgement. A package with no unmapped code does not require it.

The accepted nullable value is recorded exactly as supplied. Thus an optional
acknowledgement is never normalized or fabricated. This single property is the
whole acknowledgement mechanism: there is no per-slip or per-code
acknowledgement, no per-code disposition, and no mapped-amount dispute field.

## Data model

There is **no JSON mapping config**, no mapping-resource read, and no change to
`Mappings/T4LineMap.2025.json`.

All public instance types are sealed classes, never records. Every one owns a
hand-written `ToString()` containing fixed structural facts only. Output types
have internal constructors so only this assembly can create review state.
Input strings remain readable programmatically but carry `[JsonIgnore]`.

All accepted collections are copied to private arrays and exposed as
`IReadOnlyList<T>` snapshots. Equality is explicit and ordinal; no generated
record formatter or record equality is used.

### Exact Wave 0 public contract

The following is the complete public-surface manifest. Names, namespaces,
nullability, enum values, constructors, properties, attributes, and method
signatures are fixed. Wave 0 compiles all signatures; later waves replace only
the designated `NotImplementedException` bodies.

```csharp
namespace CaseReview.Models;

public enum SignOffDecision
{
    Approved = 1,
    ChangesRequested = 2,
    Rejected = 3
}

public sealed class ReviewedBoxAmount :
    System.IEquatable<ReviewedBoxAmount>
{
    internal ReviewedBoxAmount(string boxCode, decimal amount);

    [System.Text.Json.Serialization.JsonIgnore]
    public string BoxCode { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public decimal Amount { get; }

    public bool Equals(ReviewedBoxAmount? other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public override string ToString();
}

public sealed class ReviewedSlip :
    System.IEquatable<ReviewedSlip>
{
    internal ReviewedSlip(
        string documentId,
        int taxYear,
        string sourceReference,
        TaxLogicEngine.Models.T4Slip slip,
        System.Collections.Generic.IReadOnlyList<
            TaxLogicEngine.Models.TaxLineEntry> lineEntries,
        System.Collections.Generic.IReadOnlyList<ReviewedBoxAmount> boxAmounts,
        System.Collections.Generic.IReadOnlyList<string> unmappedBoxCodes);

    [System.Text.Json.Serialization.JsonIgnore]
    public string DocumentId { get; }

    public int TaxYear { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string SourceReference { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public TaxLogicEngine.Models.T4Slip Slip { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public System.Collections.Generic.IReadOnlyList<
        TaxLogicEngine.Models.TaxLineEntry> LineEntries { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public System.Collections.Generic.IReadOnlyList<ReviewedBoxAmount>
        BoxAmounts { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public System.Collections.Generic.IReadOnlyList<string>
        UnmappedBoxCodes { get; }

    public bool Equals(ReviewedSlip? other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public override string ToString();
}

public sealed class ReviewDecisionDescriptor
{
    public ReviewDecisionDescriptor(
        SignOffDecision decision,
        string? reviewerReference,
        string? decidedAtReference,
        string? note,
        bool? unmappedBoxesAcknowledged);

    public SignOffDecision Decision { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? ReviewerReference { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? DecidedAtReference { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? Note { get; }

    public bool? UnmappedBoxesAcknowledged { get; }

    public override string ToString();
}

public sealed class ReviewSignOff :
    System.IEquatable<ReviewSignOff>
{
    internal ReviewSignOff(
        SignOffDecision decision,
        string reviewerReference,
        string decidedAtReference,
        string? note,
        bool? unmappedBoxesAcknowledged);

    public SignOffDecision Decision { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string ReviewerReference { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string DecidedAtReference { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? Note { get; }

    public bool? UnmappedBoxesAcknowledged { get; }

    public bool Equals(ReviewSignOff? other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public override string ToString();
}

public sealed partial class AccountantReviewPackage :
    System.IEquatable<AccountantReviewPackage>
{
    internal AccountantReviewPackage(
        string firmId,
        string caseId,
        string participantId,
        int taxYear,
        System.Collections.Generic.IReadOnlyList<ReviewedSlip> reviewedSlips,
        ReviewSignOff? signOff);

    [System.Text.Json.Serialization.JsonIgnore]
    public string FirmId { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string CaseId { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string ParticipantId { get; }

    public int TaxYear { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public System.Collections.Generic.IReadOnlyList<ReviewedSlip>
        ReviewedSlips { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public ReviewSignOff? SignOff { get; }

    public TaxLogicEngine.Models.ParseResult<AccountantReviewPackage>
        RecordDecision(ReviewDecisionDescriptor? decision);

    public bool Equals(AccountantReviewPackage? other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public override string ToString();
}

public static class ReviewFields
{
    public const string Extractions = "Extractions";
    public const string Extraction = "Extraction";
    public const string FirmId = "FirmId";
    public const string CaseId = "CaseId";
    public const string ParticipantId = "ParticipantId";
    public const string TaxYear = "TaxYear";
    public const string DocumentId = "DocumentId";
    public const string Decision = "Decision";
    public const string ReviewerReference = "ReviewerReference";
    public const string DecidedAtReference = "DecidedAtReference";
    public const string Note = "Note";
    public const string SignOff = "SignOff";
    public const string UnmappedBoxAcknowledgement =
        "UnmappedBoxAcknowledgement";
}

namespace CaseReview.Services;

public static class SupportedReviewTaxYears
{
    public static System.Collections.Generic.IReadOnlyList<int> All { get; }
    public static bool IsSupported(int taxYear);
}

public static class ReviewPackageAssembler
{
    public static TaxLogicEngine.Models.ParseResult<
        CaseReview.Models.AccountantReviewPackage> Assemble(
            System.Collections.Generic.IReadOnlyList<
                CaseProcessing.Models.ProcessedT4Extraction?>? extractions);
}
```

`AccountantReviewPackage` is partial only to give assembly-owned core state and
decision-recording behaviour separate source-file ownership. It is one sealed
public type in the compiled contract.

`InternalsVisibleTo("CaseReview.Tests")` is declared only by the new
`CaseReview` project. It permits direct tests of proof-by-construction output
types. No `InternalsVisibleTo` grant is added to `CaseProcessing`; cross-year
and otherwise fabricated `ProcessedT4Extraction` values remain unavailable.

### Model semantics

#### `ReviewedBoxAmount`

A two-member snapshot of exactly one `(BoxCode, Amount)` tuple emitted by
`T4SlipBoxAmounts.Enumerate`. It contains no behavior and no public
constructor. Equality is ordinal on `BoxCode` and exact on `decimal Amount`.
`ToString()` names the type only; it emits neither member.

#### `ReviewedSlip`

One processed extraction as presented for review. It retains the exact
`T4Slip` reference and immutable snapshots of line entries, populated box
amounts, and unmapped codes. Equality uses:

- ordinal equality for `DocumentId` and `SourceReference`;
- exact equality for `TaxYear`;
- reference equality for `Slip`, binding the review to the exact parsed object;
- ordered value equality for line entries, source box codes, box amounts, and
  unmapped codes.

Its `ToString()` exposes tax year and counts only. Direct JSON exposes only
`TaxYear`; all reviewed content is omitted because direct serialization is a
defensive surface, not the future Portal API contract.

#### `ReviewDecisionDescriptor`

The only public constructible input type. It deliberately accepts nullable
strings, a nullable acknowledgement, and undefined enum casts so
`RecordDecision` can fail closed with `ValidationError` rather than constructors
throwing.

It performs no validation, trimming, parsing, or normalization. Its
`ToString()` reports only structural presence flags. The reviewer, decided-at,
and note strings are `[JsonIgnore]`.

#### `ReviewSignOff`

The accepted immutable decision. Its constructor is internal. It records the
decision, reviewer reference, decided-at reference, optional note, and nullable
acknowledgement exactly as accepted. Equality is ordinal and value-based.
`ToString()` emits only the decision kind and structural presence flags; it
never emits a caller string.

#### `AccountantReviewPackage`

The aggregate read model. The internal constructor and get-only properties make
invalid public construction impossible. The unsigned package has `SignOff ==
null`; a successful `RecordDecision` returns a replacement with one non-null
sign-off while preserving the same immutable reviewed-content snapshot.

Value equality compares attribution ordinally, tax year exactly, reviewed slips
in order, and sign-off by value. This makes AC-9 and AC-30 direct equality
assertions rather than field-by-field approximations.

Direct JSON exposes only `TaxYear`. Attribution, reviewed content, and sign-off
are `[JsonIgnore]`. `ToString()` emits tax year, reviewed-slip count,
unmapped-box count, and whether a sign-off exists; never an identifier, box
code, amount, line number, reviewer string, note, or SIN.

## Services & public API

### `SupportedReviewTaxYears`

```csharp
public static IReadOnlyList<int> All { get; } // exactly [2025]
public static bool IsSupported(int taxYear);
```

This is the only module-local supported-year policy. It does not read a clock,
another module's policy, or mapping resources. Widening the set is an explicit
future scope change.

### `ReviewPackageAssembler`

```csharp
public static ParseResult<AccountantReviewPackage> Assemble(
    IReadOnlyList<ProcessedT4Extraction?>? extractions);
```

The one assembly entry point. It is static, synchronous, pure, and
deterministic. A success contains an unsigned package and zero errors. A failure
contains at least one error and no package. It mutates no input and throws
nothing for the nullable collection shape in its contract.

The assembler invokes exactly one engine method:
`T4SlipBoxAmounts.Enumerate`. It never invokes `T4SlipParser`,
`T4LineMapper`, `IT4LineMapProvider`, `EmbeddedT4LineMapProvider`,
`MoneyRounding`, or a mapping resource.

### `AccountantReviewPackage.RecordDecision`

```csharp
public ParseResult<AccountantReviewPackage> RecordDecision(
    ReviewDecisionDescriptor? decision);
```

The one decision-recording operation. It is synchronous, pure, and
deterministic. It validates the descriptor and the package-level unmapped-code
gate, then returns a replacement. It does not authorize, persist, notify,
transition a case, emit an event, read a clock, or interpret the decided-at
reference.

### Fixed errors and PII boundary

Every module-originated error:

- uses one `ReviewFields` constant;
- uses a fixed non-interpolated message constant;
- contains no caller value, box code, amount, line number, year value, or SIN;
- preserves the fixed ordering declared above.

The module never logs. Its only reachable SIN is
`ReviewedSlip.Slip.Sin`, the existing masked `RecipientIdentifier`. It declares
no SIN property or type, constructs no `RecipientIdentifier`, and references no
masking helper.

### Domain rules from `CLAUDE.md`

| Rule | Architectural enforcement |
|---|---|
| Tax year is explicit and fail-closed | `SupportedReviewTaxYears` contains only 2025; every element is checked; no fallback or coercion |
| Currency uses `decimal` and half-up rounding | the module performs no arithmetic or rounding; every `decimal` is copied unchanged from upstream |
| Box-to-line mapping is data-driven | no mapper/config access; line entries arrive only on `ProcessedT4Extraction.Mapping` |
| Never echo a full SIN | exact `T4Slip` reference exposes only `RecipientIdentifier`; all rendering and JSON surfaces omit the slip and caller text |
| Dependencies point inward | exactly `CaseProcessing` and `TaxLogicEngine`; no reverse edge, `TaxCases`, or direct `DocumentExtraction` edge |

### Consolidated `[ASSUMED]` Class B decisions

The PRD's Class B assumptions are retained and finalized as follows:

- module and project names are `CaseReview`, `src/CaseReview/`, and
  `tests/CaseReview.Tests/`;
- the exact type and member names are those in the Wave 0 manifest above;
- acknowledgement is represented by nullable
  `bool? UnmappedBoxesAcknowledged`, because the approved ACs distinguish absent
  from explicitly negative;
- when acknowledgement is not required, any nullable value is accepted and
  recorded exactly rather than normalized or rejected;
- immutable collection state uses defensive ordered snapshots;
- package and output models use manual value equality rather than records;
- assembly accumulates independent coherence errors in the fixed order stated
  above, after structural collection failures short-circuit;
- sign-off accumulates independent descriptor errors after null/already-signed
  structural failures short-circuit;
- the package instance method owns decision recording; no separate sign-off
  service or mutable setter exists;
- no hash/fingerprint, change log, persistence record, or clock-backed timestamp
  is introduced.

## Trade-offs

### 1. New `CaseReview` module vs. extending `CaseProcessing`

Adding package assembly to `CaseProcessing` would avoid a project and one
dependency edge. Rejected: spec 008 fixes that module's job as processing one
validated extraction, and its public contract is closed. Multi-slip review,
human decision state, and a package-level gate are a distinct boundary. Keeping
them separate prevents the processor from acquiring workflow responsibilities.

### 2. New module vs. attaching sign-off to `TaxCases`

Putting the decision on `TaxCase` would make case lifecycle and review appear
integrated. Rejected: this feature is not authorized to prove membership,
authorize a reviewer, transition case status, or create a change record.
`TaxCases` also has a fixed sole dependency on `TaxLogicEngine`; consuming
`ProcessedT4Extraction` there would violate the compiled dependency contract.

### 3. Nullable boolean vs. a per-code acknowledgement model

A per-code record would offer richer audit detail. Rejected by the human's A1c
decision: acknowledgement is once per package, and per-code dispositions would
introduce new tax facts and new verification scope. A non-nullable boolean was
also rejected because AC-50 and AC-51 deliberately distinguish absent from
explicitly negative input.

### 4. Immutable replacement vs. mutating a package in place

Mutation would make the API smaller and avoid one allocation. Rejected: the
original reviewed artifact must remain unsigned and unchanged, a second decision
must be impossible, and approval must remain bound to exact content. A
replacement package makes those invariants observable and follows spec 006's
approved pattern.

### 5. Defensive snapshots vs. retaining upstream list references

Retaining `Mapping.Lines` and `UnmappedBoxCodes` by reference would be the
literal interpretation of "verbatim" and allocate less. Rejected: those
interfaces do not prove the underlying lists are immutable. Ordered
value-for-value snapshots preserve every observable value while preventing a
caller-held list from changing an already reviewed package.

### 6. One combined `AssembleAndSign` call vs. two operations

A combined call could guarantee no unsigned package escapes. Rejected: assembly
and human decision occur at different workflow moments, and the Portal needs to
render the unsigned package before a human can decide. The two-step immutable
contract binds sign-off without coupling review presentation to input assembly.

## Risks

### Correctness: review content drifts from engine output

Re-parsing, re-mapping, re-rounding, or re-aggregating would create a second tax
answer. Mitigation: the module consumes only `ProcessedT4Extraction`; preserves
the exact slip reference; copies line, amount, provenance, and unmapped values
without arithmetic; permits only `T4SlipBoxAmounts.Enumerate`; and has source
and assembly scans forbidding every tax-producing service and mapping resource.

### Correctness: acknowledgement gate can be bypassed

A caller could attempt approval with an absent or false acknowledgement, or put
unmapped codes on a later slip in a multi-slip package. Mitigation: the gate
scans all reviewed slips at record time, requires exactly `true`, has separate
negative tests for null and false, records the accepted affirmative value, and
has end-to-end coverage with several codes across several slips.

### Correctness: cross-person or cross-year assembly

Mixing attribution would put one person's income in another person's review.
Mitigation: ordinal firm/case/participant comparisons, exact year comparison,
unconditional policy checks on every element, duplicate document rejection, no
normalization, and one fail-closed AC per rule.

### Security/PII: caller strings and reviewer notes leak

The package carries names through `T4Slip` and arbitrary free text through the
decision descriptor. Mitigation: no logging; `[JsonIgnore]` on every caller
string and all nested reviewed content at the defensive JSON boundary;
hand-written structural `ToString()` on every public instance type; fixed
non-interpolated errors; and a planted-value matrix over success and every
rejection family.

### Immutability: the exact `T4Slip` reference contains nested read-only interfaces

The PRD requires reference identity for `T4Slip`; this module therefore cannot
deep-copy the slip. `T4Slip` is init-only, but an upstream implementation could
still supply a mutable object behind `OtherInformation`. Mitigation: box amounts,
line entries, provenance lists, and unmapped codes are snapshotted at assembly;
the package exposes no mutation API; tests prove caller list mutation does not
change snapshots. A tamper-evident persisted digest is explicitly deferred by
the PRD and must be designed with persistence.

### Tax-year policy drift between modules

All current modules support 2025 independently. If one widens without this
module, a processed future-year extraction must still fail here. Mitigation:
one local policy, every element checked, no inference from upstream success,
mapping resources, or a clock, plus source/IL verification of the unconditional
guard.

### Integration: parallel tasks collide

Package assembly and decision recording both concern one aggregate. Mitigation:
`AccountantReviewPackage` is partial: core state is in
`AccountantReviewPackage.cs`, and decision behavior is in
`AccountantReviewPackage.Decisions.cs`. Wave ownership keeps every sibling file
set disjoint, and all public signatures compile in Wave 0 before parallel work.
