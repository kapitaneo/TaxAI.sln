# CLAUDE.md — TaxLogicEngine (project-level)

Rules and inventory specific to the **TaxLogicEngine** project. Repository-wide policy — the modular-monolith decision, module boundary/dependency rules, global security/PII rules, and the spec→plan→implement→reviewer workflow — lives in the root [CLAUDE.md](../../CLAUDE.md) and is not repeated here. Read that file first; this one adds the tax-engine specifics.

## Responsibilities

TaxLogicEngine owns deterministic, provider-independent Canadian tax logic:

- **Slip parsing** — T4 and T5 raw-input validation and parsing.
- **Box→CRA-line mapping** — per-tax-year, driven exclusively by the JSON configs under `Mappings/`.
- **Currency rounding** — via the shared `MoneyRounding` helper.
- **SIN masking** — via the `RecipientIdentifier` value object.
- **Per-tax-year isolation** — year-scoped configs that fail closed.

## Domain rules (Canadian tax)

- **SIN masking implementation:** mask to the last 3 digits in all logs, errors, and output (the global PII rule in the root file). In this codebase that means using `RecipientIdentifier` — and beware that C# records auto-generate a `ToString()` that prints every property; override it.
- **Tax calculations are per tax-year.** Never mix brackets, rates, or limits across years. There is no "latest" or "current" year. If a slip's year has no config, fail closed — never fall back to an adjacent year.
- **All currency math uses `decimal`,** rounded half-up to cents (`MidpointRounding.AwayFromZero`) via `MoneyRounding.ToCents`. Never `double` or `float`. Never a bare `Math.Round`.
- **T4/T5 box values map to CRA line numbers via the JSON config only.** Never hardcode a line number in C#.
- **CRA filing deadlines:** T1 — April 30; self-employed — June 15.

## Key design decisions

- Field mappings are data-driven via JSON files in `Mappings/`, not hardcoded.
- CRA line numbers are the canonical identifiers linking form boxes to tax return entries.
- The mapping configs are embedded resources with fixed logical names that provider code and tests hardcode — **never change them**:
  - `TaxLogicEngine.Mappings.T4LineMap.2025.json`
  - `TaxLogicEngine.Mappings.T5LineMap.2025.json`

## Forbidden dependencies

Per the module-boundary rules in the root file: this project must stay deterministic and provider-independent, so it never contains UI, OCR, LLM/GPT, persistence/EF/SQL, HTTP/ASP.NET Core, cloud or email SDKs, or any I/O that makes a tax result non-deterministic. OCR and LLM output may only ever arrive here as untrusted *input* to validate.

## Existing types — check before creating anything

Do not re-specify or re-implement any of these:

- `ParseResult<T>`, `ValidationError`
- `MoneyRounding.ToCents` (half-up currency helper)
- `RecipientIdentifier` (SIN masking value object)
- `T5BoxCodes`, `T5OtherInformation`, `T5Slip`
- `T5RawInput`, `T5RawBox`, `T5SlipParser`
- `TaxLineEntry`, `T5MappingResult`
- `BoxLineMapping`, `T5LineMappingConfig`, `T5LineMap`
- `IT5LineMapProvider`, `EmbeddedT5LineMapProvider`
- `T5SlipBoxAmounts`, `T5LineMapper`
- `T4BoxCodes`, `T4OtherInformation`, `T4Slip`
- `T4RawInput`, `T4RawBox`, `T4SlipParser`
- `T4MappingResult`, `T4BoxLineMapping`, `T4LineMappingConfig`, `T4LineMap`
- `T4OtherInformationCodes`, `IT4LineMapProvider`, `EmbeddedT4LineMapProvider`
- `T4SlipBoxAmounts`, `T4LineMapper`
- `Mappings/T4LineMap.2025.json` (T4 box→line config, 2025 only)
- `Mappings/T5LineMap.2025.json` (T5 box→line config, 2025 only)

## Completed tax specs (001–004)

Spec 001 (T5 slip parsing), spec 002 (T5 box→CRA line mapping), spec 003 (T4 slip parsing), and spec 004 (T4 box→CRA line mapping) are implemented and merged.

Spec 003 covers T4 **parsing only**; spec 004 adds the T4 box→CRA line mapping (`TaxLineEntry` generation for T4) — both are merged. Two T4 required-field decisions are recorded in `specs/003-t4-slip-parsing/architecture.md` and flagged `[CONFIRM]`: Box 54 is required (so the parser presumes the employer/filing-side copy, not the employee copy), and Box 10 (province) is validated only when present. Spec 004 ships **2025 only**; several T4 boxes are deliberately deferred to `UnmappedBoxCodes` (calculation/schedule/residence-dependent) with per-box rationale in [Mappings/README.md](Mappings/README.md).

## Tests

This project's suite lives in `tests/TaxLogicEngine.Tests/` (parser, mapper, provider, boundary, PII, and year-isolation suites). Run everything with `dotnet test TaxAI.sln`; no test may be removed, skipped, or weakened to make a change pass.
