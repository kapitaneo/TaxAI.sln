#requires -Version 7.0
<#
.SYNOPSIS
    Prepares and starts TaxAI on this machine for a local Development demo.

.DESCRIPTION
    One command, safe to run repeatedly (spec 017 T01 and T02):

      1. checks the pinned toolchain;
      2. prepares a local SQL Server database and applies the repository-owned
         migrations with the repository-local dotnet-ef tool;
      3. generates the Development settings the host requires, once;
      4. generates the synthetic sample documents, once;
      5. starts the host in the Development environment on HTTPS loopback.

    The application itself never migrates. Generated settings are kept outside
    the repository, in the .NET user-secrets store under this tool's own
    identifier, and are handed to the launched host as environment variables of
    that one process. The portal project declares no user-secrets identifier, so
    no test host ever reads them.

    Nothing this tool prints contains a generated key or a database connection
    value.

    A local run is never production authorization. Use only the synthetic
    samples; never upload a real taxpayer document to a local host.

.PARAMETER SetupOnly
    Prepare the database and settings, then stop without starting the host.

.PARAMETER Reset
    Discard the generated settings, the local content directory and the local
    database, then prepare everything again. Stored uploads become unreadable
    once their keys are regenerated, which is why this is never implicit.

.PARAMETER ConnectionString
    Use another SQL Server on this machine instead of LocalDB. It must name a
    loopback server and require encryption explicitly.

.PARAMETER SettingsPath
    Keep the generated settings in this file instead of the user-secrets store.

.PARAMETER ContentRoot
    Keep encrypted uploads in this directory instead of the ignored
    artifacts/local-demo/content directory. It must be an absolute path. It is
    recorded on first use and not changed afterwards.

.PARAMETER SkipDatabase
    Do not create the database or apply migrations.

.PARAMETER Force
    Do not ask before -Reset discards local data.
#>
[CmdletBinding()]
param(
    [switch] $SetupOnly,
    [switch] $Reset,
    [string] $ConnectionString,
    [string] $SettingsPath,
    [string] $ContentRoot,
    [switch] $SkipDatabase,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$portalProject = Join-Path $repositoryRoot 'src' 'TaxAI.Portal'
$persistenceProject = Join-Path $repositoryRoot 'src' 'Persistence'
$localDemoRoot = Join-Path $repositoryRoot 'artifacts' 'local-demo'
$sampleGenerator = Join-Path $PSScriptRoot 'New-LocalDemoSamples.ps1'

# This tool's own identifier in the user-secrets store. It is deliberately not
# a UserSecretsId of any project.
$secretsId = 'taxai-local-demo'
$launchProfile = 'TaxAI.Portal.LocalDemo'
$origin = 'https://localhost:7199/'
$localDbInstance = 'MSSQLLocalDB'
$localDatabase = 'TaxAiLocalDemo'
$connectionStringKey = 'ConnectionStrings:TaxAiDatabase'
$contentRootKey = 'TaxAi:Intake:DevelopmentContentRoot'
$generatedKeyNames = @(
    'TaxAi:Intake:DigestKey',
    'TaxAi:Intake:FingerprintKey',
    'TaxAi:Intake:ContentEncryptionKey'
)

function Write-Step([string] $message) {
    Write-Host "[local-demo] $message"
}

function Get-DefaultSettingsPath {
    $store = if ($IsWindows) {
        Join-Path $env:APPDATA 'Microsoft' 'UserSecrets'
    }
    else {
        Join-Path $HOME '.microsoft' 'usersecrets'
    }

    return Join-Path $store $secretsId 'secrets.json'
}

function New-GeneratedKey {
    $bytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)

    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Read-Settings([string] $path) {
    $settings = [ordered]@{}

    if (Test-Path -LiteralPath $path) {
        $stored = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        foreach ($name in $stored.Keys) {
            $settings[$name] = [string] $stored[$name]
        }
    }

    return $settings
}

function Invoke-Native([string] $description, [scriptblock] $command) {
    # Tool output can echo configuration, so it is captured and never printed.
    $null = & $command 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "$description failed (exit code $LASTEXITCODE). Run the step by hand to see its output."
    }
}

if (-not $SettingsPath) {
    $SettingsPath = Get-DefaultSettingsPath
}

$SettingsPath = [System.IO.Path]::GetFullPath($SettingsPath)

# ------------------------------------------------------------------- toolchain

Write-Step 'Checking the pinned toolchain.'
Push-Location $repositoryRoot
try {
    Invoke-Native 'Restoring the repository-local tools' { dotnet tool restore }
}
finally {
    Pop-Location
}

# ----------------------------------------------------------------------- reset

if ($Reset) {
    if (-not $Force -and
        -not $PSCmdlet.ShouldContinue(
            'The generated settings, the local content directory and the local database will be discarded.',
            'Reset the local demo')) {
        Write-Step 'Reset cancelled. Nothing was changed.'
        return
    }

    $previous = Read-Settings $SettingsPath
    if (-not $SkipDatabase -and $previous.Contains($connectionStringKey)) {
        Write-Step 'Dropping the local database.'
        $env:ConnectionStrings__TaxAiDatabase = $previous[$connectionStringKey]
        Push-Location $repositoryRoot
        try {
            Invoke-Native 'Dropping the local database' {
                dotnet ef database drop --force --project $persistenceProject -- --development-loopback
            }
        }
        finally {
            Pop-Location
            Remove-Item Env:ConnectionStrings__TaxAiDatabase -ErrorAction SilentlyContinue
        }
    }

    if ($previous.Contains($contentRootKey) -and
        (Test-Path -LiteralPath $previous[$contentRootKey])) {
        Remove-Item -LiteralPath $previous[$contentRootKey] -Recurse -Force
    }

    if (Test-Path -LiteralPath $SettingsPath) {
        Remove-Item -LiteralPath $SettingsPath -Force
    }
}

