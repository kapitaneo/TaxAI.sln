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
        the ledger window arithmetic and the record format, and it proves the
        structure and ordering of the zone and regional drills and that the
        Canada East recovery-host IaC they activate exists. It measures
        nothing and invents no timing. Nothing leaves the process. It is NOT
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

    ZoneFailover (Live, AC-105, AC-106)
        With -ConfirmChangeAuthority only: forces an in-region failover of the
        zone-redundant database, restarts the primary application host and
        measures the time until the host answers /health through the primary
        gateway again (target <= 15 min). Zone-redundant storage and ledger
        copies need no action. The released incarnation is unchanged, so the
        host must start without reconciliation.

    RegionalFailover (Live, AC-107, AC-108, DEP-18)
        With -ConfirmChangeAuthority only, in a synthetic-only subscription,
        in this exact order, measuring from the first step:
          1. require a current membership attestation in the Canada East copy;
          2. record the geo-replication lag before failover (RPO <= 15 min);
          3. Manual SQL failover-group failover to the Canada East server;
          4. customer-managed failover of the source and key-ring accounts;
          5. activate the Canada East recovery host (main.bicep, recovery
             profile, the operator's -ActivationParametersPath);
          6. deploy the exact release artifact to the recovery host;
          7. require the host to stay unavailable while quarantined;
          8. observe the new release record in the Canada East ledger - the
             orchestrator writes it only after deletion replay, session/link
             revocation and membership reconciliation all succeed;
          9. require the first healthy response to follow that release;
         10. require the host to answer through the recovery gateway;
         11. report the complete elapsed interval (target <= 4 h).
        Document Intelligence never fails over: extraction answers the fixed
        503 until the Canada Central resource is reachable again.

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

.PARAMETER RecoveryRegion
    Posture: the deployment's configured recovery region - the exact value of
    the `TaxAi:Production:RecoveryRegion` setting (main.bicep `recoveryRegion`).
    The failover partner's programmatic location is compared to this value;
    no region name is built into the drill.

.PARAMETER ControlAccountNames
    Posture, Attest, RegionalFailover: the Canada Central and Canada East
    control-ledger accounts, in that order.

.PARAMETER RecoveryResourceGroup
    RegionalFailover: the dedicated Canada East recovery resource group.

.PARAMETER ActivationParametersPath
    RegionalFailover: the operator's main.bicep parameters file for the
    recovery profile with its sealed `activation` input (never committed).

.PARAMETER ArtifactPath
    RegionalFailover: the exact approved release artifact (zip).

.PARAMETER StorageAccountNames
    RegionalFailover: the source and key-ring storage accounts to fail over.

.PARAMETER PublicHostName
    ZoneFailover, RegionalFailover: the approved public host name.

.PARAMETER SiteName
    ZoneFailover: the primary App Service site.

.PARAMETER GatewayAddress
    ZoneFailover: the primary gateway public address.

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
    [ValidateSet('Offline', 'Posture', 'SqlRestore', 'ZoneFailover', 'RegionalFailover', 'Attest')]
    [string] $Mode = 'Offline',

    [string] $AssemblyDirectory,

    [string] $SubscriptionId,

    [string] $ResourceGroup,

    [string] $SqlServerName,

    [string] $SqlDatabaseName,

    [string] $FailoverGroupName,

    [string] $RecoveryRegion,

    [string[]] $ControlAccountNames,

    [string] $MembershipsPath,

    [string] $RecoveryResourceGroup,

    [string] $ActivationParametersPath,

    [string] $ArtifactPath,

    [string[]] $StorageAccountNames,

    [string] $PublicHostName,

    [string] $SiteName,

    [string] $GatewayAddress,

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

# ------------------------------------------------------------- the plans
# The exact, ordered steps each Live drill performs. Offline proves their
# structure; only a Live run against authorized resources measures anything.
$RegionalPlan = @(
    'RequireCurrentAttestation'
    'CaptureReplicationLag'
    'SqlFailoverToCanadaEast'
    'StorageFailoverToCanadaEast'
    'ActivateRecoveryHost'
    'DeployReleaseArtifact'
    'RequireQuarantinedStartup'
    'ObserveCanadaEastRelease'
    'RequireHealthyOnlyAfterRelease'
    'RequireReachableThroughRecoveryGateway'
    'MeasureElapsedRecovery'
)
$ZonePlan = @(
    'ForceInRegionSqlFailover'
    'RestartPrimaryHost'
    'RequireHealthyThroughPrimaryGateway'
    'MeasureElapsedRecovery'
)

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

    # Structure and ordering only. Nothing is timed offline.
    $order = @{}
    for ($index = 0; $index -lt $RegionalPlan.Count; $index++) { $order[$RegionalPlan[$index]] = $index }
    Add-Check 'RegionalPlanIsComplete' ($RegionalPlan.Count -eq 11 -and @($RegionalPlan | Select-Object -Unique).Count -eq 11)
    Add-Check 'AttestationPrecedesAnyChange' ($order['RequireCurrentAttestation'] -eq 0)
    Add-Check 'RpoCapturedBeforeFailover' ($order['CaptureReplicationLag'] -lt $order['SqlFailoverToCanadaEast'])
    Add-Check 'DataFailsOverBeforeTheHostActivates' ([Math]::Max($order['SqlFailoverToCanadaEast'], $order['StorageFailoverToCanadaEast']) -lt $order['ActivateRecoveryHost'])
    Add-Check 'QuarantineObservedBeforeRelease' ($order['RequireQuarantinedStartup'] -lt $order['ObserveCanadaEastRelease'])
    Add-Check 'AdmissionVerifiedOnlyAfterRelease' ($order['ObserveCanadaEastRelease'] -lt $order['RequireHealthyOnlyAfterRelease'])
    Add-Check 'MeasurementEndsTheDrill' ($order['MeasureElapsedRecovery'] -eq $RegionalPlan.Count - 1)
    Add-Check 'ZonePlanIsComplete' ($ZonePlan.Count -eq 4 -and $ZonePlan[-1] -eq 'MeasureElapsedRecovery')

    # The recovery host the regional drill activates exists in this repository.
    $main = Get-Content -LiteralPath (Join-Path $repositoryRoot 'infra/azure/main.bicep') -Raw
    Add-Check 'RecoveryHostIaCPresent' (
        ([regex]::Matches($main, "module recovery(?:Egress|Runtime|Portal|Endpoints|Ingress) '[^']+' = if \(activating\)")).Count -eq 5 -and
        $main.Contains("profile: 'RegionalRecovery'"))
    Add-Check 'HostingModulesServeCanadaEast' (@('app-service', 'application-gateway', 'egress', 'private-endpoints' | Where-Object {
        (Get-Content -LiteralPath (Join-Path $repositoryRoot "infra/azure/modules/$_.bicep") -Raw) -notmatch "'canadaeast'" }).Count -eq 0)

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
    Assert-LiveAuthority -Required @('SubscriptionId', 'ResourceGroup', 'SqlServerName', 'SqlDatabaseName', 'FailoverGroupName', 'RecoveryRegion', 'ControlAccountNames')
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
        # The partner's programmatic location is read from the server resource and
        # compared with the configured recovery region, never with display text.
        $partners = @($group['partnerServers'])
        $partnerRegions = @($partners | ForEach-Object { [string] (Invoke-Az @('sql', 'server', 'show', '--ids', [string] $_['id']))['location'] })
        Add-Check 'FailoverPartnerInRecoveryRegion' ($partners.Count -eq 1 -and
            @($partnerRegions | Where-Object { -not [string]::Equals($_, $RecoveryRegion, [StringComparison]::Ordinal) }).Count -eq 0)

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

function Test-Healthy {
    param([Parameter(Mandatory)][string] $Address)
    # Through the gateway, presenting the approved host name; certificate
    # validation stays on. Only the fixed /health literal is read.
    $output = & curl.exe --silent --max-time 20 --resolve "${PublicHostName}:443:$Address" "https://$PublicHostName/health" 2>$null
    return $LASTEXITCODE -eq 0 -and ($output -join '') -ceq 'Healthy'
}

function Wait-Healthy {
    param([Parameter(Mandatory)][string] $Address, [Parameter(Mandatory)][TimeSpan] $Deadline, [DateTimeOffset] $Started)
    while (([DateTimeOffset]::UtcNow - $Started) -lt $Deadline) {
        if (Test-Healthy $Address) {
            return [DateTimeOffset]::UtcNow
        }
        Start-Sleep -Seconds 30
    }
    return $null
}

function Invoke-ZoneFailover {
    Assert-LiveAuthority -Required @('SubscriptionId', 'ResourceGroup', 'SqlServerName', 'SqlDatabaseName', 'SiteName', 'PublicHostName', 'GatewayAddress') -Mutating
    if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
        Write-Host 'UNEXECUTED: curl is not available to observe the host. Record as unexecuted, never passed.' -ForegroundColor Yellow
        exit 3
    }
    $common = @('--resource-group', $ResourceGroup, '--subscription', $SubscriptionId)
    $started = [DateTimeOffset]::UtcNow
    try {
        Invoke-Az (@('sql', 'db', 'failover', '--server', $SqlServerName, '--name', $SqlDatabaseName) + $common) | Out-Null
        Add-Check 'ForceInRegionSqlFailover' $true
        Invoke-Az (@('webapp', 'restart', '--name', $SiteName) + $common) | Out-Null
        Add-Check 'RestartPrimaryHost' $true
        $healthy = Wait-Healthy -Address $GatewayAddress -Deadline ([TimeSpan]::FromMinutes(30)) -Started $started
        Add-Check 'RequireHealthyThroughPrimaryGateway' ($null -ne $healthy)
        if ($null -ne $healthy) {
            $elapsed = ($healthy - $started).TotalMinutes
            Write-Host ("  zone recovery elapsed minutes: {0:N1}" -f $elapsed)
            Add-Check 'ZoneRecoveryWithin15Minutes' ($elapsed -le 15)
        }
    }
    catch {
        Add-Check 'ZoneDrillCompleted' $false
    }
    Complete 'LIVE ZONE DRILL (AC-105, AC-106). Record the measured interval against the exact resources.'
}

