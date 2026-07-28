# TaxAI

TaxAI is the foundation of a Canadian tax filing assistant, built as a structured modular monolith. Its core is **TaxLogicEngine**, a .NET 8.0 class library of deterministic, provider-independent Canadian tax logic.

This README describes only what is implemented today. The forward roadmap lives in [NEXT.md](NEXT.md); repository and agent policy live in [CLAUDE.md](CLAUDE.md) and [AGENTS.md](AGENTS.md).

## Implemented capabilities

- **T5 slip parsing** — validation and parsing of T5 (investment income) raw input into a typed `T5Slip`.
- **T4 slip parsing** — validation and parsing of T4 (employment income) raw input into a typed `T4Slip`.
- **T5 box → CRA line mapping** — `TaxLineEntry` generation driven by the embedded JSON config `Mappings/T5LineMap.2025.json` (tax year 2025).
- **T4 box → CRA line mapping** — `TaxLineEntry` generation driven by the embedded JSON config `Mappings/T4LineMap.2025.json` (tax year 2025; some boxes deliberately deferred, see `src/TaxLogicEngine/Mappings/README.md`).
- **Currency rounding** — `decimal`-only math, rounded half-up to cents via `MoneyRounding`.
- **SIN masking** — `RecipientIdentifier` masks a SIN to its last 3 digits in every output.
- **Per-tax-year isolation** — mapping configs are year-scoped; a year without config fails closed (no adjacent-year fallback).
- **Tax case core** — an immutable 2025 engagement aggregate with firm/year/participant isolation, participant-scoped evidence and intake, derived readiness, and deterministic change records.
- **Document extraction contract** — the deterministic, provider-independent boundary between future OCR/LLM adapters and the tax domain. Extraction output is untrusted input, never a source of tax truth. No adapter implementation ships.
- **T4 case processing** — a deterministic coordinator that drives one already-validated T4 extraction through the engine and returns the parsed slip plus engine-produced CRA line entries. It invents no tax truth of its own.
- **Accountant review package** — an immutable review boundary assembled from successful T4 processing results, with one recorded human decision and a fail-closed approval rule for unmapped codes.
- **Portal host** — a runnable ASP.NET Core application that composes the modules above through a single dependency-injection composition root. It serves a health endpoint and a static shell page, and nothing else.

Mappings are data-driven: box values map to CRA line numbers via the JSON configs only — no line number is hardcoded in C#.

### What the portal deliberately is not

The portal is a foundation, not a product surface. It renders **no taxpayer data**, accepts **no user input**, and has no authentication, user accounts, document upload, persistence, deployment configuration, or OCR/LLM integration. Every one of those is a separately specified feature, gated behind the open questions recorded in `src/TaxAI.Portal/CLAUDE.md`.

## Project structure

```plaintext
TaxAI.sln                         Solution
src/
  TaxLogicEngine/                 Deterministic tax engine (net8.0)
    Models/                       Slips, raw inputs, parse results, line maps
    Services/                     Parsers, line mappers, providers, rounding
    Mappings/                     JSON box->line configs + rationale README
  TaxCases/                       Tax case core — the engagement aggregate
  DocumentExtraction/             Untrusted-extraction ingestion contract
  CaseProcessing/                 T4 case processor (extraction -> slip + lines)
  CaseReview/                     Accountant review package and sign-off
  TaxAI.Portal/                   ASP.NET Core host and composition root
tests/
  TaxLogicEngine.Tests/           xUnit test suite
  TaxCases.Tests/                 xUnit test suite
  DocumentExtraction.Tests/       xUnit test suite
  CaseProcessing.Tests/           xUnit test suite
  CaseReview.Tests/               xUnit test suite
  TaxAI.Portal.Tests/             xUnit test suite
specs/                            Per-feature PRDs, architecture, task plans
```

Each module under `src/` carries its own `CLAUDE.md` describing its boundary.

## Build and test

```bash
# Build
dotnet build TaxAI.sln

# Run the full suite through the execution gate (preferred)
pwsh ./scripts/Invoke-SolutionTests.ps1

# Run all tests directly
dotnet test TaxAI.sln

# Run a single test
dotnet test tests/TaxLogicEngine.Tests --filter "FullyQualifiedName~TestMethodName"
```

`dotnet test` returns exit code 0 even when an entire test assembly is skipped,
so a green result is not by itself proof that the suite ran.
`scripts/Invoke-SolutionTests.ps1` fails unless every project listed in
`scripts/test-projects.json` actually ran, reported no failures, skipped nothing,
and met its recorded minimum test count.

## Run the application

```bash
dotnet run --project src/TaxAI.Portal
```

The host serves exactly two routes: `GET /health`, returning a fixed literal, and `GET /`, returning a static shell page.
