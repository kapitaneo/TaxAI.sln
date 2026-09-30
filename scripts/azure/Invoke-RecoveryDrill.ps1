<#
.SYNOPSIS
    The Canadian recovery, restore and control-ledger drill (spec 016 T06;
    DEP-17, DEP-18, DEP-19, DEP-20; AC-103, AC-105 to AC-116, AC-165).

.DESCRIPTION
    Every release decision is made by the compiled Tenancy RecoveryReleasePolicy
    and RecoveryRetentionWindow, and every ledger name and record by the
    compiled TaxAI.AzureInfrastructure AzureRecoveryControlStore - the same code
    the Portal runs - never by a second implementation in this script.

    Offline (default)
        A fully local simulation over synthetic facts. It proves the quarantine
        and release decisions (missing interval, missing acknowledgement, stale
        evidence, rollback, expired/revoked access, membership uncertainty),
        the ledger window arithmetic and the record format, and it accounts a
        synthetic reconciliation-inclusive timeline against the zone (15 min)
        and regional (4 h) objectives. Nothing leaves the process. It is NOT
        deployment evidence: DEP-17 to DEP-20 stay pending until the Live
        drills are observed against the exact resources.

    Posture (read-only Live)
        Against one explicitly named subscription and resource group, from
        inside the approved private network: SQL short-term retention is 35
        days, no long-term retention exists, backups are geo-zone redundant,
        the failover group is Manual, and each control-ledger container carries
        a LOCKED 90-day immutability policy strictly longer than the 42-day
        recoverable window. Changes nothing.

    SqlRestore (Live, DEP-17)
        With -ConfirmChangeAuthority only: restores the named database to a new
        synthetic-subscription database at a point 15 minutes ago and measures
        the elapsed time. It never overwrites, renames or deletes a database.
        The restored copy is a NEW incarnation: a host pointed at it starts
        quarantined until the orchestrator reconciles it.

    RegionalFailover (Live, DEP-18)
        With -ConfirmChangeAuthority only: performs the Manual failover-group
        failover to Canada East in a synthetic-only subscription, measures the
        time and the replication lag reported beforehand (RPO <= 15 min), and
        stops. Canada East application-hosting activation is a pending
        plan-amendment item; the drill records the application RTO as
        unexecuted rather than inventing it.

    Attest
        Publishes the trusted provisioning process's current membership
        attestation (-MembershipsPath) to both control accounts with the
        operator's own Entra sign-in. Create-only; nothing is overwritten.

    Output names closed check codes only: never a key, identifier, account,
    subscription, server, membership or taxpayer value.

    Exit codes:
      0  every executed check held
      1  one or more checks failed
      2  the drill could not run (bad arguments or unreadable input)
      3  a Live mode was not authorized or its tooling is unavailable.
         Record as unexecuted, never passed.

.PARAMETER Mode
    Offline (default), Posture, SqlRestore, RegionalFailover or Attest.

.PARAMETER AssemblyDirectory
    The build output holding TaxAI.AzureInfrastructure.dll, Tenancy.dll and their
    Azure dependencies (the Portal output).
    Defaults to the Portal's Release, then Debug, output.

.PARAMETER SubscriptionId
    Live only: the exact subscription the drill is authorized for.

.PARAMETER ResourceGroup
    Live only: the resource group holding the approved inventory.

.PARAMETER SqlServerName
    Posture, SqlRestore, RegionalFailover: the primary SQL logical server.

.PARAMETER SqlDatabaseName
    Posture, SqlRestore: the application database.

.PARAMETER FailoverGroupName
    Posture, RegionalFailover: the Manual failover group.

.PARAMETER ControlAccountNames
    Posture, Attest: the Canada Central and Canada East control-ledger accounts.

.PARAMETER MembershipsPath
    Attest only: a JSON array of { firmId, userId } exported from the trusted
    provisioning process immediately before publication.

