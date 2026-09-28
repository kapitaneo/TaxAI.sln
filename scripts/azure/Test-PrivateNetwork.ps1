<#
.SYNOPSIS
    Private-network, public-denial and deny-default egress probe for the TaxAI
    regional topology (spec 016 T02: DEP-06, DEP-07, DEP-08, DEP-09).

.DESCRIPTION
    One evaluator decides every result from a set of observations:

      DEP-06  From EVERY running application worker (at least two), each of
              the four dependency host names (source Blob, SQL, Key Vault,
              Document Intelligence) resolves only to that dependency's
              private endpoint addresses, all of them private (AC-049-052).
      DEP-07  Public network access is Disabled on all four resources AND an
              access attempt through each public endpoint, from a vantage
              point outside the virtual network, is denied (AC-053-056).
      DEP-08  Application and configuration traffic are both routed into the
              network, the only default route goes to the firewall at its
              observed address, no other internet route exists, and on every
              worker an unapproved destination is unreachable while the
              approved identity endpoint is reachable - the positive control
              that proves the probe can observe a success (AC-058).
      DEP-09  A synthetic random marker (never taxpayer bytes) posted by every
              worker to an unapproved receiver never arrives, while the same
              receiver does detect a marker the runner posts directly - the
              positive receive detector (AC-057).

    Modes:
      Evaluate (default)  Evaluate an observation file. The report is always
                          labelled SUPPLIED OBSERVATIONS - it is never deployed
                          proof, whatever the file claims.
      Live                Collect the observations from the exact deployed
                          resources and evaluate them. Requires the explicit
                          subscription, resource group, site, the four
                          resource IDs, the synthetic receiver URLs and
                          -ConfirmSyntheticProbeAuthority. Without the Azure
                          CLI or access the check is recorded UNEXECUTED - never
                          passed and never skipped.

    Output names checks and closed refusal codes only; it never prints an
    address, host name or marker.

    Exit codes:
      0  every check passed
      1  one or more refusals
      2  the check could not run (unreadable input or bad arguments)
      3  one or more checks were not executed (nothing refused)

.EXAMPLE
    pwsh ./scripts/azure/Test-PrivateNetwork.ps1 -ObservationPath ./observations.json
