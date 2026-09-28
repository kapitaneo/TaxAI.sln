<#
.SYNOPSIS
    The key-rotation and protected-key recovery drill (spec 016 T03, DEP-14,
    AC-044 to AC-046).

.DESCRIPTION
    Proves that rotating an approved Key Vault key keeps legitimately retained
    old ciphertext decryptable, that a still-needed key version is never
    retired, and that emergency revocation is refused without just-in-time
    elevation, justification, an independent reviewer and a bounded window.

    Every retirement, revocation and rotation-target decision is made by the
    compiled TaxAI.AzureInfrastructure KeyVersionUsageGuard - the same code the
    platform relies on - never by a second implementation in this script.

    Offline (default)
        A fully local simulation with synthetic RSA 3072 keys and synthetic
        32-byte data keys wrapped with RSA-OAEP-256. Nothing leaves the
        process. It proves the drill's logic end to end but is NOT deployment
        evidence: DEP-14 stays pending until the Live drill is observed
        against the exact resources.

    Live
        Runs against one explicitly named vault and key in one explicitly named
        subscription, and only with -ConfirmChangeAuthority, because it adds a
        new key version. It must run from inside the approved private network:
        the vault has no public endpoint. It checks the vault posture and the
        90-day rotation policy against the data-configuration contract, wraps a
        synthetic value with the current version, rotates, unwraps the old
        ciphertext with the old version, evaluates retirement of the old
        version against the supplied usage inventory (it must be refused) and
        confirms the old version is still enabled. It never disables, deletes
        or purges a key version or a secret.

    Output names closed check codes only; it never prints a key, key
    identifier, subscription, vault name or ciphertext.

    Exit codes:
      0  every executed check held
      1  one or more checks failed
      2  the drill could not run (bad arguments or unreadable input)
      3  Live mode was not authorized or its tooling is unavailable.
         Record as unexecuted, never passed.

.PARAMETER Mode
    Offline (default) or Live.

.PARAMETER AssemblyPath
    The compiled TaxAI.AzureInfrastructure.dll. Defaults to the repository's
    Release, then Debug, build output.

.PARAMETER SubscriptionId
    Live only: the exact subscription the drill is authorized for.

.PARAMETER KeyVaultName
    Live only: the vault holding the key.

.PARAMETER KeyName
    Live only: the approved key to rotate.

.PARAMETER UsageEvidencePath
    Live only: a JSON array of keyUsageEvidence objects
    (infra/azure/contracts/data-configuration.schema.json) inventoried
    immediately before the drill, one per source.

.PARAMETER ConfirmChangeAuthority
    Live only: the operator confirms explicit change authority to add a key
    version to the named key.

.EXAMPLE
    pwsh ./scripts/azure/Invoke-KeyRotationDrill.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Offline', 'Live')]
    [string] $Mode = 'Offline',

    [string] $AssemblyPath,

    [string] $SubscriptionId,

    [string] $KeyVaultName,

    [string] $KeyName,

    [string] $UsageEvidencePath,

    [switch] $ConfirmChangeAuthority
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$results = [System.Collections.Generic.List[object]]::new()

function Add-Check {
    param([Parameter(Mandatory)][string] $Code, [Parameter(Mandatory)][bool] $Held)
    $results.Add([pscustomobject]@{ Code = $Code; Held = $Held })
}

function Write-Report {
    param([string] $Banner)
    foreach ($result in $results) {
        $state = if ($result.Held) { 'HELD' } else { 'FAILED' }
        Write-Host "  - $($result.Code): $state"
    }
    Write-Host $Banner
}

