# Local demo: the accountant workflow, end to end

This guide runs TaxAI on your own machine and walks one synthetic case from
sign-in to the read-only review output. It takes about five minutes.

**A local run is not production authorization.** The local host is a
Development host: it uses a Development-only sign-in, a Development-only
document provider, and a local database. Nothing here deploys anything, and
nothing here makes the application safe to receive real data.

**Never use a real taxpayer document locally.** Upload only the two synthetic
samples the tool generates.

## Prerequisites

- Windows with **SQL Server LocalDB** (it ships with Visual Studio and with SQL
  Server Express). Another SQL Server on the same machine also works; see
  [Options](#options).
- The **.NET SDK** version pinned in `global.json`.
- **PowerShell 7** (`pwsh`).
- The .NET HTTPS development certificate, trusted by your browser. If your
  browser refuses `https://localhost:7199`, run `dotnet dev-certs https --trust`
  once and restart the browser. The demo needs HTTPS: the intake origin and the
  session cookies are HTTPS-only, and that rule is not relaxed locally.
- Two browser sessions that do not share cookies: a normal window and a
  **private window**, or two different browsers.

## The one command

From the repository root:

```powershell
pwsh ./scripts/local/Start-LocalDemo.ps1
```

It is safe to run again at any time. Each run:

1. checks the pinned toolchain and restores the repository-local tools;
2. starts LocalDB and applies the repository-owned migrations (the application
   itself never migrates);
3. generates the Development settings once and keeps them outside the
   repository;
4. writes the two synthetic samples into `artifacts/local-demo/`, once;
5. starts the host on `https://localhost:7199`.

When it prints `Sign in at https://localhost:7199/development/sign-in`, the
host is ready. Leave the window open; press `Ctrl+C` in it to stop the host.

The tool prints no key and no database connection value, and neither is written
to any tracked file.

## The samples

`artifacts/local-demo/` is ignored by git and holds:

- `t4-sample.pdf` — one synthetic T4 for tax year 2025;
- `medical-sample.pdf` — one synthetic medical receipt.

They are **synthetic** deterministic-format text files, **not viewable PDFs**: a
PDF viewer will not open them. They carry a `.pdf` name and signature only so
that they pass the unchanged upload check, and the Development provider reads
the structured text that follows. This is a local sample convention, not PDF
support; a real PDF, a photo, or a scan extracts nothing locally.

The T4 sample contains a made-up nine-digit value in its recipient-identifier
box, because the existing parser requires that box to be present. It is random
digits produced on your machine. It exists only in that ignored file and inside
the encrypted stored upload: it is never shown on any page, never stored in the
database, and never logged. Do not paste it anywhere.

## The twelve steps

Use your normal browser window for the accountant and the private window for
the client.

1. **Start the application.** Run the one command above and wait for the
   sign-in address.
2. **Sign in.** In the normal window open
   `https://localhost:7199/development/sign-in` and choose *Sign in as the
   development accountant*. You land on the case list.
3. **Create a case.** Choose *New case* (or *Create the first case*), enter a
   made-up taxpayer display name, set *Expected T4 documents* to 1 and
   *Expected medical-expense documents* to 1, and choose *Create intake case*.
4. **Copy the client link.** The secure client link is displayed once. Copy
   it. This is the only address you ever copy by hand.
5. **Be the client.** In the **second browser** session (the private window)
   paste the link and choose *Continue securely*. Answer *Available now* to
   both questions and save. Upload `t4-sample.pdf` in the T4 slot and
   `medical-sample.pdf` in the medical slot, then choose *Submit current
   intake*. You can close the private window.
6. **Run extraction.** Back in the normal window choose *Cases*, then
   *Documents* on your case. Open *Review extraction* for the declared T4 and
   choose *Run extraction*. Do the same for the medical document.
7. **Review the extraction.** The T4 page shows the classification, the
   employer and employee names, and the boxes as printed. The medical page
   shows the neutral evidence panel; no tax treatment is decided there.
8. **Correct a value.** On the T4 page, under *Record one correction*, choose
   *T4 box value*, enter box code `14` and a corrected amount such as
   `51000.00`, and choose *Record correction*.
9. **See the chain run again.** The page now shows a new *Latest version* and
   the corrected value. Every correction creates a new immutable version and
   runs the existing validation and tax-processing chain again; the earlier
   version is kept.
10. **Accept.** Choose *Accept current version* on the T4 page. Accepting the
    medical document is optional and does not affect the next step.
11. **Finalize the case.** Choose *Back to case documents*, then *Finalize
    accepted T4 documents*.
12. **Read the review output.** You are taken to the read-only accountant
    review page for the new review record. It shows the tax year, the slip, the
    boxes and values, and the tax lines the existing engine produced. Nothing
    medical appears there.

## What you should and should not see

- A refused correction or finalization shows one fixed notice at the top of the
  page and changes nothing. For example, finalizing before any T4 is accepted
  is refused.
- A page for something that does not exist, or that belongs to another firm, is
  an empty "not found" response. That is deliberate.
- No page, address, or message ever shows the recipient-identifier value, in
  whole or in part.
- The client pages never show an accountant link.

## Options

```powershell
# Prepare the database, settings and samples without starting the host.
pwsh ./scripts/local/Start-LocalDemo.ps1 -SetupOnly

# Discard the generated settings, the stored uploads and the local database,
# then prepare everything again. It asks first.
pwsh ./scripts/local/Start-LocalDemo.ps1 -Reset
```

`-ConnectionString` uses another SQL Server on this machine instead of LocalDB.
It must name a server on this machine and require encryption; the tool and the
host refuse anything else. Supply the value in your own shell and do not record
it in a file in the repository.

A second run keeps the generated keys and the stored uploads. Only `-Reset`
replaces them, because uploads stored under the old keys cannot be read again.

## If something does not work

- **The browser refuses the page.** The development certificate is not trusted;
  see [Prerequisites](#prerequisites).
- **"SQL Server LocalDB was not found."** Install LocalDB, or pass
  `-ConnectionString`.
- **The port is in use.** Another copy of the host is still running. Stop it
  with `Ctrl+C` in its window.
- **The client link says it is not available.** A link works once. Open the
  case from *Cases*, choose *Intake progress*, and reissue the link.
- **An upload is not accepted.** Use the generated samples exactly as written.
  Editing a sample or uploading another file is refused or extracts nothing.

## What this is not

The local demo does not deploy, does not provision a firm or a user beyond the
one synthetic development accountant, and does not read real documents. There
is no accountant-side upload, no review decision or approval, no export, and no
tax calculation. The production deployment gate is unchanged and still refuses
every deployable host.