#>
[CmdletBinding()]
param(
    [ValidateSet('Evaluate', 'Live')]
    [string] $Mode = 'Evaluate',

    [string] $ObservationPath,

    [string] $SubscriptionId,
    [string] $ResourceGroup,
    [string] $SiteName,
    [string] $SourceStorageResourceId,
    [string] $SqlServerResourceId,
    [string] $KeyVaultResourceId,
    [string] $DocumentIntelligenceResourceId,
    [string] $ReceiverUrl,
    [string] $ReceiverCheckUrl,
    [switch] $PublicVantage,
    [switch] $ConfirmSyntheticProbeAuthority,
    [string] $ObservationOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$dependencyNames = @('SourceStorage', 'Sql', 'KeyVault', 'DocumentIntelligence')
$approvedProbeHost = 'login.microsoftonline.com'

$results = [ordered]@{
    'DEP-06' = [System.Collections.Generic.List[string]]::new()
    'DEP-07' = [System.Collections.Generic.List[string]]::new()
    'DEP-08' = [System.Collections.Generic.List[string]]::new()
    'DEP-09' = [System.Collections.Generic.List[string]]::new()
}
$unexecuted = [System.Collections.Generic.HashSet[string]]::new()

function Get-Value {
    param($Object, [string[]] $Path)
    $current = $Object
    foreach ($segment in $Path) {
        if ($current -is [System.Collections.IDictionary] -and $current.Contains($segment)) {
            $current = $current[$segment]
        }
        else {
            return $null
        }
    }
    return $current
}

function Test-PrivateAddress {
    param([string] $Candidate)
    $address = $null
    if (-not [System.Net.IPAddress]::TryParse($Candidate, [ref] $address) -or
        $address.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
        return $false
    }
    $bytes = $address.GetAddressBytes()
    return ($bytes[0] -eq 10) -or
           ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or
           ($bytes[0] -eq 192 -and $bytes[1] -eq 168)
}

# ------------------------------------------------------------- evaluator
function Invoke-Evaluation {
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Observations)

    $workers = @(Get-Value $Observations @('workers'))
    $workers = @($workers | Where-Object { $_ -is [System.Collections.IDictionary] })

    # ---- DEP-06: per-worker private resolution
    if ($workers.Count -lt 2) {
        $results['DEP-06'].Add('FewerThanTwoWorkersObserved')
    }
    foreach ($name in $dependencyNames) {
        $endpoint = @(Get-Value $Observations @('privateEndpointAddresses', $name) | Where-Object { $_ })
        if ($endpoint.Count -eq 0 -or @($endpoint | Where-Object { -not (Test-PrivateAddress $_) }).Count -gt 0) {
            $results['DEP-06'].Add("PrivateEndpointAddressUnknown ($name)")
            continue
        }
        foreach ($worker in $workers) {
            $resolved = @(Get-Value $worker @('resolutions', $name) | Where-Object { $_ })
            if ($resolved.Count -eq 0) {
                $results['DEP-06'].Add("ResolutionMissing ($name)")
            }
            elseif (@($resolved | Where-Object { $_ -notin $endpoint -or -not (Test-PrivateAddress $_) }).Count -gt 0) {
                $results['DEP-06'].Add("ResolvesOutsidePrivateEndpoint ($name)")
            }
        }
    }

    # ---- DEP-07: public access disabled and denied
    foreach ($name in $dependencyNames) {
        if ((Get-Value $Observations @('publicNetworkAccess', $name)) -cne 'Disabled') {
            $results['DEP-07'].Add("PublicNetworkAccessNotDisabled ($name)")
        }
        switch (Get-Value $Observations @('publicProbe', $name)) {
            'Denied' { }
            'Unexecuted' { [void] $unexecuted.Add('DEP-07') }
            default { $results['DEP-07'].Add("PublicEndpointReachable ($name)") }
        }
    }

    # ---- DEP-08: routing and deny-default egress with a positive control
    $routes = Get-Value $Observations @('routes')
    if ((Get-Value $routes @('applicationRouteAll')) -ne $true) {
        $results['DEP-08'].Add('ApplicationRoutingNotForced')
    }
    foreach ($kind in @('contentShare', 'imagePull', 'backupRestore')) {
        if ((Get-Value $routes @('configurationRouting', $kind)) -ne $true) {
            $results['DEP-08'].Add("ConfigurationRoutingNotForced ($kind)")
        }
    }
    $firewall = [string] (Get-Value $routes @('firewallPrivateAddress'))
    if ((Get-Value $routes @('defaultRoute', 'nextHopType')) -cne 'VirtualAppliance' -or
        -not (Test-PrivateAddress $firewall) -or
        [string] (Get-Value $routes @('defaultRoute', 'nextHopIpAddress')) -cne $firewall) {
        $results['DEP-08'].Add('DefaultRouteBypass')
    }
    if ((Get-Value $routes @('otherInternetRoutes')) -ne 0) {
        $results['DEP-08'].Add('InternetRouteBypass')
    }
    if ($workers.Count -lt 2) {
        $results['DEP-08'].Add('FewerThanTwoWorkersObserved')
    }
    foreach ($worker in $workers) {
        if ((Get-Value $worker @('approvedEgress', 'connected')) -ne $true) {
            $results['DEP-08'].Add('PositiveControlNotObserved')
        }
        if ((Get-Value $worker @('unapprovedEgress', 'attempted')) -ne $true) {
            $results['DEP-08'].Add('UnapprovedEgressNotAttempted')
        }
        elseif ((Get-Value $worker @('unapprovedEgress', 'connected')) -ne $false) {
            $results['DEP-08'].Add('UnapprovedEgressAllowed')
        }
    }

    # ---- DEP-09: synthetic byte non-delivery with a positive receive detector
    $synthetic = Get-Value $Observations @('syntheticEgress')
    if ((Get-Value $synthetic @('positiveControlDetected')) -ne $true) {
        $results['DEP-09'].Add('ReceiveDetectorNotProven')
    }
    $sent = @(Get-Value $synthetic @('workerMarkersSent') | Where-Object { $_ -ne $null })
    if ($workers.Count -lt 2 -or $sent.Count -ne $workers.Count) {
        $results['DEP-09'].Add('SyntheticTransmissionNotAttemptedFromEveryWorker')
    }
    if ((Get-Value $synthetic @('workerMarkerDetected')) -ne $false) {
        $results['DEP-09'].Add('SyntheticPayloadDelivered')
    }
}

