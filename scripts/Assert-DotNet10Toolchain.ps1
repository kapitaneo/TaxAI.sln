<#
.SYNOPSIS
    Verifies the exact repository-wide .NET 10 SDK, project, and package contract.

.DESCRIPTION
    This preflight is data-driven from TaxAI.sln. It does not assert a project
    count; it requires every approved path exactly once, rejects unapproved or
    unregistered project files, and validates each discovered project.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot 'TaxAI.sln'
$violations = [System.Collections.Generic.List[string]]::new()

function Add-Violation {
    param([Parameter(Mandatory)][string] $Message)
    $violations.Add($Message)
}

function Normalize-Path {
    param([Parameter(Mandatory)][string] $Path)
    $Path.Replace('\', '/')
}

# The registry every project must be listed in to exist. A new module is
# registered here in the same PR that introduces it — spec 014 T01 added the two
# ClientIntake entries below. This list stays closed on purpose: an unregistered
# project file is a project nobody validated. Registering is additive and weakens
# nothing; the per-project net10.0, TargetFrameworks, xunit.v3, and test-package
# checks below apply to every entry.
$approvedProjects = @(
    'src/CaseProcessing/CaseProcessing.csproj'
    'src/CaseReview/CaseReview.csproj'
    'src/ClientIntake/ClientIntake.csproj'
    'src/DocumentExtraction/DocumentExtraction.csproj'
    'src/Persistence/Persistence.csproj'
    'src/TaxAI.Portal/TaxAI.Portal.csproj'
    'src/TaxCases/TaxCases.csproj'
    'src/TaxLogicEngine/TaxLogicEngine.csproj'
    'src/Tenancy/Tenancy.csproj'
    'tests/CaseProcessing.Tests/CaseProcessing.Tests.csproj'
    'tests/CaseReview.Tests/CaseReview.Tests.csproj'
    'tests/ClientIntake.Tests/ClientIntake.Tests.csproj'
    'tests/DocumentExtraction.Tests/DocumentExtraction.Tests.csproj'
    'tests/Persistence.Tests/Persistence.Tests.csproj'
    'tests/TaxAI.Portal.Tests/TaxAI.Portal.Tests.csproj'
    'tests/TaxCases.Tests/TaxCases.Tests.csproj'
    'tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj'
    'tests/Tenancy.Tests/Tenancy.Tests.csproj'
)

$testPackages = [ordered]@{
    'Microsoft.NET.Test.Sdk' = '18.8.1'
    'xunit' = '2.9.3'
    'xunit.runner.visualstudio' = '3.1.5'
    'coverlet.collector' = '10.0.1'
}

$frameworkPackages = @(
    [pscustomobject]@{
        Project = 'src/TaxAI.Portal/TaxAI.Portal.csproj'
        Package = 'Microsoft.AspNetCore.Authentication.OpenIdConnect'
        Version = '10.0.10'
    }
    [pscustomobject]@{
        Project = 'src/Persistence/Persistence.csproj'
        Package = 'Microsoft.EntityFrameworkCore.SqlServer'
        Version = '10.0.10'
    }
    [pscustomobject]@{
        Project = 'src/Persistence/Persistence.csproj'
        Package = 'Microsoft.EntityFrameworkCore.Design'
        Version = '10.0.10'
    }
    [pscustomobject]@{
        Project = 'tests/TaxAI.Portal.Tests/TaxAI.Portal.Tests.csproj'
        Package = 'Microsoft.AspNetCore.Mvc.Testing'
        Version = '10.0.10'
    }
    [pscustomobject]@{
        Project = 'tests/Persistence.Tests/Persistence.Tests.csproj'
        Package = 'Microsoft.EntityFrameworkCore.Sqlite'
        Version = '10.0.10'
    }
)

Push-Location $repositoryRoot
try {
    $sdkVersion = (& dotnet --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdkVersion -ne '10.0.302') {
        Add-Violation "dotnet --version must resolve exactly 10.0.302; found '$sdkVersion'."
    }

    $solutionOutput = @(& dotnet sln $solutionPath list 2>&1)
    if ($LASTEXITCODE -ne 0) {
        Add-Violation 'dotnet sln TaxAI.sln list failed.'
        $solutionProjects = @()
    }
    else {
        $solutionProjects = @(
            $solutionOutput |
                Where-Object { $_ -match '\.csproj$' } |
                ForEach-Object { Normalize-Path $_.Trim() }
        )
    }

    foreach ($approved in $approvedProjects) {
        $matches = @($solutionProjects | Where-Object { $_ -ceq $approved })
        if ($matches.Count -ne 1) {
            Add-Violation "$approved must occur exactly once in TaxAI.sln; found $($matches.Count)."
        }
    }

    foreach ($actual in $solutionProjects) {
        if ($actual -cnotin $approvedProjects) {
            Add-Violation "Unapproved project is declared in TaxAI.sln: $actual."
        }
    }

    $trackedProjects = @(
        Get-ChildItem -LiteralPath @(
            (Join-Path $repositoryRoot 'src'),
            (Join-Path $repositoryRoot 'tests')
        ) -Filter *.csproj -File -Recurse |
            ForEach-Object {
                Normalize-Path ([IO.Path]::GetRelativePath($repositoryRoot, $_.FullName))
            }
    )
    foreach ($project in $trackedProjects) {
        if ($project -cnotin $approvedProjects) {
            Add-Violation "Unapproved or unregistered project file exists: $project."
        }
    }

    $projectDocuments = @{}
    foreach ($project in $approvedProjects) {
        $fullPath = Join-Path $repositoryRoot $project
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            Add-Violation "Approved project file is missing: $project."
            continue
        }

        [xml] $document = [IO.File]::ReadAllText($fullPath)
        $projectDocuments[$project] = $document

        $targetFrameworks = @(
            $document.SelectNodes("//*[local-name()='TargetFramework']")
        )
        if ($targetFrameworks.Count -ne 1 -or
            [string]$targetFrameworks[0].InnerText -cne 'net10.0') {
            Add-Violation "$project must declare exactly one TargetFramework equal to net10.0."
        }
        if (@($document.SelectNodes(
                "//*[local-name()='TargetFrameworks']")).Count -gt 0) {
            Add-Violation "$project must not declare TargetFrameworks."
        }

        $packageReferences = @(
            $document.SelectNodes("//*[local-name()='PackageReference']")
        )
        if (@($packageReferences |
                Where-Object {
                    $_.GetAttribute('Include') -ceq 'xunit.v3'
                }).Count -gt 0) {
            Add-Violation "$project must not reference xunit.v3."
        }

        if ($project.StartsWith('tests/', [StringComparison]::Ordinal)) {
            foreach ($packageName in $testPackages.Keys) {
                $matches = @(
                    $packageReferences |
                        Where-Object {
                            $_.GetAttribute('Include') -ceq $packageName
                        }
                )
                if ($matches.Count -ne 1 -or
                    $matches[0].GetAttribute('Version') -cne
                        $testPackages[$packageName]) {
                    Add-Violation (
                        "$project must reference $packageName exactly once at " +
                        "$($testPackages[$packageName]).")
                }
            }
        }
    }

    foreach ($expected in $frameworkPackages) {
        if (-not $projectDocuments.ContainsKey($expected.Project)) {
            continue
        }
        $references = @(
            $projectDocuments[$expected.Project].SelectNodes(
                "//*[local-name()='PackageReference']") |
                Where-Object {
                    $_.GetAttribute('Include') -ceq $expected.Package
                }
        )
        if ($references.Count -ne 1 -or
            $references[0].GetAttribute('Version') -cne $expected.Version) {
            Add-Violation (
                "$($expected.Project) must reference $($expected.Package) " +
                "exactly once at $($expected.Version).")
        }
    }

    $designReference = @(
        $projectDocuments['src/Persistence/Persistence.csproj'].SelectNodes(
            "//*[local-name()='PackageReference']") |
            Where-Object {
                $_.GetAttribute('Include') -ceq
                    'Microsoft.EntityFrameworkCore.Design'
            }
    )
    if ($designReference.Count -ne 1 -or
        [string]$designReference[0].PrivateAssets -cne 'all') {
        Add-Violation 'Microsoft.EntityFrameworkCore.Design must retain PrivateAssets=all.'
    }

    foreach ($centralPath in @(
        'Directory.Build.props',
        'Directory.Build.targets',
        'Directory.Packages.props'
    )) {
        if (Test-Path -LiteralPath (Join-Path $repositoryRoot $centralPath)) {
            Add-Violation "$centralPath is forbidden by spec 013."
        }
    }

    foreach ($file in Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File |
        Where-Object { $_.Extension -in @('.csproj', '.props', '.targets') }) {
        $content = [IO.File]::ReadAllText($file.FullName)
        if ($content -match '<TargetFrameworks?>[^<]*(?:net8\.0|net9\.0)') {
            Add-Violation (
                "Active net8.0/net9.0 target declaration remains in " +
                "$(Normalize-Path ([IO.Path]::GetRelativePath($repositoryRoot, $file.FullName))).")
        }
    }

    $globalJson = Get-Content -LiteralPath (
        Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json
    if ($globalJson.sdk.version -cne '10.0.302' -or
        $globalJson.sdk.rollForward -cne 'disable' -or
        $globalJson.sdk.allowPrerelease -ne $false) {
        Add-Violation 'global.json must pin SDK 10.0.302 with rollForward=disable and allowPrerelease=false.'
    }

    $toolManifest = Get-Content -LiteralPath (
        Join-Path $repositoryRoot '.config/dotnet-tools.json') -Raw |
        ConvertFrom-Json
    $tools = @($toolManifest.tools.psobject.Properties)
    if ($toolManifest.version -ne 1 -or
        $toolManifest.isRoot -ne $true -or
        $tools.Count -ne 1 -or
        $tools[0].Name -cne 'dotnet-ef' -or
        $tools[0].Value.version -cne '10.0.10' -or
        $tools[0].Value.rollForward -ne $false -or
        @($tools[0].Value.commands).Count -ne 1 -or
        [string]$tools[0].Value.commands[0] -cne 'dotnet-ef') {
        Add-Violation 'The root tool manifest must contain only dotnet-ef 10.0.10.'
    }
}
finally {
    Pop-Location
}

if ($violations.Count -gt 0) {
    Write-Host '.NET 10 toolchain preflight failed:' -ForegroundColor Red
    foreach ($violation in $violations) {
        Write-Host "  - $violation" -ForegroundColor Red
    }
    exit 1
}

Write-Host '.NET 10 toolchain preflight passed.' -ForegroundColor Green
exit 0
