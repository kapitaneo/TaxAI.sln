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

---

# T4 box → CRA line mapping config

Data-driven box→line rules for spec 004 (`T4LineMap.{year}.json`), loaded by
`EmbeddedT4LineMapProvider`. Same rules: **no CRA line number is hardcoded in C#**;
one file per tax year; values apply to the **2025** tax year only.

Line numbers come from the canada.ca **individuals** T4 page ("T4 slip: Statement
of Remuneration Paid", date modified 2026-02-02); box identities/definitions from
the canada.ca **employers** page (which carries no line numbers). Verified via
browser (canada.ca blocks automated fetch), retrieved 2026-07-11.

## Mapped boxes (T4LineMap.2025.json)

| Box / code | Description | CRA line | Source |
|-----|-------------|----------|--------|
| 14 | Employment income | 10100 | Individuals T4 page — "line 10100" |
| 18 | Employee's EI premiums | 31200 | Individuals T4 page — "line 31200" |
| 20 | RPP contributions | 20700 | Individuals T4 page — "line 20700" |
| 22 | Income tax deducted | 43700 | Individuals T4 page — "line 43700" |
| 44 | Union dues | 21200 | Individuals T4 page — "line 21200" |
| 52 | Pension adjustment | 20600 | Individuals T4 page — "line 20600" |
| 39 | Security options deduction 110(1)(d) | 24900 | Individuals T4 page — "line 24900" |
| 41 | Security options deduction 110(1)(d.1) | 24900 | Individuals T4 page — "line 24900" |
| 42 | Employment commissions | 10120 | Individuals T4 page — "line 10120" |
| 43 | Canadian Forces / police deduction | 24400 | Individuals T4 page — "line 24400" |
| 77 | Workers' comp benefits repaid to employer | 22900 | Individuals T4 page — "line 22900" |

Codes 39 and 41 both land on line 24900 and therefore sum onto one entry.

## Intentionally NOT mapped

Absent from the config on purpose; when populated they surface in
`T4MappingResult.UnmappedBoxCodes` (nothing silently dropped). The reason each is
deferred is recorded here — it is documentary, not encoded behaviour.

| Box / code | Why not mapped |
|-----|----------------|
| 16 / 17 (CPP/QPP) | Lines 30800 **and** 22215 via **Schedule 8 / Form RC381** — a base-vs-enhanced *calculation*, not a direct sum. Deferred to the calculation feature. |
| 16A / 17A (second CPP/QPP) | Line 22215 via **Schedule 8 / RC381** — calculation. Deferred. |
| 46 (charitable donations) | Line 34900 but entered via **Schedule 9** — schedule-dependent. Deferred. |
| 55 (PPIP premiums) | Line **31205 if Quebec resident, else 31200** — residence is not on the T4, so it cannot be routed here. Deferred. |
| 24 / 26 / 56 (insurable/pensionable earnings) | Calculation inputs with **no line** of their own. |
| 66 / 67, 85, 87 | "See line" cases (retiring allowances → 13000; private-health premiums → 33099/33199; emergency-services volunteer → 10100 + 31220/31240). Deferred in v1. |
| 45 (dental) | canada.ca: "Do not report on your income tax return." |
| 30,31,32,33,34,36,38,40,86 | Already included in Box 14; do not report separately. |
| 69,71,88 / 78–83 / 74,75 | Indian Act exempt / self-employment forms / past-service informational. |

## Source pages

- Individuals: T4 slip – Statement of Remuneration Paid (box → line numbers)
- Employers: T4 slip – Information for employers (box identities/definitions only)
