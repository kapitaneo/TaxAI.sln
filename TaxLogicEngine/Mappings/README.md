# T5 box → CRA line mapping config

Data-driven box→line rules for spec 002. One JSON file per tax year
(`T5LineMap.{year}.json`), embedded as an assembly resource and loaded by
`EmbeddedT5LineMapProvider`. **No CRA line number is hardcoded in C#** — this
directory is the single source of truth.

Every entry below was verified against official canada.ca pages (browser, because
canada.ca blocks automated fetch). Values apply to the **2025** tax year.

## Mapped boxes (T5LineMap.2025.json)

| Box | Description | CRA line(s) | Source |
|-----|-------------|-------------|--------|
| 11 | Taxable amount, dividends other than eligible | 12000, 12010 | Lines 12000/12010 – "T5 slips: box 11 and box 25" (12000); "box 11" (12010) |
| 25 | Taxable amount, eligible dividends | 12000 | Lines 12000/12010 – "T5 slips: box 11 and box 25" |
| 13 | Interest from Canadian sources | 12100 | Line 12100 – Interest and other investment income |
| 14 | Other income from Canadian sources | 12100 | Line 12100 – "other investment income" |
| 15 | Foreign income | 12100 | Line 12100 – foreign income (report in CAD) |
| 17 | Royalties from Canadian sources | 12100 | Line 12100 – other investment income |
| 19 | Accrued income: annuities | 12100 | Line 12100 – other investment income |
| 12 | Dividend tax credit, other than eligible | 40425 | Line 40425 – "the total of ... T5 slips (boxes 12 and 26)" |
| 26 | Dividend tax credit, eligible | 40425 | Line 40425 – "the total of ... T5 slips (boxes 12 and 26)" |

A box may map to **several** lines (box 11 → 12000 *and* 12010).

## Intentionally NOT mapped

These are absent from the config on purpose; they surface in
`T5MappingResult.UnmappedBoxCodes` (nothing is silently dropped).

| Box | Why not mapped |
|-----|----------------|
| 10, 24 | **Actual** dividend amounts — informational only; the *taxable* amounts (11/25) are what reach a line. |
| 16 | Foreign tax paid → **line 40500** (federal foreign tax credit) is **calculated** on Form T2209 (limited to the lesser of foreign tax paid and Canadian tax on the foreign income) — not a direct sum. Deferred to the calculation feature. |
| 18 | Capital gains dividends → **line 12700** is *taxable* capital gains, reached via **Schedule 3** after the capital-gains inclusion rate and loss-netting — a calculation, out of scope for route-and-total. Deferred to the calculation feature. |
| 21, 22, 23, 27, 28, 29 | Non-monetary identification/control boxes — not reported as amounts on a line. |
| 30 (+ other-information codes) | No standard T1 line; preserved as unmapped. |

## Source pages

- Lines 12000 and 12010 – Taxable amount of dividends from taxable Canadian corporations
- Line 12100 – Interest and other investment income
- Line 40425 – Federal dividend tax credit (2025)
- Line 12700 – Taxable capital gains (confirms box 18 is a Schedule 3 / inclusion-rate case)

## Adding a new year

Copy `T5LineMap.2025.json` to `T5LineMap.{year}.json`, re-verify each line against
that year's canada.ca pages, and add it as an `<EmbeddedResource>` in the csproj.
No C# change is required.