# ------------------------------------------------------------ live collector
function Invoke-Az {
    param([string[]] $Arguments)
    $raw = & az @Arguments --only-show-errors --output json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $raw) {
        return $null
    }
    return (($raw -join "`n") | ConvertFrom-Json -AsHashtable)
}

function Invoke-WorkerCommand {
    param([string] $ScmHost, [string] $Instance, [string] $Token, [string] $Command)
    try {
        $response = Invoke-RestMethod -Method Post -Uri "https://$ScmHost/api/command" `
            -Headers @{ Authorization = "Bearer $Token"; Cookie = "ARRAffinity=$Instance" } `
            -ContentType 'application/json' -Body (@{ command = $Command; dir = '/home' } | ConvertTo-Json) `
            -TimeoutSec 60
        return $response
    }
    catch {
        return $null
    }
}

function Get-LiveObservations {
    $resources = [ordered]@{
        SourceStorage = $SourceStorageResourceId
        Sql = $SqlServerResourceId
        KeyVault = $KeyVaultResourceId
        DocumentIntelligence = $DocumentIntelligenceResourceId
    }
    $observations = [ordered]@{
        collectedBy = 'Live'
        workers = [System.Collections.Generic.List[object]]::new()
        privateEndpointAddresses = [ordered]@{}
        publicNetworkAccess = [ordered]@{}
        publicProbe = [ordered]@{}
        routes = [ordered]@{}
        syntheticEgress = [ordered]@{}
    }
    $hosts = @{}

    foreach ($name in $dependencyNames) {
        $resource = Invoke-Az @('resource', 'show', '--ids', $resources[$name])
        if ($null -eq $resource) {
            return $null
        }
        $observations.publicNetworkAccess[$name] = [string] (Get-Value $resource @('properties', 'publicNetworkAccess'))
        $hosts[$name] = switch ($name) {
            'SourceStorage' { ([Uri] (Get-Value $resource @('properties', 'primaryEndpoints', 'blob'))).Host }
            'Sql' { [string] (Get-Value $resource @('properties', 'fullyQualifiedDomainName')) }
            'KeyVault' { ([Uri] (Get-Value $resource @('properties', 'vaultUri'))).Host }
            'DocumentIntelligence' { ([Uri] (Get-Value $resource @('properties', 'endpoint'))).Host }
        }

        $addresses = [System.Collections.Generic.List[string]]::new()
        foreach ($connection in @(Get-Value $resource @('properties', 'privateEndpointConnections'))) {
            $endpointId = [string] (Get-Value $connection @('properties', 'privateEndpoint', 'id'))
            $endpoint = Invoke-Az @('network', 'private-endpoint', 'show', '--ids', $endpointId)
            foreach ($config in @(Get-Value $endpoint @('customDnsConfigs'))) {
                foreach ($address in @(Get-Value $config @('ipAddresses'))) {
                    $addresses.Add([string] $address)
                }
            }
        }
        $observations.privateEndpointAddresses[$name] = $addresses.ToArray()

        if (-not $PublicVantage) {
            $observations.publicProbe[$name] = 'Unexecuted'
        }
        elseif ($name -eq 'Sql') {
            if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
                $observations.publicProbe[$name] = 'Unexecuted'
            }
            else {
                $text = (& sqlcmd -S "tcp:$($hosts[$name]),1433" -G -l 15 -Q 'SELECT 1' 2>&1) -join "`n"
                $observations.publicProbe[$name] = if ($text -match '47073|Deny Public Network Access') { 'Denied' } else { 'Reachable' }
            }
        }
        else {
            try {
                Invoke-WebRequest -Uri "https://$($hosts[$name])/" -Method Get -TimeoutSec 20 -SkipHttpErrorCheck -ErrorAction Stop |
                    ForEach-Object {
                        $denied = $_.StatusCode -eq 403 -and
                                  $_.Content -match '(?i)public network access|PublicNetworkAccess|ForbiddenByConnection|not authorized to access|network'
                        $observations.publicProbe[$name] = if ($denied) { 'Denied' } else { 'Reachable' }
                    }
            }
            catch {
                # No connection through the public endpoint is a denial.
                $observations.publicProbe[$name] = 'Denied'
            }
        }
    }

    $site = Invoke-Az @('webapp', 'show', '--subscription', $SubscriptionId, '--resource-group', $ResourceGroup, '--name', $SiteName)
    $siteResource = Invoke-Az @('resource', 'show', '--ids', "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Web/sites/$SiteName")
    if ($null -eq $site -or $null -eq $siteResource) {
        return $null
    }
    $observations.routes.applicationRouteAll = [bool] (Get-Value $siteResource @('properties', 'vnetRouteAllEnabled'))
    $observations.routes.configurationRouting = [ordered]@{
        contentShare = [bool] (Get-Value $siteResource @('properties', 'vnetContentShareEnabled'))
        imagePull = [bool] (Get-Value $siteResource @('properties', 'vnetImagePullEnabled'))
        backupRestore = [bool] (Get-Value $siteResource @('properties', 'vnetBackupRestoreEnabled'))
    }

    $subnet = Invoke-Az @('network', 'vnet', 'subnet', 'show', '--ids', [string] (Get-Value $siteResource @('properties', 'virtualNetworkSubnetId')))
    $routeTable = Invoke-Az @('network', 'route-table', 'show', '--ids', [string] (Get-Value $subnet @('routeTable', 'id')))
    $firewalls = Invoke-Az @('network', 'firewall', 'list', '--subscription', $SubscriptionId, '--resource-group', $ResourceGroup)
    $defaultRoutes = @(Get-Value $routeTable @('routes') | Where-Object { (Get-Value $_ @('addressPrefix')) -eq '0.0.0.0/0' })
    $observations.routes.defaultRoute = if ($defaultRoutes.Count -eq 1) {
        [ordered]@{
            nextHopType = [string] (Get-Value $defaultRoutes[0] @('nextHopType'))
            nextHopIpAddress = [string] (Get-Value $defaultRoutes[0] @('nextHopIpAddress'))
        }
    } else { $null }
    $observations.routes.otherInternetRoutes = @(Get-Value $routeTable @('routes') | Where-Object { (Get-Value $_ @('nextHopType')) -eq 'Internet' }).Count
    $observations.routes.firewallPrivateAddress = $null
    if (@($firewalls).Count -eq 1) {
        $observations.routes.firewallPrivateAddress = [string] @(@($firewalls)[0]['ipConfigurations'])[0]['privateIPAddress']
    }

    $instances = @(Invoke-Az @('webapp', 'list-instances', '--subscription', $SubscriptionId, '--resource-group', $ResourceGroup, '--name', $SiteName) | ForEach-Object { [string] $_['name'] })
    $token = (Invoke-Az @('account', 'get-access-token', '--subscription', $SubscriptionId))['accessToken']
    $scmHost = @($site['enabledHostNames'] | Where-Object { $_ -like '*.scm.*' })[0]
    if ($instances.Count -eq 0 -or -not $token -or -not $scmHost) {
        return $null
    }

    # A fresh random synthetic marker. It is not derived from, and never
    # accompanies, any taxpayer value.
    $marker = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
    $sent = [System.Collections.Generic.List[string]]::new()

    foreach ($instance in $instances) {
        $worker = [ordered]@{ instance = [guid]::NewGuid().ToString('N'); resolutions = [ordered]@{} }
        foreach ($name in $dependencyNames) {
            $response = Invoke-WorkerCommand $scmHost $instance $token "getent ahostsv4 $($hosts[$name])"
            if ($null -eq $response) {
                return $null
            }
            $worker.resolutions[$name] = @(([string] $response.Output) -split "`n" |
                ForEach-Object { ($_ -split '\s+')[0] } | Where-Object { $_ } | Sort-Object -Unique)
        }

        $approved = Invoke-WorkerCommand $scmHost $instance $token "curl -sS -o /dev/null -m 15 -w '%{http_code}' https://$approvedProbeHost/common/discovery/v2.0/keys"
        $worker.approvedEgress = [ordered]@{ connected = ($null -ne $approved -and [string] $approved.Output -match '^[1-5]\d\d$' -and [string] $approved.Output -ne '000') }

        $unapproved = Invoke-WorkerCommand $scmHost $instance $token "curl -sS -o /dev/null -m 15 -w '%{http_code}' $ReceiverUrl"
        $worker.unapprovedEgress = [ordered]@{
            attempted = $null -ne $unapproved
            connected = $null -ne $unapproved -and [string] $unapproved.Output -match '^[1-5]\d\d$'
        }

        $post = Invoke-WorkerCommand $scmHost $instance $token "curl -sS -o /dev/null -m 15 -X POST --data-binary $marker $ReceiverUrl"
        if ($null -ne $post) {
            $sent.Add($worker.instance)
        }
        $observations.workers.Add($worker)
    }

    # Positive receive detector: the runner posts a second, distinct synthetic
    # marker directly; the receiver must report it.
    $control = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
    try {
        Invoke-RestMethod -Method Post -Uri $ReceiverUrl -Body $control -ContentType 'application/octet-stream' -TimeoutSec 30 | Out-Null
        $controlSeen = (Invoke-RestMethod -Method Get -Uri "$ReceiverCheckUrl$control" -TimeoutSec 30).detected -eq $true
        $workerSeen = (Invoke-RestMethod -Method Get -Uri "$ReceiverCheckUrl$marker" -TimeoutSec 30).detected -eq $true
    }
    catch {
        return $null
    }
    $observations.syntheticEgress = [ordered]@{
        positiveControlDetected = $controlSeen
        workerMarkersSent = $sent.ToArray()
        workerMarkerDetected = $workerSeen
    }

    return $observations
}