# -------------------------------------------------------------------- settings

Write-Step 'Preparing the Development settings.'
$settings = Read-Settings $SettingsPath

foreach ($name in $generatedKeyNames) {
    # Never regenerated once present: a new key would make already-stored
    # encrypted uploads unreadable.
    if (-not $settings.Contains($name)) {
        $settings[$name] = New-GeneratedKey
    }
}

if (-not $settings.Contains($contentRootKey)) {
    $settings[$contentRootKey] = if ($ContentRoot) {
        [System.IO.Path]::GetFullPath($ContentRoot)
    }
    else {
        Join-Path $localDemoRoot 'content'
    }
}

if ($ConnectionString) {
    $settings[$connectionStringKey] = $ConnectionString
}
elseif (-not $settings.Contains($connectionStringKey)) {
    $settings[$connectionStringKey] =
        "Server=(localdb)\$localDbInstance;Database=$localDatabase;" +
        'Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
}

$fixedSettings = [ordered]@{
    # Inert placeholders. The host requires an identity provider to be named in
    # every environment; locally it is never contacted, because the
    # Development sign-in is used instead of the external challenge.
    'TaxAi:Oidc:Authority'                   = 'https://development-fixture.taxai.invalid/'
    'TaxAi:Oidc:ClientId'                    = 'taxai-local-demo'
    'TaxAi:Intake:PublicOrigin'              = $origin
    'TaxAi:Intake:LinkLifetime'              = '1.00:00:00'
    'TaxAi:Intake:ClientSessionIdleLifetime' = '00:30:00'
    'TaxAi:Intake:MaximumFileBytes'          = '10485760'
    'TaxAi:Intake:MaximumFilesPerRequest'    = '1'
    'TaxAi:Intake:PublicPermitLimit'         = '60'
    'TaxAi:Intake:PublicWindow'              = '00:01:00'
    'TaxAi:Intake:SessionPermitLimit'        = '120'
    'TaxAi:Intake:SessionWindow'             = '00:01:00'
    'TaxAi:Intake:AnswerPermitLimit'         = '60'
    'TaxAi:Intake:AnswerWindow'              = '00:01:00'
    'TaxAi:Intake:UploadPermitLimit'         = '30'
    'TaxAi:Intake:UploadWindow'              = '00:01:00'
    'TaxAi:Intake:UploadConcurrencyLimit'    = '2'
    'TaxAi:Intake:SubmissionPermitLimit'     = '30'
    'TaxAi:Intake:SubmissionWindow'          = '00:01:00'
    'TaxAi:Extraction:Provider'              = 'Deterministic'
    'TaxAi:Extraction:ContentReader'         = 'DevelopmentEncryptedIntakeContentStore'
    'TaxAi:Extraction:ProviderCallLimit'     = '00:00:30'
    'TaxAi:Extraction:MaximumSourceBytes'    = '10485760'
    'TaxAi:Extraction:ProviderLabel'         = 'deterministic-local-demo'
}

foreach ($name in $fixedSettings.Keys) {
    $settings[$name] = $fixedSettings[$name]
}

$null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SettingsPath)
$null = New-Item -ItemType Directory -Force -Path $settings[$contentRootKey]
$settings | ConvertTo-Json | Set-Content -LiteralPath $SettingsPath -Encoding utf8NoBOM

# -------------------------------------------------------------------- database

if (-not $SkipDatabase) {
    if (-not $ConnectionString -and $settings[$connectionStringKey].Contains('(localdb)')) {
        if (-not (Get-Command sqllocaldb -ErrorAction SilentlyContinue)) {
            throw 'SQL Server LocalDB was not found. Install it, or pass -ConnectionString for another SQL Server on this machine.'
        }

        Write-Step 'Starting SQL Server LocalDB.'
        $null = & sqllocaldb create $localDbInstance 2>&1
        Invoke-Native 'Starting SQL Server LocalDB' { sqllocaldb start $localDbInstance }
    }

    Write-Step 'Applying the repository-owned migrations.'
    $env:ConnectionStrings__TaxAiDatabase = $settings[$connectionStringKey]
    Push-Location $repositoryRoot
    try {
        Invoke-Native 'Applying the migrations' {
            dotnet ef database update --project $persistenceProject -- --development-loopback
        }
    }
    finally {
        Pop-Location
        Remove-Item Env:ConnectionStrings__TaxAiDatabase -ErrorAction SilentlyContinue
    }
}

# --------------------------------------------------------------------- samples

# Existing samples are kept by the generator, so this is safe on every run.
& $sampleGenerator -OutputDirectory $localDemoRoot

Write-Step 'Setup is complete.'
Write-Step 'Upload only the synthetic samples in artifacts/local-demo.'
Write-Step "Sign in at ${origin}development/sign-in"

if ($SetupOnly) {
    return
}

# ------------------------------------------------------------------------ host

# The settings reach the host as environment variables of this process tree
# only. They are removed again when the host stops.
$exported = @()
try {
    foreach ($name in $settings.Keys) {
        $variable = $name.Replace(':', '__')
        Set-Item -LiteralPath "Env:$variable" -Value $settings[$name]
        $exported += $variable
    }

    Write-Step 'Starting the host. Press Ctrl+C to stop it.'
    & dotnet run --project $portalProject --launch-profile $launchProfile
}
finally {
    foreach ($variable in $exported) {
        Remove-Item -LiteralPath "Env:$variable" -ErrorAction SilentlyContinue
    }
}
