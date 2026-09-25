# Spec 015 verification — T4 and medical document extraction

## Result

**Final gate pending.** T07 adds cross-module verification of T01–T06. Its
synthetic PII probes use deliberately non-real values. Raw source bytes are
excluded from the structured-surface claim and remain confined to the encrypted
source object.

### Authorized T06 guard correction

The human explicitly authorized correcting
`tests/TaxAI.Portal.Tests/Extraction/PreservedSurfaceTests.cs` after the first
full gate found that `NoT07VerificationSurfaceWasIntroduced` forbade this
T07-required document. The replacement checks the committed T06 merge tree at
`33ca9186d7f1915715f5d3930b2eda7fea379769`, retaining both historical assertions:
no verification document and no `CrossModule`-named test file existed in T06.
The Git command must succeed and the tree must contain T06's route tests, so an
empty result cannot silently pass. All product-surface assertions are unchanged.
`tasks.md` and all test floors remain unchanged.

## Evidence index

| ID | Executable evidence |
|---|---|
| E01 | `T4SlipParserEmployeeCopyTests` and the employee-copy cases in `T4CaseProcessorTests` prove the T01 parser supersession and unchanged engine chain. |
| E02 | `ExtractionWorkflow.Tests/BoundaryTests.cs`, `Models/ResultShapeTests.cs`, `Models/SinInvariantTests.cs`, and `DocumentIntelligence.Tests` prove the closed contracts, deterministic adapter, no new tax authority, and package/network boundaries. |
| E03 | `SqlExtractionStoreTests`, `ExtractionMigrationTests`, `ExtractionSchemaTests`, `PiiSchemaTests`, and the persistence boundary/upgrade tests prove append-only, firm-scoped, additive relational storage. |
| E04 | `ExtractionWorkflow.Tests/Ingest/*` proves the real ingress, case year, engine delegation, fail-closed provider handling, medical validation, duplicate policy, scope, source lifetime, and ingest PII rules. |
| E05 | `ExtractionWorkflow.Tests/Review/*` proves corrections, overrides, duplicate decisions, exact-version acceptance, one-time finalization, immutable review creation, and review PII rules. |
| E06 | `TaxAI.Portal.Tests/Extraction/*` proves all eight routes, antiforgery/authentication, four-part content reading, closed browser projections, startup refusal, and preserved surfaces. |
| E07 | `DocumentExtractionPersistenceTests` drives real intake, ingest, review, `SqlIntakeStore`, `SqlExtractionStore`, and `SqlReviewRecordRepository` against a relational database after real migrations, including T4 plus medical evidence, exactly one review association, replay refusal, and a whole-database planted-value sweep. |
| E08 | `DocumentExtractionAcceptanceTests` drives both real workflows through the eight-route portal composition with the shipped deterministic provider and Development encrypted reader, including correction, duplicate resolution, classification disagreement/override, acceptance, finalization, and neutral medical rendering. A positively checked runtime listener asserts zero HTTP/DNS/socket egress attempts during the journey; the final verification runs in the restricted-network sandbox. |
| E09 | `DocumentExtractionSecurityAcceptanceTests` compares foreign and missing resources over all eight routes and proves identical status, empty body, headers, and no mutation. |
| E10 | `DocumentExtractionPiiSweepTests` captures browser models, HTML, URLs, cookies/headers, SQL-independent structured state, audits, logs, metrics, traces, exceptions, and review output with planted SIN/source/provider/patient values; provider/patient display names occur only on the accountant medical surface. |
| E11 | `pwsh ./scripts/Invoke-SolutionTests.ps1` is the authoritative gate. The final committed-tree result is recorded below after execution. |
| E12 | `PreservedSurfaceTests`, portal upgrade/boundary tests, repository verification tests, and the historical T01 guards prove the spec 012, spec 014, static-asset, solution-registration, and pre-existing contract boundaries remain intact. |

## Acceptance-criterion ledger

“Pass” means the criterion is satisfied by the executable evidence named in
that row; the evidence index gives the concrete suites and integration tests.