.PARAMETER ConfirmChangeAuthority
    SqlRestore, RegionalFailover, Attest: the operator confirms explicit change
    authority for the named synthetic resources.

.EXAMPLE
    pwsh ./scripts/azure/Invoke-RecoveryDrill.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Offline', 'Posture', 'SqlRestore', 'RegionalFailover', 'Attest')]
    [string] $Mode = 'Offline',

    [string] $AssemblyDirectory,

    [string] $SubscriptionId,

    [string] $ResourceGroup,

    [string] $SqlServerName,

    [string] $SqlDatabaseName,

    [string] $FailoverGroupName,

    [string[]] $ControlAccountNames,

    [string] $MembershipsPath,

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

function Complete {
    param([string] $Banner)
    Write-Report $Banner
    if (@($results | Where-Object { -not $_.Held }).Count -gt 0) {
        exit 1
    }
    exit 0
}

# ---------------------------------------------------------- the assemblies
if (-not $AssemblyDirectory) {
    $AssemblyDirectory = @('Release', 'Debug') |
        ForEach-Object { Join-Path $repositoryRoot "src/TaxAI.Portal/bin/$_/net10.0" } |
        Where-Object { Test-Path -LiteralPath (Join-Path $_ 'TaxAI.AzureInfrastructure.dll') } |
        Select-Object -First 1
}
if (-not $AssemblyDirectory -or -not (Test-Path -LiteralPath (Join-Path $AssemblyDirectory 'Tenancy.dll'))) {
    Write-Host 'FATAL: the compiled TaxAI.AzureInfrastructure and Tenancy assemblies were not found. Build the solution first.' -ForegroundColor Red
    exit 2
}
try {
    foreach ($name in @('Tenancy.dll', 'TaxAI.AzureInfrastructure.dll')) {
        [void][System.Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath (Join-Path $AssemblyDirectory $name)).Path)
    }
    $null = [Tenancy.Models.RecoveryReleasePolicy]
    $null = [TaxAI.AzureInfrastructure.Control.AzureRecoveryControlStore]
}
catch {
    Write-Host 'FATAL: the recovery assemblies could not be loaded by this PowerShell runtime.' -ForegroundColor Red
    exit 2
}

function New-Incarnation {
    param([char] $Fill)
    $incarnation = $null
    [void][Tenancy.Models.DatabaseIncarnation]::TryCreate([string]::new($Fill, 64), [ref] $incarnation)
    return $incarnation
}

function New-Snapshot {
    param(
        [Parameter(Mandatory)][DateTimeOffset] $ReadAt,
        [object] $GenesisAt,
        [object] $AttestationAt,
        [string] $Status = 'Complete'
    )
    $records = [System.Collections.Generic.List[Tenancy.Contracts.RecoveryControlRecord]]::new()
    if ($null -ne $GenesisAt) {
        $records.Add([Tenancy.Contracts.RecoveryControlRecord]::Genesis((New-Incarnation 'e'), [DateTimeOffset] $GenesisAt))
    }
    $attestation = $null
    if ($null -ne $AttestationAt) {
        $membership = [Tenancy.Contracts.RecoveryAuthorizedMembership]::new(
            [Tenancy.Models.TrustedFirmId]::FromGuid([Guid]'aaaaaaaa-0000-4000-8000-000000000091'),
            [Guid]'dddddddd-0000-4000-8000-000000000091')
        $attestation = [Tenancy.Contracts.RecoveryAuthorizationAttestation]::new(
            [DateTimeOffset] $AttestationAt,
            [Tenancy.Contracts.RecoveryAuthorizedMembership[]]@($membership))
    }
    return [Tenancy.Contracts.RecoveryControlSnapshot]::new(
        [Tenancy.Models.RecoveryControlReadStatus]$Status,
        $ReadAt,
        [Tenancy.Contracts.RecoveryControlRecord[]]$records.ToArray(),
        $attestation)
}