# ---------------------------------------------------------- the guard
if (-not $AssemblyPath) {
    $AssemblyPath = @('Release', 'Debug') |
        ForEach-Object { Join-Path $repositoryRoot "src/TaxAI.AzureInfrastructure/bin/$_/net10.0/TaxAI.AzureInfrastructure.dll" } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
}
if (-not $AssemblyPath -or -not (Test-Path -LiteralPath $AssemblyPath)) {
    Write-Host 'FATAL: the compiled TaxAI.AzureInfrastructure assembly was not found. Build the solution first.' -ForegroundColor Red
    exit 2
}
try {
    [void][System.Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
    $guard = [TaxAI.AzureInfrastructure.Keys.KeyVersionUsageGuard]::new()
}
catch {
    Write-Host 'FATAL: the guard assembly could not be loaded by this PowerShell runtime.' -ForegroundColor Red
    exit 2
}

function New-Evidence {
    param(
        [Parameter(Mandatory)][string] $Source,
        [Parameter(Mandatory)][bool] $Complete,
        [Parameter(Mandatory)][DateTimeOffset] $ObservedAt,
        [string[]] $References = @()
    )
    return [TaxAI.AzureInfrastructure.Keys.KeyUsageEvidence]::new(
        [TaxAI.AzureInfrastructure.Keys.KeyUsageSource]$Source,
        $Complete,
        $ObservedAt,
        [string[]]$References)
}

function Test-Retirement {
    param(
        [Parameter(Mandatory)][string] $Candidate,
        [Parameter(Mandatory)][string] $Active,
        [Parameter(Mandatory)][object[]] $Evidence,
        [Parameter(Mandatory)][DateTimeOffset] $At
    )
    $request = [TaxAI.AzureInfrastructure.Keys.KeyRetirementRequest]::new(
        $Candidate,
        $Active,
        [TaxAI.AzureInfrastructure.Keys.KeyUsageEvidence[]]$Evidence,
        $At)
    return $guard.EvaluateRetirement($request)
}

function Test-Refused {
    param([Parameter(Mandatory)] $Decision, [Parameter(Mandatory)][string] $Refusal)
    return (-not $Decision.IsPermitted) -and (@($Decision.Refusals | ForEach-Object { $_.ToString() }) -contains $Refusal)
}

function New-Revocation {
    param(
        [string] $Elevation = 'JustInTime',
        [string] $Justification = 'SuspectedKeyCompromise',
        [string] $Reference = 'INC-DRILL-0001',
        [Guid] $Requester = [Guid]'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        [Guid] $Reviewer = [Guid]'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        [TimeSpan] $Window = [TimeSpan]::FromHours(4),
        [Parameter(Mandatory)][DateTimeOffset] $At
    )
    $start = $At.AddMinutes(-5)
    return [TaxAI.AzureInfrastructure.Keys.EmergencyRevocationRequest]::new(
        [TaxAI.AzureInfrastructure.Keys.PrivilegedElevation]$Elevation,
        [TaxAI.AzureInfrastructure.Keys.RevocationJustification]$Justification,
        $Reference,
        $Requester,
        $Reviewer,
        $start,
        $start.Add($Window),
        $At)
}

# ------------------------------------------------------------ offline
function Invoke-Offline {
    $padding = [System.Security.Cryptography.RSAEncryptionPadding]::OaepSHA256
    $now = [DateTimeOffset]::new(2026, 1, 15, 12, 0, 0, [TimeSpan]::Zero)
    $keyId = 'https://kv-drill-synthetic.vault.azure.net/keys/drill-key'
    $version1 = "$keyId/$([guid]::NewGuid().ToString('N'))"
    $version2 = "$keyId/$([guid]::NewGuid().ToString('N'))"

    $key1 = [System.Security.Cryptography.RSA]::Create(3072)
    $key2 = [System.Security.Cryptography.RSA]::Create(3072)
    $dataKey = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    try {
        # Retained ciphertext wrapped before the rotation.
        $retained = $key1.Encrypt($dataKey, $padding)

        # Rotation: the new version becomes active; the old one is retained.
        $newWrap = $key2.Encrypt($dataKey, $padding)
        Add-Check 'RotatedVersionWraps' ([System.Security.Cryptography.CryptographicOperations]::FixedTimeEquals($key2.Decrypt($newWrap, $padding), $dataKey))

        # AC-045: authorized old ciphertext stays decryptable after rotation.
        Add-Check 'OldCiphertextDecryptableAfterRotation' ([System.Security.Cryptography.CryptographicOperations]::FixedTimeEquals($key1.Decrypt($retained, $padding), $dataKey))

        $complete = @(
            New-Evidence 'RetainedCiphertext' $true $now @($version1)
            New-Evidence 'DataProtectionKeyRing' $true $now
            New-Evidence 'RecoverableBackup' $true $now
        )

        # AC-046: a version still required by retained ciphertext is not retired.
        Add-Check 'StillRequiredVersionRetirementRefused' (Test-Refused (Test-Retirement $version1 $version2 $complete $now) 'VersionStillRequired')

        # Backups and the key ring count as much as live ciphertext.
        $backupOnly = @(
            New-Evidence 'RetainedCiphertext' $true $now
            New-Evidence 'DataProtectionKeyRing' $true $now
            New-Evidence 'RecoverableBackup' $true $now @($version1)
        )
        Add-Check 'BackupReferencedVersionRetirementRefused' (Test-Refused (Test-Retirement $version1 $version2 $backupOnly $now) 'VersionStillRequired')

        # Missing or incomplete evidence is a refusal, never an empty inventory.
        $incomplete = @(
            New-Evidence 'RetainedCiphertext' $false $now
            New-Evidence 'DataProtectionKeyRing' $true $now
            New-Evidence 'RecoverableBackup' $true $now
        )
        Add-Check 'IncompleteEvidenceRetirementRefused' (Test-Refused (Test-Retirement $version1 $version2 $incomplete $now) 'UsageEvidenceIncomplete')
        $missing = @(New-Evidence 'RetainedCiphertext' $true $now)
        Add-Check 'MissingEvidenceRetirementRefused' (Test-Refused (Test-Retirement $version1 $version2 $missing $now) 'UsageEvidenceIncomplete')
        $stale = @(
            New-Evidence 'RetainedCiphertext' $true $now.AddDays(-3)
            New-Evidence 'DataProtectionKeyRing' $true $now
            New-Evidence 'RecoverableBackup' $true $now
        )
        Add-Check 'StaleEvidenceRetirementRefused' (Test-Refused (Test-Retirement $version1 $version2 $stale $now) 'UsageEvidenceStale')

        # The active version is never retired.
        $unused = @(
            New-Evidence 'RetainedCiphertext' $true $now
            New-Evidence 'DataProtectionKeyRing' $true $now
            New-Evidence 'RecoverableBackup' $true $now
        )
        Add-Check 'ActiveVersionRetirementRefused' (Test-Refused (Test-Retirement $version2 $version2 $unused $now) 'ActiveVersion')

        # Only after rewrap, with complete current evidence that nothing needs
        # it, may the old version be retired - and the rewrapped data still reads.
        $rewrapped = $key2.Encrypt($key1.Decrypt($retained, $padding), $padding)
        $retirement = Test-Retirement $version1 $version2 $unused $now
        Add-Check 'UnusedVersionRetirementPermittedAfterRewrap' ($retirement.IsPermitted)
        Add-Check 'RewrappedCiphertextDecryptable' ([System.Security.Cryptography.CryptographicOperations]::FixedTimeEquals($key2.Decrypt($rewrapped, $padding), $dataKey))

        # AC-044: the approved rotation target.
        Add-Check 'RotationTargetNinetyDays' (([TaxAI.AzureInfrastructure.Keys.KeyVersionUsageGuard]::MeetsRotationTarget('P90D')) -and
            -not ([TaxAI.AzureInfrastructure.Keys.KeyVersionUsageGuard]::MeetsRotationTarget('P365D')) -and
            -not ([TaxAI.AzureInfrastructure.Keys.KeyVersionUsageGuard]::MeetsRotationTarget($null)))

        # Emergency revocation is privileged, justified, reviewed and bounded.
        Add-Check 'EmergencyRevocationAuthorizedWhenPrivileged' ($guard.EvaluateEmergencyRevocation((New-Revocation -At $now)).IsPermitted)
        Add-Check 'StandingAccessRevocationRefused' (Test-Refused $guard.EvaluateEmergencyRevocation((New-Revocation -Elevation 'Standing' -At $now)) 'ElevationNotJustInTime')
        Add-Check 'UnjustifiedRevocationRefused' (Test-Refused $guard.EvaluateEmergencyRevocation((New-Revocation -Justification 'None' -Reference '' -At $now)) 'JustificationMissing')
        Add-Check 'SelfReviewedRevocationRefused' (Test-Refused $guard.EvaluateEmergencyRevocation((New-Revocation -Reviewer ([Guid]'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa') -At $now)) 'IndependentReviewMissing')
        Add-Check 'UnboundedElevationRefused' (Test-Refused $guard.EvaluateEmergencyRevocation((New-Revocation -Window ([TimeSpan]::FromHours(24)) -At $now)) 'ElevationWindowNotBounded')
    }
    finally {
        [System.Security.Cryptography.CryptographicOperations]::ZeroMemory($dataKey)
        $key1.Dispose()
        $key2.Dispose()
    }

    $failed = @($results | Where-Object { -not $_.Held }).Count
    Write-Report 'SIMULATED OFFLINE DRILL - synthetic keys only. This is not deployment evidence; DEP-14 remains pending until the Live drill is observed against the exact resources.'
    if ($failed -gt 0) {
        exit 1
    }
    exit 0
}

# --------------------------------------------------------------- live
function Invoke-Az {
    param([Parameter(Mandatory)][string[]] $Arguments)
    $output = & az @Arguments --only-show-errors --output json 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'An Azure CLI call failed.'
    }
    return ($output -join "`n") | ConvertFrom-Json -AsHashtable
}

