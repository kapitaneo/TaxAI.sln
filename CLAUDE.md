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

## Domain rules (Canadian tax)

- **Never log or echo a full SIN.** Mask to the last 3 digits in all logs, errors, and output.
- **Tax calculations are per tax-year.** Never mix brackets, rates, or limits across years.
- **All currency math uses `decimal`,** rounded half-up to cents (`MidpointRounding.AwayFromZero`).
- **T4/T5 box values map to CRA line numbers via the JSON config only.** Never hardcode a line number in C#.
- **CRA filing deadlines:** T1 — April 30; self-employed — June 15.