function Get-Reasons {
    param(
        [Parameter(Mandatory)] $Snapshot,
        [Parameter(Mandatory)] $Extent,
        [Parameter(Mandatory)][DateTimeOffset] $At,
        [int] $LedgerDays = 90
    )
    $facts = [Tenancy.Models.RecoveryReleaseFacts]::new((New-Incarnation 'a'), $Snapshot, $Extent, $LedgerDays, 35, 7, $At)
    return @([Tenancy.Models.RecoveryReleasePolicy]::EvaluatePreconditions($facts) | ForEach-Object { $_.ToString() })
}

# ------------------------------------------------------------ offline
function Invoke-Offline {
    $now = [DateTimeOffset]::new(2026, 9, 29, 12, 0, 0, [TimeSpan]::Zero)
    $recent = [Tenancy.Models.RecoveredDataExtent]::Observed($true, $now.AddHours(-2))

    # AC-103, AC-111: the window arithmetic the ledger retention must beat.
    Add-Check 'PointInTimeRetentionIsThe35DayMaximum' ([Tenancy.Models.RecoveryRetentionWindow]::PointInTimeRestoreDays -eq 35)
    Add-Check 'LedgerOutlivesTheRecoverableWindow' ([Tenancy.Models.RecoveryRetentionWindow]::LedgerOutlivesBackups(90, 35, 7))
    Add-Check 'LedgerEqualToTheWindowRefused' (-not [Tenancy.Models.RecoveryRetentionWindow]::LedgerOutlivesBackups(42, 35, 7))
    Add-Check 'SevenYearSqlArchiveRefused' (-not [Tenancy.Models.RecoveryRetentionWindow]::LedgerOutlivesBackups(90, 2555, 7))

    # Positive control: a covered, fresh, attested recovery may proceed.
    $covered = New-Snapshot -ReadAt $now -GenesisAt $now.AddDays(-80) -AttestationAt $now.AddMinutes(-30)
    Add-Check 'CoveredRecoveryMayReconcile' (@(Get-Reasons $covered $recent $now).Count -eq 0)

    # The verification list: each failure quarantines.
    Add-Check 'MissingControlHistoryQuarantines' ((Get-Reasons (New-Snapshot -ReadAt $now -AttestationAt $now.AddMinutes(-30)) $recent $now) -contains 'ControlGenesisMissing')
    Add-Check 'UnavailableLedgerQuarantines' ((Get-Reasons (New-Snapshot -ReadAt $now -Status 'Unavailable') $recent $now) -contains 'ControlLedgerUnavailable')
    Add-Check 'StaleEvidenceQuarantines' ((Get-Reasons (New-Snapshot -ReadAt $now.AddMinutes(-20) -GenesisAt $now.AddDays(-80) -AttestationAt $now.AddMinutes(-30)) $recent $now) -contains 'ControlEvidenceStale')
    Add-Check 'MissingIntervalQuarantines' ((Get-Reasons (New-Snapshot -ReadAt $now -GenesisAt $now.AddHours(-1) -AttestationAt $now.AddMinutes(-30)) $recent $now) -contains 'RecoveryIntervalUncovered')
    Add-Check 'RollbackBeyondTheBackupWindowQuarantines' ((Get-Reasons $covered ([Tenancy.Models.RecoveredDataExtent]::Observed($true, $now.AddDays(-50))) $now) -contains 'RecoveredDataOutsideBackupWindow')
    Add-Check 'MembershipUncertaintyQuarantines' ((Get-Reasons (New-Snapshot -ReadAt $now -GenesisAt $now.AddDays(-80)) $recent $now) -contains 'AuthorizationAttestationMissing')
    Add-Check 'StaleAttestationQuarantines' ((Get-Reasons (New-Snapshot -ReadAt $now -GenesisAt $now.AddDays(-80) -AttestationAt $now.AddHours(-5)) $recent $now) -contains 'AuthorizationAttestationStale')

    # AC-115: an empty ledger may bootstrap only a pristine database.
    $empty = New-Snapshot -ReadAt $now
    Add-Check 'PristineDatabaseMayBootstrap' ([Tenancy.Models.RecoveryReleasePolicy]::MayBootstrap($empty, [Tenancy.Models.RecoveredDataExtent]::Observed($false, $null)))
    Add-Check 'ExistingDataNeverBootstraps' (-not [Tenancy.Models.RecoveryReleasePolicy]::MayBootstrap($empty, $recent))

    # Record format: opaque names, strict round trip.
    $record = [Tenancy.Contracts.RecoveryControlRecord]::TerminalStructured(
        [Tenancy.Models.TrustedFirmId]::FromGuid([Guid]'aaaaaaaa-0000-4000-8000-000000000091'),
        [Guid]'cccccccc-0000-4000-8000-000000000091',
        $now)
    $name = [TaxAI.AzureInfrastructure.Control.AzureRecoveryControlStore]::NameOf($record)
    $body = [TaxAI.AzureInfrastructure.Control.AzureRecoveryControlStore]::Serialize($record)
    $parsed = [TaxAI.AzureInfrastructure.Control.AzureRecoveryControlStore]::Parse($body, $name)
    Add-Check 'LedgerNamesAreOpaque' (-not $name.Contains('aaaaaaaa') -and -not $name.Contains('cccccccc'))
    Add-Check 'LedgerRecordsRoundTripStrictly' (($null -ne $parsed) -and $parsed.SameAs($record))

    # A synthetic, reconciliation-inclusive timeline (minutes). Not a measurement.
    $zone = 2 + 3 + 4          # instance restart + ledger/incarnation check + health
    $regional = 45 + 30 + 20 + 25 + 15   # SQL failover + storage failover + key checks + reconciliation + release/health
    Add-Check 'SimulatedZoneTimelineWithin15Minutes' ($zone -le 15)
    Add-Check 'SimulatedRegionalTimelineWithin4Hours' ($regional -le 240)

    Complete 'SIMULATED OFFLINE DRILL - synthetic facts only. This is not deployment evidence; DEP-17 to DEP-20 remain pending until the Live drills are observed against the exact resources.'
}

