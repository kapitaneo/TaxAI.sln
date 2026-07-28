<#
.SYNOPSIS
    Runs the full solution test suite and fails if any expected test project did
    not actually run.

.DESCRIPTION
    `dotnet test TaxAI.sln` returns exit code 0 even when an entire test assembly
    is skipped. During spec 010 T02 a Windows Application Control policy blocked
    TaxCases.Tests.dll: 337 tests silently did not run, and the command still
    reported success. Every wave gate in this repository's workflow trusts that
    exit code, so a wave could be merged on a green result that never ran part of
    the suite.

    This script closes that hole. It runs the suite with a TRX logger, then reads
    the per-project results back and fails unless every project in
    scripts/test-projects.json ran, reported no failures, skipped nothing, and met
    its established minimum count.

    Exit codes:
      0  every expected project ran and met its floor
      1  a gate violation (missing, failed, skipped, under floor, unregistered)
      2  the script could not run the suite at all

.PARAMETER Configuration
    Build configuration passed to dotnet test. Defaults to Debug.

.PARAMETER ResultsDirectory
    Where TRX files are written. Defaults to artifacts/test-results, which is
    git-ignored.

.EXAMPLE
    pwsh ./scripts/Invoke-SolutionTests.ps1
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Debug',
    [string] $ResultsDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $PSScriptRoot 'test-projects.json'
$solutionPath = Join-Path $repositoryRoot 'TaxAI.sln'

if (-not $ResultsDirectory) {
    $ResultsDirectory = Join-Path $repositoryRoot 'artifacts/test-results'
}

if (-not (Test-Path $manifestPath)) {
    Write-Host "FATAL: manifest not found at $manifestPath" -ForegroundColor Red
    exit 2
}

if (-not (Test-Path $solutionPath)) {
    Write-Host "FATAL: solution not found at $solutionPath" -ForegroundColor Red
    exit 2
}

# A stale TRX from a previous run would let a project that did not run this time
# look as though it had.
if (Test-Path $ResultsDirectory) {
    Remove-Item -Recurse -Force $ResultsDirectory
}
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null

$expected = ([System.IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json).projects

Write-Host "Running $($expected.Count) expected test projects ($Configuration)..." -ForegroundColor Cyan

dotnet test $solutionPath --configuration $Configuration --logger trx --results-directory $ResultsDirectory
$dotnetExitCode = $LASTEXITCODE

# Deliberately NOT an early exit. A non-zero dotnet exit is a real failure, but a
# ZERO exit is exactly what this script exists to distrust, so the per-project
# analysis runs either way and reports both.

$actual = @{}

foreach ($trx in Get-ChildItem -Path $ResultsDirectory -Filter *.trx -Recurse) {
    $xml = [xml][System.IO.File]::ReadAllText($trx.FullName)

    $storage = $null
    if ($xml.TestRun.TestDefinitions) {
        $unitTests = @($xml.TestRun.TestDefinitions.UnitTest)
        if ($unitTests.Count -gt 0 -and $unitTests[0]) {
            $storage = $unitTests[0].storage
        }
    }

    if (-not $storage) {
        Write-Host "  warning: $($trx.Name) records no test definitions; ignoring." -ForegroundColor Yellow
        continue
    }

    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($storage)
    $counters = $xml.TestRun.ResultSummary.Counters

    $actual[$projectName] = [pscustomobject]@{
        Total    = [int] $counters.total
        Executed = [int] $counters.executed
        Passed   = [int] $counters.passed
        Failed   = [int] $counters.failed
        Trx      = $trx.Name
    }
}

$violations = New-Object System.Collections.Generic.List[string]

foreach ($project in $expected) {
    $name = $project.name
    $floor = [int] $project.minimumTests

    if (-not $actual.ContainsKey($name)) {
        # The spec 010 T02 case: the assembly never ran, and dotnet still exited 0.
        $violations.Add("$name : DID NOT RUN. No TRX results were produced for it.")
        continue
    }

    $result = $actual[$name]

    if ($result.Failed -gt 0) {
        $violations.Add("$name : $($result.Failed) failed.")
    }

    $notExecuted = $result.Total - $result.Executed
    if ($notExecuted -gt 0) {
        $violations.Add("$name : $notExecuted of $($result.Total) tests were not executed (skipped).")
    }

    if ($result.Passed -lt $floor) {
        $violations.Add(
            "$name : passed $($result.Passed), below its floor of $floor. " +
            'Tests were lost, filtered, or did not run. Do not lower the floor to make this pass.')
    }
}

foreach ($name in $actual.Keys) {
    $isExpected = $false
    foreach ($project in $expected) {
        if ($project.name -eq $name) { $isExpected = $true }
    }

    if (-not $isExpected) {
        $violations.Add(
            "$name : ran but is not registered in scripts/test-projects.json. " +
            'Add it, with its current count as the floor, in the same PR that introduced it.')
    }
}

Write-Host ''
Write-Host 'Per-project results' -ForegroundColor Cyan

foreach ($project in $expected) {
    $name = $project.name

    if ($actual.ContainsKey($name)) {
        $result = $actual[$name]
        $status = 'ok    '
        $colour = 'Green'
        if ($result.Passed -lt [int] $project.minimumTests -or $result.Failed -gt 0) {
            $status = 'FAIL  '
            $colour = 'Red'
        }
        Write-Host ("  {0}{1,-28} {2,5} passed (floor {3})" -f $status, $name, $result.Passed, $project.minimumTests) -ForegroundColor $colour
    }
    else {
        Write-Host ("  DID NOT RUN {0,-24} (floor {1})" -f $name, $project.minimumTests) -ForegroundColor Red
    }
}

Write-Host ''

if ($violations.Count -gt 0) {
    Write-Host 'TEST EXECUTION GATE FAILED' -ForegroundColor Red
    foreach ($violation in $violations) {
        Write-Host "  - $violation" -ForegroundColor Red
    }
    Write-Host ''
    Write-Host "dotnet test itself exited $dotnetExitCode." -ForegroundColor Yellow
    exit 1
}

if ($dotnetExitCode -ne 0) {
    Write-Host "TEST EXECUTION GATE FAILED: dotnet test exited $dotnetExitCode." -ForegroundColor Red
    exit 1
}

Write-Host 'Test execution gate passed: every expected project ran and met its floor.' -ForegroundColor Green
exit 0
