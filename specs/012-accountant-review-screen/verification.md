# Verification 012 — Accountant Review Screen

## Result

Atomic T01 implements one authenticated, firm-scoped, read-only accountant
review route. The route projects one immutable persisted review version into a
closed server-rendered DTO and preserves the existing deployment refusal.

## Acceptance evidence

| Criterion | Evidence |
|---|---|
| 1 | `ShellPageTests` proves `/reviews/{recordId:guid}` is the only additive route, GET-only, and authorization-required. `ReviewPageTests` proves the exact firm-scoped policy behavior. |
| 2 | `ReviewPageTests.AuthorizedReviewUsesTheServerFirmAndRendersOnlyTheAllowlist` records the one `FindAsync` call, trusted session firm, route selector, and request cancellation path. |
| 3 | `ReviewPageTests.MissingAndForeignRecordsReturnTheSameSafeNotFound` compares status, headers, and empty response bytes for missing and foreign-firm lookups. |
| 4 | Review integration cases prove malformed selectors, anonymous callers, inactive users, inactive memberships, and identity-store failures do not reach the repository. |
| 5 | The successful-render case proves the exact approved fields, excluded sentinels, and persisted order. The DTO reflection case proves the closed property vocabulary and forbidden type absence. |
| 6 | Stored display names and codes containing markup are asserted encoded and absent in raw form. |
| 7 | Positive, negative, zero, and four-decimal values are rendered with invariant formatting. Boundary scans retain the arithmetic and rounding prohibition. |
| 8 | Dedicated cases prove explicit no-slip, no-box, no-line, no-source-box, and no-unmapped-code states. |
| 9 | Integration, reflection, source, response, logging, and exception cases prove no SIN representation, nine-digit run, forbidden identifier, or sensitive diagnostic reaches the browser or logs. |
| 10 | The repository double fails on `SaveAsync`; only GET is mapped and non-GET requests do not look up or mutate. |
| 11 | Two separately stored versions render only their own immutable package version. No “latest” query exists. |
| 12 | Project-structure and compiled-reference tests retain exactly four portal project references and the existing one package reference. No project, migration, model, or schema artifact changed. |
| 13 | Health, identity, session, membership revalidation, revocation, and startup-refusal suites pass in the complete Portal and solution runs. |
| 14 | Portal boundary/security suites retain exact prohibitions on parser, mapper, review assembler/decision, arithmetic, rounding, hard-coded CRA values, tax-year policy, client stack, direct EF, and direct CaseReview use. Persisted codes and tax year may only be carried by `ReviewEndpoints`. |
| 15 | The repository PowerShell execution gate ran all eight registered test projects with 3,213 passed, 0 failed, and 0 skipped; every floor was met. |
| 16 | Spec 013 baseline files are unchanged. Upgrade tests retain the historical route/authentication and internal-contract subsets while separately asserting the one additive route, exact internal endpoint surface, and revised fixed root body. |

## Browser projection

Allowed: tax year, immutable package version, slip type, taxpayer and employer
display names, boxes and values, tax lines, source-box references, and unmapped
codes.

Excluded: SIN in every representation; date of birth; addresses; email
addresses; phone numbers; account numbers; provider identity values; firm,
user, membership, session, and persistence identifiers; and non-display
timestamps. The opaque route key is neither displayed nor part of the DTO and
is never tenant identity. The trusted firm comes only from the validated
server-side session.

## Executed gates

All commands ran from the repository root on 2026-07-29:

```text
dotnet test tests/TaxAI.Portal.Tests/TaxAI.Portal.Tests.csproj --no-restore
Passed: 431, Failed: 0, Skipped: 0

dotnet test TaxAI.sln --no-restore
Passed: 3,213, Failed: 0, Skipped: 0

pwsh ./scripts/Invoke-SolutionTests.ps1
8/8 projects executed; Passed: 3,213, Failed: 0, Skipped: 0

dotnet publish src/TaxAI.Portal/TaxAI.Portal.csproj -c Release --no-restore
Succeeded

git diff --check
Succeeded
```

The Portal floor is raised from 411 to the verified total of 431 and is
established by spec 012 T01. No other floor is changed.

Release publish is build evidence only. The existing non-Development startup
refusal remains authoritative; this feature does not authorize deployment.