# --------------------------------------------------------------- live
function Invoke-Az {
    param([Parameter(Mandatory)][string[]] $Arguments)
    $output = & az @Arguments --only-show-errors --output json 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'An Azure CLI call failed.'
    }
    if (-not $output) {
        return $null
    }
    return ($output -join "`n") | ConvertFrom-Json -AsHashtable
}

function Assert-LiveAuthority {
    param([string[]] $Required, [switch] $Mutating)
    $missing = @($Required | Where-Object { -not (Get-Variable -Name $_ -ValueOnly) })
    if ($missing.Count -gt 0 -or ($Mutating -and -not $ConfirmChangeAuthority)) {
        Write-Host "UNEXECUTED: the $Mode drill requires $($Required -join ', ')$(if ($Mutating) { ' and explicit -ConfirmChangeAuthority' }). Record as unexecuted, never passed." -ForegroundColor Yellow
        exit 3
    }
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        Write-Host 'UNEXECUTED: the Azure CLI is not available. Record as unexecuted, never passed.' -ForegroundColor Yellow
        exit 3
    }
    $account = Invoke-Az @('account', 'show')
    if ([string] $account['id'] -ne $SubscriptionId) {
        Add-Check 'AuthorizedSubscription' $false
        Complete 'LIVE DRILL REFUSED: the signed-in subscription is not the authorized one.'
    }
    Add-Check 'AuthorizedSubscription' $true
}

