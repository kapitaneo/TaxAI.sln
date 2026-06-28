# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TaxLogicEngine is a .NET 8.0 class library for an AI-powered Canadian tax filing assistant. It handles CRA form parsing (T4, T5), field-to-line-number mapping via JSON config, draft tax return generation, rule-based validation, and GPT-assisted explanations.

## Build and Test Commands

```bash
# Build
dotnet build TaxAI.sln.sln

# Run all tests
dotnet test TaxAI.sln.sln

# Run a single test
dotnet test TaxLogicEngine.Tests --filter "FullyQualifiedName~TestMethodName"
```

## Architecture

- **TaxLogicEngine/** — Core class library (net8.0, nullable enabled). Contains Models (T4Slip, TaxReturn, TaxLineEntry), Mappings (JSON config for box→CRA line), Services (Mapper, Calculator, Validator), and Prompts (GPT templates).
- **TaxLogicEngine.Tests/** — xUnit test project referencing the engine. Uses xunit 2.5.3, coverlet for coverage.

## Key Design Decisions

- Field mappings are data-driven via JSON files in Mappings/, not hardcoded.
- CRA line numbers are the canonical identifiers linking form boxes to tax return entries.

## Feature development workflow

Features are built through a three-stage slash-command pipeline (defined in `.claude/commands/`). Each stage stops for human review before the next:

1. **`/spec <feature>`** — acting as PM/BA, writes a PRD to `specs/NNN-<slug>/prd.md`. No code. Functional requirements must reference the domain rules below.
2. **`/plan <feature>`** — acting as architect, reads the PRD and writes `architecture.md` (design, data model, trade-offs, risks) plus `tasks.md` (an ordered, dependency-aware checklist). No production code.
3. **`/implement <feature>`** — acting as senior dev, implements ONLY the first unchecked `[ ]` task in `tasks.md`, writes its tests in the same pass, checks the box, then stops. One task at a time — never batch.

Spec/plan artifacts live under `specs/`. Find the next spec number from existing folders there.

## Current state

The source tree is a skeleton: only the two `.csproj` files and a placeholder `UnitTest1.cs` exist. The Models/Mappings/Services/Prompts layout in the README and Architecture section is the intended structure, not yet created.

## Domain rules (Canadian tax)

- **Never log or echo a full SIN.** Mask to the last 3 digits in all logs, errors, and output.
- **Tax calculations are per tax-year.** Never mix brackets, rates, or limits across years.
- **All currency math uses `decimal`,** rounded half-up to cents (`MidpointRounding.AwayFromZero`).
- **T4/T5 box values map to CRA line numbers via the JSON config only.** Never hardcode a line number in C#.
- **CRA filing deadlines:** T1 — April 30; self-employed — June 15.