| Criterion | Result | Evidence |
|---|---|---|
| AC-1 — The declared ingress is implemented, not replaced | Pass | E04, E07, E08 |
| AC-2 — Medical documents reuse the existing kind and category | Pass | E02, E04, E08 |
| AC-3 — No new tax authority | Pass | E02, E04, E12 |
| AC-4 — No arithmetic or rounding | Pass | E01, E02, E04 |
| AC-5 — The engine chain is invoked, not reimplemented | Pass | E01, E04, E07 |
| AC-6 — Engine errors surface verbatim | Pass | E01, E04 |
| AC-7 — The year comes from the case | Pass | E04, E07 |
| AC-8 — Document year mismatch fails closed | Pass | E04 |
| AC-9 — Adjacent-year fallback does not exist | Pass | E01, E04 |
| AC-10 — No new year authority | Pass | E02, E04 |
| AC-11 — Firm scope is server-derived | Pass | E04, E06, E09 |
| AC-12 — Foreign-firm document is a plain 404 | Pass | E04, E09 |
| AC-13 — Revoked session fails before any work | Pass | E06 |
| AC-14 — Inactive membership fails before any work | Pass | E06 |
| AC-15 — No firm-omitting taxpayer operation | Pass | E03, E04, E06 |
| AC-16 — No anonymous or client-initiated extraction | Pass | E06 |
| AC-17 — No background initiator | Pass | E04, E06 |
| AC-18 — Provider failure fails closed | Pass | E04 |
| AC-19 — Provider timeout fails closed | Pass | E04 |
| AC-20 — Malformed provider response fails closed | Pass | E02, E04 |
| AC-21 — Unclassified document produces no tax data | Pass | E02, E04 |
| AC-22 — Confidence never gates | Pass | E02, E04, E08 |
| AC-23 — Out-of-range confidence fails closed | Pass | E04 |
| AC-24 — Duplicate box codes still reach the parser | Pass | E02, E04 |
| AC-25 — Determinism | Pass | E01, E02, E04 |
| AC-26 — No clock, environment, or current-user read | Pass | E02, E04 |
| AC-27 — No SIN in any structured surface | Pass | E02, E03, E04, E05, E07, E10 |
| AC-28 — No new SIN representation | Pass | E02, E03, E06 |
| AC-29 — Raw payload is omitted from output | Pass | E02, E04, E06, E10 |
| AC-30 — No source byte escapes | Pass | E04, E07, E10 |
| AC-31 — No content-serving route | Pass | E06, E12 |
| AC-32 — Fixed error messages | Pass | E04, E06 |
| AC-33 — Append-only history | Pass | E03, E04, E05, E07 |
| AC-34 — Audit is structural only | Pass | E03, E04, E05, E07, E10 |
| AC-35 — No provider SDK ships | Pass | E02, E11 |
| AC-36 — Deterministic modules stay clean | Pass | E02, E12 |
| AC-37 — Persistence remains the only EF project; portal the only host | Pass | E02, E03, E06, E12 |
| AC-38 — No production provider ships | Pass | E02, E06 |
| AC-39 — Startup fails closed on invalid provider configuration | Pass | E02, E06 |
| AC-40 — The review route is untouched | Pass | E06, E12 |
| AC-41 — Spec 014 surfaces are untouched | Pass | E06, E12 |
| AC-42 — Schema change is additive only | Pass | E03 |
| AC-43 — Existing contracts changed only as enumerated | Pass | E02, E03, E06, E12 |
| AC-44 — Full suite green | Pending | E11 |
| AC-45 — No unauthorized tax behaviour changed | Pass | E01, E11, E12 |
| AC-45a — Downstream Box 54 rejection superseded by T01 | Pass | E01 |
| AC-46 — Solution assertions stay open | Pass | E02, E12 |
| AC-47 — Static asset middleware unchanged | Pass | E06, E12 |
| AC-48 — The write-only store is unchanged | Pass | E06, E12 |
| AC-49 — The read port requires full four-part scope | Pass | E04, E06 |
| AC-50 — A content key alone reads nothing | Pass | E06 |
| AC-51 — Only the extraction workflow can resolve the read port | Pass | E06 |
| AC-52 — A read requires a live firm-scoped accountant request | Pass | E06, E09 |
| AC-53 — The source-byte invariant holds verbatim | Pass | E04, E07, E10 |
| AC-54 — No taxpayer-content network egress | Pass | E02, E08 |
| AC-55 — No provider payload is stored or logged | Pass | E02, E04, E07, E10 |
| AC-56 — The adapter project carries no SDK | Pass | E02 |
| AC-57 — Box 12 reaches the parser in memory | Pass | E01, E04 |
| AC-58 — The raw SIN outlives nothing | Pass | E02, E04, E07, E10 |
| AC-59 — No route accepts a SIN | Pass | E06 |
| AC-60 — A rejected SIN blocks acceptance | Pass | E04, E05 |
| AC-61 — Correction re-extracts instead of reusing stored payload | Pass | E05, E08 |
| AC-62 — No SIN-presence signal | Pass | E02, E03, E06, E10 |
| AC-63 — Valid employee copy without Box 54 is accepted | Pass | E01, E04, E07 |
| AC-64 — No fake Box 54 is required or invented | Pass | E01, E04 |
| AC-65 — A 2025 T4 without Box 45 fails through the engine | Pass | E01, E04 |
| AC-66 — Missing addresses do not block review and are not invented | Pass | E01, E04 |
| AC-67 — Box 12 and tax-year rule are unchanged | Pass | E01, E04 |
| AC-68 — The supersession is recorded | Pass | E01, T01 completion marker and `src/TaxLogicEngine/CLAUDE.md` |
| AC-69 — T4-only review contracts are unchanged | Pass | E05, E06, E12 |
| AC-70 — Medical model is a closed field set | Pass | E02, E06 |
| AC-71 — Prohibited medical content is unrepresentable | Pass | E02, E06 |
| AC-72 — No eligibility or claim assertion | Pass | E04, E06, E08 |
| AC-73 — No medical tax computation exists | Pass | E02, E04 |
| AC-74 — Amounts and dates stay as printed | Pass | E02, E03, E04, E08 |
| AC-75 — No new mapping config or slip type | Pass | E02, E12 |
| AC-76 — Medical fields render only on the new surface | Pass | E06, E08, E10 |
| AC-77 — Spec 012 allowlist is unchanged | Pass | E06, E12 |
| AC-78 — New surface has its own closed allowlist | Pass | E06 |
| AC-79 — Provider and patient names never leave accountant surface | Pass | E05, E07, E08, E10 |
| AC-80 — Medical fields persist as immutable versions | Pass | E03, E04, E07 |
| AC-81 — No threshold exists anywhere | Pass | E02, E04, E06 |
| AC-82 — Confidence does not gate acceptance | Pass | E04, E05, E08 |
| AC-83 — Confidence is never described as accuracy | Pass | E02, E06 |
| AC-84 — Confidence never auto-accepts | Pass | E04, E05 |
| AC-85 — T4 validation is the engine's and surfaces per field | Pass | E01, E04 |
| AC-86 — No new T4 rule exists | Pass | E01, E02, E04 |
| AC-87 — Missing medical date or amount blocks acceptance | Pass | E04, E05 |
| AC-88 — Missing optional medical field neither blocks nor decides eligibility | Pass | E04, E05 |
| AC-89 — No rule forces acceptance of an engine rejection | Pass | E04, E05 |
| AC-90 — Byte-identical duplicate prevention is unchanged | Pass | E06, E12 |
| AC-91 — Semantic duplicates are firm- and case-scoped | Pass | E04, E09 |
| AC-92 — Duplicate identity never uses SIN | Pass | E04, E10 |
| AC-93 — T4 duplicate comparison ignores Box 54 | Pass | E04 |
| AC-94 — Distinct same-employer T4s are not duplicates | Pass | E04 |
| AC-95 — Medical duplicate comparison uses only approved fields | Pass | E04 |
| AC-96 — A duplicate is never auto-resolved | Pass | E04, E05, E08 |
| AC-97 — Duplicate relationships are opaque and immutable | Pass | E03, E04, E05, E09 |
| AC-98 — Correction vocabulary is closed | Pass | E05, E06 |
| AC-99 — Prohibited corrections are refused | Pass | E03, E05, E06 |
| AC-100 — Each correction creates a complete immutable version | Pass | E03, E05, E08 |
| AC-101 — No SIN in a correction record | Pass | E03, E05, E10 |
| AC-102 — Correction re-runs the whole chain | Pass | E05, E08 |
| AC-103 — Correction cannot bypass a rejection | Pass | E05 |
| AC-104 — Correction requires explicit accountant action | Pass | E05, E06, E08 |
| AC-105 — No record before case-level acceptance | Pass | E05, E07, E08 |
| AC-106 — Case acceptance creates exactly one versioned record | Pass | E05, E07, E08 |
| AC-107 — Only latest accepted document versions are assembled | Pass | E05, E07, E08 |
| AC-108 — Assembler invariants are unrelaxed | Pass | E05, E07 |
| AC-109 — No post-finalization workflow exists | Pass | E05, E07 |
| AC-110 — Review route gains no latest selection | Pass | E06, E12 |
| AC-111 — Medical evidence never enters T4 record | Pass | E05, E07, E08, E10 |
| AC-112 — New taxpayer data inherits seven-year exclusive boundary | Pass | E03, E04, E07 |
| AC-113 — Retention is never described as a CRA requirement | Pass | E02, E03, E06 |
| AC-114 — Production use remains blocked | Pass | E06 |
| AC-115 — Override targets are exactly the two supported paths | Pass | E05, E06 |
| AC-116 — Disagreement is surfaced and neither silently wins | Pass | E05, E06, E08 |
| AC-117 — Override is immutable, scoped, and retains original | Pass | E03, E05, E08 |
| AC-118 — Override bypasses no validation | Pass | E05 |
| AC-119 — `Other` never becomes tax input by default | Pass | E04, E05, E06 |

## T07 cross-module conclusions

- The relational proof applies the repository migrations and uses all three SQL
  adapters together; the portal proof uses the real composition, workflows,
  deterministic provider, encrypted Development reader, authorization, and
  antiforgery over HTTP. Each substitutes only the boundary the other proves.
- T4 corrections, semantic duplicate decisions, classification disagreement,
  exact-version acceptance, medical neutrality, review construction, and the
  one-time intake association survive the complete journeys.
- Foreign and missing selectors are byte-indistinguishable for every extraction
  route. The planted-value sweep covers every structured surface available to
  the composed application and the relational database.
- No production provider, SDK, network transport, deployment authorization,
  background initiator, content route, tax rule, arithmetic, or retention path
  was introduced by verification.