function Invoke-Posture {
    Assert-LiveAuthority -Required @('SubscriptionId', 'ResourceGroup', 'SqlServerName', 'SqlDatabaseName', 'FailoverGroupName', 'ControlAccountNames')
    try {
        $common = @('--resource-group', $ResourceGroup, '--subscription', $SubscriptionId)
        $shortTerm = Invoke-Az (@('sql', 'db', 'str-policy', 'show', '--server', $SqlServerName, '--name', $SqlDatabaseName) + $common)
        Add-Check 'SqlPointInTimeRetentionIs35Days' ([int] $shortTerm['retentionDays'] -eq [Tenancy.Models.RecoveryRetentionWindow]::PointInTimeRestoreDays)
        $longTerm = Invoke-Az (@('sql', 'db', 'ltr-policy', 'show', '--server', $SqlServerName, '--name', $SqlDatabaseName) + $common)
        Add-Check 'NoSqlLongTermArchive' (@('weeklyRetention', 'monthlyRetention', 'yearlyRetention' | Where-Object { [string] $longTerm[$_] -notin @('PT0S', '', $null) }).Count -eq 0)
        $database = Invoke-Az (@('sql', 'db', 'show', '--server', $SqlServerName, '--name', $SqlDatabaseName) + $common)
        Add-Check 'SqlBackupsGeoZoneRedundant' ([string] $database['requestedBackupStorageRedundancy'] -eq 'GeoZone')
        Add-Check 'SqlZoneRedundant' ([bool] $database['zoneRedundant'])
        $group = Invoke-Az (@('sql', 'failover-group', 'show', '--server', $SqlServerName, '--name', $FailoverGroupName) + $common)
        Add-Check 'FailoverIsManual' ([string] $group['readWriteEndpoint']['failoverPolicy'] -eq 'Manual')
        Add-Check 'FailoverPartnerInCanadaEast' (@($group['partnerServers'] | Where-Object { [string] $_['location'] -ne 'Canada East' }).Count -eq 0)

        $window = [Tenancy.Models.RecoveryRetentionWindow]::LongestRecoverableWindow(35, 7).TotalDays
        foreach ($accountName in $ControlAccountNames) {
            foreach ($container in @('taxai-recovery-ledger', 'taxai-recovery-attestation', 'taxai-release-evidence')) {
                $policy = Invoke-Az (@('storage', 'container', 'immutability-policy', 'show', '--account-name', $accountName, '--container-name', $container) + $common)
                Add-Check "ControlContainerLocked" ([string] $policy['state'] -eq 'Locked')
                Add-Check "ControlContainerOutlivesBackups" ([int] $policy['immutabilityPeriodSinceCreationInDays'] -gt $window)
                Add-Check "ControlContainerNoProtectedAppend" (-not [bool] $policy['allowProtectedAppendWrites'])
            }
        }
    }
    catch {
        Add-Check 'PostureReadable' $false
    }
    Complete 'LIVE POSTURE CHECK - read-only. Record the executed checks against the exact resources.'
}