# ------------------------------------------------------------------ main
$observations = $null
$label = 'SUPPLIED OBSERVATIONS (evaluation only - not deployed proof)'

if ($Mode -eq 'Evaluate') {
    if (-not $ObservationPath) {
        Write-Host 'FATAL: Evaluate mode requires -ObservationPath.' -ForegroundColor Red
        exit 2
    }
    try {
        $observations = Get-Content -LiteralPath $ObservationPath -Raw | ConvertFrom-Json -AsHashtable
    }
    catch {
        Write-Host 'FATAL: the observation file could not be read as JSON.' -ForegroundColor Red
        exit 2
    }
    if ($observations -isnot [System.Collections.IDictionary]) {
        Write-Host 'FATAL: the observation file must contain one JSON object.' -ForegroundColor Red
        exit 2
    }
}
else {
    $required = @($SubscriptionId, $ResourceGroup, $SiteName, $SourceStorageResourceId, $SqlServerResourceId,
        $KeyVaultResourceId, $DocumentIntelligenceResourceId, $ReceiverUrl, $ReceiverCheckUrl)
    if (@($required | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0 -or -not $ConfirmSyntheticProbeAuthority) {
        Write-Host 'FATAL: Live mode requires every target argument, the synthetic receiver URLs and -ConfirmSyntheticProbeAuthority.' -ForegroundColor Red
        exit 2
    }
    if ($ReceiverUrl -match "(?i)$([regex]::Escape($approvedProbeHost))") {
        Write-Host 'FATAL: the synthetic receiver must be an unapproved destination.' -ForegroundColor Red
        exit 2
    }
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        Write-Host 'UNEXECUTED: the Azure CLI is unavailable, so DEP-06 to DEP-09 were not observed. Record this as unexecuted, not passed.' -ForegroundColor Yellow
        exit 3
    }
    $observations = Get-LiveObservations
    if ($null -eq $observations) {
        Write-Host 'UNEXECUTED: the deployed resources or workers could not be observed with the supplied access. Record this as unexecuted, not passed.' -ForegroundColor Yellow
        exit 3
    }
    if ($ObservationOutputPath) {
        $observations | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ObservationOutputPath -Encoding utf8
    }
    $label = 'OBSERVED LIVE against the named resources'
}

Invoke-Evaluation -Observations $observations

Write-Host "Private network probe: $label"
$refused = $false
foreach ($gate in $results.Keys) {
    $codes = @($results[$gate] | Sort-Object -Unique)
    if ($codes.Count -gt 0) {
        $refused = $true
        Write-Host "  $gate REFUSED" -ForegroundColor Red
        foreach ($code in $codes) {
            Write-Host "    - $code" -ForegroundColor Red
        }
    }
    elseif ($unexecuted.Contains($gate)) {
        Write-Host "  $gate UNEXECUTED" -ForegroundColor Yellow
    }
    else {
        Write-Host "  $gate PASS"
    }
}

if ($refused) {
    exit 1
}
if ($unexecuted.Count -gt 0) {
    exit 3
}
exit 0
