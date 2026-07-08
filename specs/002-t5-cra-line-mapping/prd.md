# PRD: T5 Box → CRA Line Mapping

## Problem

Spec 001 produces validated `T5Slip` models but stops before they mean anything on
a tax return: a box value is useless until it lands on the correct CRA line. This
feature maps a taxpayer's T5 slip box values to CRA line numbers and aggregates
them into the `TaxLineEntry` items a draft return is built from — the step spec 001
deliberately deferred. It serves the engine's return-builder and, ultimately, the
individual filer whose investment income must reach the right lines.

## User stories

1. As the **return builder**, I want a set of `T5Slip`s mapped to CRA line numbers,
   so that I can assemble a draft return without knowing box-to-line rules myself.
2. As the **return builder**, I want amounts that land on the same CRA line summed
   into one `TaxLineEntry`, so that each line carries the total across all slips
   (as the CRA Federal Worksheet requires).
3. As an **engine maintainer**, I want the box→line rules to live in the JSON
   mapping config, not C#, so that CRA changes are a data edit and no line number is
   ever hardcoded.
4. As a **compliance reviewer**, I want the mapping scoped to a single tax year, so
   that a box never lands on a line that changed in another year.
5. As the **return builder**, I want boxes with no line mapping (or that route to a
   credit/schedule) handled explicitly rather than silently dropped, so that nothing
   a slip reported is lost.
6. As a **developer**, I want a fail-closed result (errors, no partial map) when a
   slip can't be mapped, so that a bad mapping never yields a wrong return.

## Functional requirements

1. The mapper SHALL accept one or more validated `T5Slip`s (from spec 001) for a
   single tax year and return the resulting `TaxLineEntry` items (CRA line number +
   amount). It SHALL NOT parse slips or read files other than the mapping config.
2. Box→CRA-line rules SHALL come **only from a JSON mapping config** in `Mappings/`.
   No CRA line number SHALL be hardcoded in C# (per CLAUDE.md). The config is the
   single source of truth for which box maps to which line.
3. The mapping config SHALL be **per tax-year**. The mapper SHALL use the config for
   the slips' tax year and SHALL NOT mix mappings across years (per CLAUDE.md).
4. When multiple box amounts (across boxes and across slips) map to the **same CRA
   line**, the mapper SHALL **sum them** into a single `TaxLineEntry` for that line
   (CRA reports the total per line via the Federal Worksheet).
5. All amount aggregation SHALL use `decimal`, rounded half-up to cents
   (`MidpointRounding.AwayFromZero`, per CLAUDE.md) — reusing the existing
   `MoneyRounding` helper.
6. The mapper SHALL support a box mapping to a **non-income destination** (e.g.
   dividend tax credit, foreign tax credit, a schedule) as defined by the config,
   and represent it as a `TaxLineEntry` keyed by that destination line — it SHALL
   NOT force every box onto an income line.
7. Only the box values the config maps SHALL be used. A box present on the slip with
   **no mapping** (including `OtherInformation` codes) SHALL be reported back
   distinctly (e.g. an "unmapped boxes" list), not silently discarded (FR-traceable
   to spec 001 FR3).
8. All input amounts SHALL be treated as **already in CAD**. If a slip's
   `CurrencyCode` is not `CAD`, the mapper SHALL fail closed with a validation error
   (foreign-to-CAD conversion is out of scope — see Open questions), because CRA
   requires amounts reported on a line to be in Canadian dollars.
9. The mapper SHALL return a **fail-closed result** (`ParseResult<T>`-style): on any
   mapping error (unknown tax-year config, non-CAD slip, malformed config) it SHALL
   return errors and no partial mapping.
10. Mapping SHALL be deterministic and side-effect free: same slips + same config →
    same `TaxLineEntry` set.

## Out of scope

- **Parsing** slips (done in spec 001) — input is already `T5Slip` models.
- **Foreign-currency conversion** to CAD (Bank of Canada rate). Non-CAD slips are
  rejected here; conversion is its own feature.
- **Tax calculation**: computing the dividend tax credit, gross-ups, foreign tax
  credit limits, or any tax owing. This feature only *routes and totals* amounts the
  slip already states.
- **Other slip types** (T4, T3, T5013). T5 only.
- Building the full **draft T1 return** or any PDF/JSON export.
- Authoring the definitive mapping table — the JSON config values must be verified
  against official CRA sources during implementation, not invented.

## Acceptance criteria

1. **Single box to a line**
   Given one CAD T5 slip with interest (box 13) = `100.00`,
   When it is mapped,
   Then a `TaxLineEntry` for the interest line (per config, e.g. 12100) with amount
   `100.00` is produced.

2. **Aggregation across boxes onto one line**
   Given a slip where box 13 = `100.00` and box 15 (foreign income) = `50.00` both
   map to line 12100,
   When mapped,
   Then a single `TaxLineEntry` for 12100 with amount `150.00` is produced.

3. **Aggregation across multiple slips**
   Given two slips each with box 13 = `100.00`,
   When mapped together,
   Then the interest line has one `TaxLineEntry` totalling `200.00`.

4. **Non-income destination**
   Given a slip with dividend tax credits (box 12 + box 26) that the config routes
   to the dividend-tax-credit line,
   When mapped,
   Then those amounts total onto that credit line's `TaxLineEntry`, not an income
   line.

5. **Unmapped box preserved, not dropped**
   Given a slip carrying a box code with no mapping in the config,
   When mapped,
   Then that code appears in the result's unmapped-boxes list and does not silently
   vanish.

6. **Foreign currency rejected**
   Given a slip whose `CurrencyCode` is `USD`,
   When mapped,
   Then the mapper fails closed with an error identifying the non-CAD slip and
   produces no `TaxLineEntry`s.

7. **Per-tax-year config**
   Given slips for tax year 2024,
   When mapped,
   Then the 2024 mapping config is used and no other year's mapping is applied.

8. **Rounding**
   Given box amounts that sum to `100.005` on a line,
   When mapped,
   Then that line's `TaxLineEntry` is `100.01` (half-up, away from zero).

## Open questions

1. **Foreign amounts:** confirmed reject-non-CAD for v1 (FR8). Is that acceptable, or
   should the mapper instead accept a caller-supplied CAD amount / FX rate? (Real
   returns need conversion eventually; proposing to keep it a separate feature.)
2. **Actual vs taxable dividend boxes:** on the T1, the *taxable* amounts (box 11,
   box 25) map to the dividend income line; the *actual* amounts (box 10, box 24) are
   informational. Should the config map only the taxable boxes and treat actual
   boxes as unmapped, or record both? (Needs CRA confirmation during planning.)
3. **Destination model:** are credit/schedule destinations (line 40425, 40500,
   Schedule 3 → 12700) represented as ordinary `TaxLineEntry`s keyed by line number,
   or do schedules need their own type? Affects the output shape.
4. **`TaxLineEntry` shape:** minimum fields — CRA line number + decimal amount. Do we
   also need the source box code(s) / provenance for auditing and SIN-safe display?
5. **Config validation & versioning:** where do per-year config files live and how is
   a missing or malformed year config surfaced (error vs. exception)? What is the
   first tax year to ship?
6. **Which lines for v1:** ship the full T5 box set (dividends, interest, foreign,
   royalties, capital gains dividends, accrued income, credits), or a core income
   subset first? Each destination line must be CRA-verified before it goes in config.