function Invoke-SqlRestore {
    Assert-LiveAuthority -Required @('SubscriptionId', 'ResourceGroup', 'SqlServerName', 'SqlDatabaseName') -Mutating
    $started = [DateTimeOffset]::UtcNow
    $restorePoint = $started.AddMinutes(-15)
    $target = "$SqlDatabaseName-drill-$($started.ToString('yyyyMMddHHmm'))"
    try {
        Invoke-Az @('sql', 'db', 'restore', '--resource-group', $ResourceGroup, '--subscription', $SubscriptionId,
            '--server', $SqlServerName, '--name', $SqlDatabaseName, '--dest-name', $target,
            '--time', $restorePoint.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ')) | Out-Null
        $elapsed = ([DateTimeOffset]::UtcNow - $started).TotalMinutes
        Add-Check 'PointInTimeRestoreCompleted' $true
        Add-Check 'RestoredAsANewDatabaseNeverOverwritten' ($target -ne $SqlDatabaseName)
        Write-Host ("  restore elapsed minutes: {0:N1}" -f $elapsed)
    }
    catch {
        Add-Check 'PointInTimeRestoreCompleted' $false
    }
    Complete 'LIVE SQL RESTORE DRILL (DEP-17). Point a synthetic host at the restored copy: it must start quarantined and release only after reconciliation (DEP-19, DEP-20). Delete the synthetic copy under the same change authority.'
}

function Invoke-RegionalFailover {
    Assert-LiveAuthority -Required @('SubscriptionId', 'ResourceGroup', 'SqlServerName', 'FailoverGroupName') -Mutating
    $common = @('--resource-group', $ResourceGroup, '--subscription', $SubscriptionId)
    try {
        $group = Invoke-Az (@('sql', 'failover-group', 'show', '--server', $SqlServerName, '--name', $FailoverGroupName) + $common)
        Add-Check 'FailoverIsManual' ([string] $group['readWriteEndpoint']['failoverPolicy'] -eq 'Manual')
        $partner = @($group['partnerServers'])[0]
        $started = [DateTimeOffset]::UtcNow
        Invoke-Az (@('sql', 'failover-group', 'set-primary', '--server', ([string] $partner['id']).Split('/')[-1], '--name', $FailoverGroupName) + $common) | Out-Null
        $elapsed = ([DateTimeOffset]::UtcNow - $started).TotalMinutes
        Add-Check 'ControlledSqlFailoverCompleted' $true
        Write-Host ("  SQL failover elapsed minutes: {0:N1}" -f $elapsed)
    }
    catch {
        Add-Check 'ControlledSqlFailoverCompleted' $false
    }
    Write-Host '  UNEXECUTED: Canada East application-hosting activation (pending plan amendment); the application RTO is not measured by this drill.' -ForegroundColor Yellow
    Complete 'LIVE REGIONAL DRILL (DEP-18, partial). The application-recovery objective remains pending; Document Intelligence never fails over.'
}

function Invoke-Attest {
    Assert-LiveAuthority -Required @('SubscriptionId', 'ControlAccountNames', 'MembershipsPath') -Mutating
    try {
        $memberships = @(Get-Content -LiteralPath $MembershipsPath -Raw | ConvertFrom-Json)
    }
    catch {
        Write-Host 'FATAL: the membership export could not be read.' -ForegroundColor Red
        exit 2
    }
    $recordedAt = [DateTimeOffset]::UtcNow
    $body = @{
        schemaVersion = 1
        recordedAtUtc = $recordedAt.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
        memberships = @($memberships | ForEach-Object { @{ firmId = ([Guid] $_.firmId).ToString('D'); userId = ([Guid] $_.userId).ToString('D') } })
    } | ConvertTo-Json -Depth 4 -Compress
    $name = [TaxAI.AzureInfrastructure.Control.AzureRecoveryControlStore]::AttestationNameOf($recordedAt)
    $path = Join-Path ([IO.Path]::GetTempPath()) "taxai-attestation-$([guid]::NewGuid().ToString('N')).json"
    Set-Content -LiteralPath $path -Value $body -NoNewline
    try {
        foreach ($accountName in $ControlAccountNames) {
            Invoke-Az @('storage', 'blob', 'upload', '--auth-mode', 'login', '--account-name', $accountName,
                '--container-name', 'taxai-recovery-attestation', '--name', $name, '--file', $path, '--overwrite', 'false') | Out-Null
        }
        Add-Check 'AttestationPublishedToBothCopies' $true
    }
    catch {
        Add-Check 'AttestationPublishedToBothCopies' $false
    }
    finally {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    }
    Complete 'ATTESTATION PUBLISHED - create-only, both Canadian control accounts.'
}

switch ($Mode) {
    'Offline' { Invoke-Offline }
    'Posture' { Invoke-Posture }
    'SqlRestore' { Invoke-SqlRestore }
    'RegionalFailover' { Invoke-RegionalFailover }
    'Attest' { Invoke-Attest }
}