function Get-NewReleaseInstant {
    param([Parameter(Mandatory)][string] $EastAccount, [Parameter(Mandatory)][DateTimeOffset] $After)
    $blobs = Invoke-Az @('storage', 'blob', 'list', '--auth-mode', 'login', '--account-name', $EastAccount,
        '--container-name', 'taxai-recovery-ledger', '--prefix', 'release/')
    $instants = @($blobs | ForEach-Object { [DateTimeOffset]::Parse([string] $_['properties']['lastModified'], [Globalization.CultureInfo]::InvariantCulture) } |
        Where-Object { $_ -ge $After } | Sort-Object)
    if ($instants.Count -eq 0) {
        return $null
    }
    return $instants[0]
}

function Invoke-RegionalFailover {
    Assert-LiveAuthority -Required @('SubscriptionId', 'ResourceGroup', 'RecoveryResourceGroup', 'SqlServerName', 'SqlDatabaseName', 'FailoverGroupName', 'ControlAccountNames', 'StorageAccountNames', 'ActivationParametersPath', 'ArtifactPath', 'PublicHostName') -Mutating
    if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
        Write-Host 'UNEXECUTED: curl is not available to observe the host. Record as unexecuted, never passed.' -ForegroundColor Yellow
        exit 3
    }
    if ($ControlAccountNames.Count -ne 2 -or $RecoveryResourceGroup -ieq $ResourceGroup) {
        Write-Host 'FATAL: two control accounts (Canada Central, Canada East) and a distinct recovery resource group are required.' -ForegroundColor Red
        exit 2
    }

    $common = @('--resource-group', $ResourceGroup, '--subscription', $SubscriptionId)
    $eastLedger = $ControlAccountNames[1]
    $started = [DateTimeOffset]::UtcNow
    try {
        # 1. Current authorization evidence exists before anything changes.
        $attestations = Invoke-Az @('storage', 'blob', 'list', '--auth-mode', 'login', '--account-name', $eastLedger,
            '--container-name', 'taxai-recovery-attestation', '--prefix', 'attestation/')
        $newest = @($attestations | ForEach-Object { [DateTimeOffset]::Parse([string] $_['properties']['lastModified'], [Globalization.CultureInfo]::InvariantCulture) } | Sort-Object)[-1]
        $attested = $null -ne $newest -and ($started - $newest) -le [Tenancy.Models.RecoveryRetentionWindow]::AttestationMaximumAge
        Add-Check 'RequireCurrentAttestation' $attested
        if (-not $attested) {
            Complete 'REGIONAL DRILL STOPPED: publish a current attestation (-Mode Attest) first. Nothing was changed.'
        }

        # 2. The structured-data loss window, read before failover.
        $links = Invoke-Az (@('sql', 'db', 'replica', 'list-links', '--server', $SqlServerName, '--name', $SqlDatabaseName) + $common)
        $lag = @($links | ForEach-Object { $_['replicationLag'] } | Where-Object { $null -ne $_ } | ForEach-Object { [int] $_ }) | Measure-Object -Maximum
        Add-Check 'CaptureReplicationLag' ($lag.Count -gt 0)
        if ($lag.Count -gt 0) {
            Write-Host ("  replication lag seconds before failover: {0}" -f $lag.Maximum)
            Add-Check 'SqlRpoWithin15Minutes' ($lag.Maximum -le 900)
        }

        # 3. Manual, controlled SQL failover to Canada East.
        $group = Invoke-Az (@('sql', 'failover-group', 'show', '--server', $SqlServerName, '--name', $FailoverGroupName) + $common)
        Add-Check 'FailoverIsManual' ([string] $group['readWriteEndpoint']['failoverPolicy'] -eq 'Manual')
        $partner = ([string] @($group['partnerServers'])[0]['id']).Split('/')[-1]
        Invoke-Az (@('sql', 'failover-group', 'set-primary', '--server', $partner, '--name', $FailoverGroupName) + $common) | Out-Null
        Add-Check 'SqlFailoverToCanadaEast' $true

        # 4. Customer-managed failover of the source and key-ring accounts.
        foreach ($account in $StorageAccountNames) {
            Invoke-Az (@('storage', 'account', 'failover', '--name', $account, '--yes') + $common) | Out-Null
        }
        Add-Check 'StorageFailoverToCanadaEast' $true

        # 5. Activate the Canada East recovery host from the committed IaC.
        $deployment = Invoke-Az @('deployment', 'group', 'create', '--resource-group', $RecoveryResourceGroup, '--subscription', $SubscriptionId,
            '--name', "taxai-recovery-$($started.ToString('yyyyMMddHHmm'))",
            '--template-file', (Join-Path $repositoryRoot 'infra/azure/main.bicep'), '--parameters', "@$ActivationParametersPath")
        $site = [string] $deployment['properties']['outputs']['recoverySiteName']['value']
        $addressId = [string] $deployment['properties']['outputs']['recoveryIngressAddressId']['value']
        Add-Check 'ActivateRecoveryHost' (-not [string]::IsNullOrEmpty($site) -and -not [string]::IsNullOrEmpty($addressId))
        $address = [string] (Invoke-Az @('network', 'public-ip', 'show', '--ids', $addressId))['ipAddress']

        # 6. The exact approved release artifact.
        Invoke-Az @('webapp', 'deploy', '--resource-group', $RecoveryResourceGroup, '--subscription', $SubscriptionId,
            '--name', $site, '--src-path', $ArtifactPath, '--type', 'zip') | Out-Null
        Add-Check 'DeployReleaseArtifact' $true

        # 7-9. Quarantined until the Canada East release; healthy only after it.
        $quarantinedObserved = -not (Test-Healthy $address)
        Add-Check 'RequireQuarantinedStartup' $quarantinedObserved
        $healthy = Wait-Healthy -Address $address -Deadline ([TimeSpan]::FromHours(4)) -Started $started
        $release = Get-NewReleaseInstant -EastAccount $eastLedger -After $started
        Add-Check 'ObserveCanadaEastRelease' ($null -ne $release)
        Add-Check 'RequireHealthyOnlyAfterRelease' ($null -ne $healthy -and $null -ne $release -and $release -le $healthy)

        # 10-11. Reachable through the recovery gateway; the whole interval.
        Add-Check 'RequireReachableThroughRecoveryGateway' ($null -ne $healthy)
        if ($null -ne $healthy) {
            $elapsed = ($healthy - $started).TotalMinutes
            Write-Host ("  regional recovery elapsed minutes (reconciliation included): {0:N1}" -f $elapsed)
            Add-Check 'MeasureElapsedRecovery' $true
            Add-Check 'RegionalRecoveryWithin4Hours' ($elapsed -le 240)
        }
        else {
            Add-Check 'MeasureElapsedRecovery' $false
        }
    }
    catch {
        Add-Check 'RegionalDrillCompleted' $false
    }
    Complete 'LIVE REGIONAL DRILL (AC-107, AC-108, DEP-18). Document Intelligence never fails over; extraction answers the fixed 503 until Canada Central is reachable. Record the measured results against the exact resources.'
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
    'ZoneFailover' { Invoke-ZoneFailover }
    'RegionalFailover' { Invoke-RegionalFailover }
    'Attest' { Invoke-Attest }
}
