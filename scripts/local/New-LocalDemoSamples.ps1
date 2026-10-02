#requires -Version 7.0
<#
.SYNOPSIS
    Generates the two synthetic sample documents for the local Development demo.

.DESCRIPTION
    Writes one T4 sample and one medical-receipt sample (spec 017 T02) into an
    untracked directory. Each begins with the signature line the upload gate
    requires of a file declared as a PDF, followed by the text format the
    deterministic Development provider reads.

    The samples are deterministic-format text with a .pdf name. They are not
    viewable PDFs, and nothing here makes the application read a real PDF.

    The T4 sample needs a value in its recipient-identifier box, because the
    existing parser requires that box to be present. That value is assembled
    from random digits when this script runs. It exists only in the generated
    file: it is never a literal in this script and is never printed.

    An existing sample is kept, so a second run changes nothing. Use -Force to
    replace both.

    Use only these synthetic samples locally; never upload a real taxpayer
    document to a local host.

.PARAMETER OutputDirectory
    Where to write the samples. Defaults to the ignored artifacts/local-demo
    directory of this repository.

.PARAMETER Force
    Replace samples that already exist.
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts' 'local-demo'
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

$signatureLine = '%PDF-1.7'
$documentHeader = 'TAXAI-DETERMINISTIC-DOCUMENT/1'
$t4SampleName = 't4-sample.pdf'
$medicalSampleName = 'medical-sample.pdf'

function New-SyntheticRecipientIdentifier {
    # Leading zeros mark the value as synthetic to a reader; the remaining
    # digits come from the operating system's random source at run time.
    $digits = [System.Text.StringBuilder]::new('000')
    foreach ($position in 1..6) {
        $null = $digits.Append([System.Security.Cryptography.RandomNumberGenerator]::GetInt32(0, 10))
    }

    return $digits.ToString()
}

function Write-Sample([string] $name, [string[]] $lines) {
    $path = Join-Path $OutputDirectory $name

    if ((Test-Path -LiteralPath $path) -and -not $Force) {
        return
    }

    # Line feeds and no byte-order mark: the first bytes must be the signature.
    $text = ($lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($false))
}

$null = New-Item -ItemType Directory -Force -Path $OutputDirectory

# Exactly what the existing parser requires of an employee copy, plus one
# amount box the existing 2025 mapping configuration maps to a line.
Write-Sample $t4SampleName @(
    $signatureLine
    $documentHeader
    'KIND: T4'
    'YEAR: 2025'
    'EMPLOYER-NAME: Synthetic Employer Ltd.'
    'EMPLOYEE-NAME: Synthetic Employee'
    "BOX 12: $(New-SyntheticRecipientIdentifier)"
    'BOX 14: 50000.00'
    'BOX 45: 1'
)

Write-Sample $medicalSampleName @(
    $signatureLine
    $documentHeader
    'KIND: MEDICAL-EXPENSE-EVIDENCE'
    'SERVICE-DATE: 2025-03-14'
    'GROSS-AMOUNT: 120.50'
    'CURRENCY: CAD'
    'PROVIDER-NAME: Synthetic Dental Clinic'
    'PATIENT-NAME: Synthetic Employee'
    'RECEIPT-REFERENCE: R-SYNTHETIC-1'
)

Write-Host "[local-demo] Synthetic samples are ready: $t4SampleName, $medicalSampleName"
