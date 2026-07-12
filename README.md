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

Mappings are data-driven: box values map to CRA line numbers via the JSON configs only — no line number is hardcoded in C#.

## Project structure

```plaintext
TaxAI.sln                         Solution (two projects)
src/
  TaxLogicEngine/                 Production class library (net8.0)
    Models/                       Slips, raw inputs, parse results, line maps
    Services/                     Parsers, line mappers, providers, rounding
    Mappings/                     JSON box->line configs + rationale README
tests/
  TaxLogicEngine.Tests/           xUnit test suite
specs/                            Per-feature PRDs, architecture, task plans
```

## Build and test

```bash
# Build
dotnet build TaxAI.sln

# Run all tests
dotnet test TaxAI.sln

# Run a single test
dotnet test tests/TaxLogicEngine.Tests --filter "FullyQualifiedName~TestMethodName"
```