function ConvertTo-Base64Url {
    param([byte[]] $Bytes)
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function ConvertFrom-Base64Url {
    param([string] $Text)
    $normalized = $Text.Replace('-', '+').Replace('_', '/')
    switch ($normalized.Length % 4) {
        2 { $normalized += '==' }
        3 { $normalized += '=' }
    }
    return [Convert]::FromBase64String($normalized)
}

function Invoke-KeyOperation {
    param([string] $VersionId, [string] $Operation, [byte[]] $Value)
    $body = @{ alg = 'RSA-OAEP-256'; value = (ConvertTo-Base64Url $Value) } | ConvertTo-Json -Compress
    $bodyPath = Join-Path ([IO.Path]::GetTempPath()) "taxai-drill-$([guid]::NewGuid().ToString('N')).json"
    Set-Content -LiteralPath $bodyPath -Value $body -NoNewline
    try {
        $response = Invoke-Az @('rest', '--method', 'post', '--url', "$VersionId/$($Operation)?api-version=7.5", '--resource', 'https://vault.azure.net', '--body', "@$bodyPath")
    }
    finally {
        Remove-Item -LiteralPath $bodyPath -Force -ErrorAction SilentlyContinue
    }
    return ConvertFrom-Base64Url ([string] $response['value'])
}

function Invoke-Live {
    if (-not $ConfirmChangeAuthority -or -not $SubscriptionId -or -not $KeyVaultName -or -not $KeyName -or -not $UsageEvidencePath) {
        Write-Host 'UNEXECUTED: the Live drill requires -SubscriptionId, -KeyVaultName, -KeyName, -UsageEvidencePath and explicit -ConfirmChangeAuthority. Record as unexecuted, never passed.' -ForegroundColor Yellow
        exit 3
    }
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        Write-Host 'UNEXECUTED: the Azure CLI is not available. Record as unexecuted, never passed.' -ForegroundColor Yellow
        exit 3
    }

    try {
        $inventory = Get-Content -LiteralPath $UsageEvidencePath -Raw | ConvertFrom-Json -AsHashtable
        $contract = Get-Content -LiteralPath (Join-Path $repositoryRoot 'infra/azure/contracts/data-configuration.schema.json') -Raw | ConvertFrom-Json -AsHashtable
    }
    catch {
        Write-Host 'FATAL: the usage inventory or the data-configuration contract could not be read.' -ForegroundColor Red
        exit 2
    }

    $dataKey = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    try {
        $account = Invoke-Az @('account', 'show')
        if ([string] $account['id'] -ne $SubscriptionId) {
            Add-Check 'AuthorizedSubscription' $false
            Write-Report 'LIVE DRILL REFUSED: the signed-in subscription is not the authorized one.'
            exit 1
        }
        Add-Check 'AuthorizedSubscription' $true

        # AC-042, AC-043: the vault posture equals the reviewed contract.
        $expected = $contract['properties']['keyVault']['properties']
        $vault = Invoke-Az @('keyvault', 'show', '--name', $KeyVaultName, '--subscription', $SubscriptionId)
        $properties = $vault['properties']
        Add-Check 'VaultRegionCanadaCentral' ([string] $vault['location'] -eq $expected['location']['const'])
        Add-Check 'VaultRbacAuthorization' ($properties['enableRbacAuthorization'] -eq $expected['enableRbacAuthorization']['const'])
        Add-Check 'VaultSoftDelete' ($properties['enableSoftDelete'] -eq $expected['enableSoftDelete']['const'])
        Add-Check 'VaultSoftDeleteRetention' ([int] $properties['softDeleteRetentionInDays'] -eq $expected['softDeleteRetentionInDays']['const'])
        Add-Check 'VaultPurgeProtection' ($properties['enablePurgeProtection'] -eq $expected['enablePurgeProtection']['const'])
        Add-Check 'VaultPublicNetworkDisabled' ([string] $properties['publicNetworkAccess'] -eq $expected['publicNetworkAccess']['const'])

        # AC-044: the active key rotates at the 90-day target.
        $policy = Invoke-Az @('keyvault', 'key', 'rotation-policy', 'show', '--vault-name', $KeyVaultName, '--name', $KeyName, '--subscription', $SubscriptionId)
        $rotate = @($policy['lifetimeActions'] | Where-Object { [string] $_['action']['type'] -ieq 'rotate' })
        $target = if ($rotate.Count -eq 1) { [string] $rotate[0]['trigger']['timeAfterCreate'] } else { $null }
        Add-Check 'RotationTargetNinetyDays' ([TaxAI.AzureInfrastructure.Keys.KeyVersionUsageGuard]::MeetsRotationTarget($target))

        # AC-045: ciphertext wrapped before a rotation stays decryptable after it.
        $before = Invoke-Az @('keyvault', 'key', 'show', '--vault-name', $KeyVaultName, '--name', $KeyName, '--subscription', $SubscriptionId)
        $oldVersion = [string] $before['key']['kid']
        $retained = Invoke-KeyOperation $oldVersion 'wrapkey' $dataKey
        $after = Invoke-Az @('keyvault', 'key', 'rotate', '--vault-name', $KeyVaultName, '--name', $KeyName, '--subscription', $SubscriptionId)
        $newVersion = [string] $after['key']['kid']
        Add-Check 'RotationCreatedNewVersion' ($newVersion -ne $oldVersion)
        $recovered = Invoke-KeyOperation $oldVersion 'unwrapkey' $retained
        try {
            Add-Check 'OldCiphertextDecryptableAfterRotation' ([System.Security.Cryptography.CryptographicOperations]::FixedTimeEquals($recovered, $dataKey))
        }
        finally {
            [System.Security.Cryptography.CryptographicOperations]::ZeroMemory($recovered)
        }

        # AC-046: the old version is still required, so its retirement is
        # refused. The drill's own synthetic ciphertext is part of the inventory.
        $now = [DateTimeOffset]::UtcNow
        $evidence = foreach ($item in @($inventory)) {
            $references = @($item['referencedKeyVersionIds'] | ForEach-Object { [string] $_ })
            if ([string] $item['source'] -eq 'RetainedCiphertext') {
                $references += $oldVersion
            }
            New-Evidence ([string] $item['source']) ([bool] $item['isComplete']) ([DateTimeOffset]::Parse([string] $item['observedAtUtc'], [Globalization.CultureInfo]::InvariantCulture)) $references
        }
        $decision = Test-Retirement $oldVersion $newVersion @($evidence) $now
        Add-Check 'StillRequiredVersionRetirementRefused' (-not $decision.IsPermitted)

        # Protected-key recovery: the historical version is retained and enabled.
        $historical = Invoke-Az @('keyvault', 'key', 'show', '--id', $oldVersion)
        Add-Check 'HistoricalVersionRetainedAndEnabled' ([bool] $historical['attributes']['enabled'])
    }
    catch {
        Add-Check 'LiveDrillCompleted' $false
    }
    finally {
        [System.Security.Cryptography.CryptographicOperations]::ZeroMemory($dataKey)
    }

    $failed = @($results | Where-Object { -not $_.Held }).Count
    Write-Report 'LIVE DRILL observed against the named resources. Record the outcome in the DEP-14 evidence; this alone is not production authorization.'
    if ($failed -gt 0) {
        exit 1
    }
    exit 0
}

if ($Mode -eq 'Live') {
    Invoke-Live
}
Invoke-Offline
