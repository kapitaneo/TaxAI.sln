<#
.SYNOPSIS
    Offline infrastructure-plan check for the TaxAI regional topology
    (spec 016 T02).

.DESCRIPTION
    Two stages, both offline. Neither contacts Azure, and passing both proves
    nothing about deployed resources.

      Compile  Restores the exact Bicep CLI pinned in
               infra/azure/contracts/toolchain.json from its official NuGet
               package, verifies the package SHA-512 before executing it,
               then builds and lints infra/azure/main.bicep (every module) and
               builds every parameter file. Any error or warning refuses.
               Parameter types enforce every bound (regions, minimum capacity
               of two, closed parameter objects).

      Policy   Evaluates the reviewed security posture of the templates and
               contracts: P1v3, zone redundancy and a minimum of two workers;
               WAF v2 Prevention with public 443 as the only listener; HTTPS
               re-encryption with host-name validation and no plaintext
               backend; private-only App Service and SCM; application and
               configuration traffic routed into the network; a deny-default
               route table, NSGs and firewall; exactly five private endpoint
               bindings with private DNS; the closed outbound allowlist; no
               diagnostic setting and no preview.

    Output names closed refusal codes only; it never prints a configured value.

    Exit codes:
      0  every executed stage passed
      1  one or more refusals
      2  the check could not run (unreadable input or bad arguments)
      3  policy passed but the compile stage was not executed (the pinned
         toolchain could not be restored). Record as unexecuted, never passed.

.PARAMETER InfraRoot
    The infra/azure directory to check. Defaults to this repository's.

.PARAMETER Stage
    All (default), Policy or Compile.

.PARAMETER ToolchainCache
    Where the pinned Bicep package is restored. Defaults to a per-user temp
    directory outside the repository.

.EXAMPLE
    pwsh ./scripts/azure/Test-ProductionPlan.ps1
#>
[CmdletBinding()]
param(
    [string] $InfraRoot,

    [ValidateSet('All', 'Policy', 'Compile')]
    [string] $Stage = 'All',

    [string] $ToolchainCache
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $InfraRoot) {
    $InfraRoot = Join-Path $repositoryRoot 'infra/azure'
}
if (-not (Test-Path -LiteralPath (Join-Path $InfraRoot 'main.bicep'))) {
    Write-Host 'FATAL: the infrastructure root does not contain main.bicep.' -ForegroundColor Red
    exit 2
}

$refusals = [System.Collections.Generic.List[string]]::new()

function Add-Refusal {
    param([Parameter(Mandatory)][string] $Code, [Parameter(Mandatory)][string] $Subject)
    $refusals.Add("$Code ($Subject)")
}

function Read-Source {
    param([Parameter(Mandatory)][string] $RelativePath)
    $path = Join-Path $InfraRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        return $null
    }
    # Whole-line comments are commentary, never posture. Line endings are
    # normalized so a check reads the same on every platform.
    $text = (Get-Content -LiteralPath $path -Raw).Replace("`r`n", "`n")
    return [regex]::Replace($text, '(?m)^\s*//.*$', '')
}

# ------------------------------------------------------------ policy rules
# Kind Require: the pattern must match. Forbid: it must not match.
# Count: it must match exactly Expected times.
$rules = @(
    # ---- hosting (FR-02, AC-018 to AC-020)
    @{ Code = 'PlanSkuNotP1v3'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = "name: 'P1v3'\s+tier: 'PremiumV3'" }
    # Spec 016 T06: zone redundant exactly where the region offers zones (Canada
    # Central); the manually activated Canada East recovery host is non-zonal.
    @{ Code = 'ZoneRedundancyDisabled'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = "var zoneRedundant = location == 'canadacentral'" }
    @{ Code = 'ZoneRedundancyDisabled'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = 'zoneRedundant: zoneRedundant' }
    @{ Code = 'ZoneRedundancyDisabled'; File = 'modules/app-service.bicep'; Kind = 'Forbid'; Pattern = 'zoneRedundant: false' }
    @{ Code = 'PerSiteScalingEnabled'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = 'perSiteScaling: false' }
    @{ Code = 'MinimumWorkersBelowTwo'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = '@minValue\(2\)\s+@maxValue\(10\)\s+param minimumInstanceCount int' }
    @{ Code = 'MinimumWorkersBelowTwo'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = 'capacity: minimumInstanceCount' }
    @{ Code = 'AutoscaleMinimumBelowTwo'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = 'minimum: string\(minimumInstanceCount\)\s+default: string\(minimumInstanceCount\)' }
    @{ Code = 'AutoscaleMinimumBelowTwo'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = '@minValue\(2\)\s+@maxValue\(10\)\s+param maximumInstanceCount int' }
    @{ Code = 'AutoscaleMinimumBelowTwo'; File = 'modules/app-service.bicep'; Kind = 'Count'; Expected = 1; Pattern = 'profiles: \[' }
    @{ Code = 'AutoscaleMinimumBelowTwo'; File = 'modules/app-service.bicep'; Kind = 'Count'; Expected = 1; Pattern = "name: '[^']+'\s+capacity: \{" }
    @{ Code = 'AutoscaleMinimumBelowTwo'; File = 'modules/app-service.bicep'; Kind = 'Forbid'; Pattern = "(?:minimum|default): '" }
    @{ Code = 'AutoscaleMinimumBelowTwo'; File = 'modules/app-service.bicep'; Kind = 'Forbid'; Pattern = "type: '(?:ExactCount|PercentChangeCount)'" }

    # ---- private origin and SCM (AC-021, AC-022)
    @{ Code = 'PortalPublicNetworkAccessEnabled'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = "publicNetworkAccess: 'Disabled'" }
    @{ Code = 'HttpsOnlyDisabled'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = 'httpsOnly: true' }
    @{ Code = 'PublicOriginBypass'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = "ipSecurityRestrictionsDefaultAction: 'Deny'" }
    @{ Code = 'PublicOriginBypass'; File = 'modules/app-service.bicep'; Kind = 'Forbid'; Pattern = 'ipSecurityRestrictions:\s' }
    @{ Code = 'ScmBypass'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = "scmIpSecurityRestrictionsDefaultAction: 'Deny'" }
    @{ Code = 'ScmBypass'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = 'scmIpSecurityRestrictionsUseMain: false' }
    @{ Code = 'ScmBypass'; File = 'modules/app-service.bicep'; Kind = 'Forbid'; Pattern = 'scmIpSecurityRestrictions:\s' }
    @{ Code = 'ScmBypass'; File = 'modules/app-service.bicep'; Kind = 'Count'; Expected = 2; Pattern = 'allow: false' }
    @{ Code = 'ScmBypass'; File = 'modules/app-service.bicep'; Kind = 'Forbid'; Pattern = 'allow: true' }
    @{ Code = 'ScmBypass'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = "ftpsState: 'Disabled'" }
    @{ Code = 'TransportBelowMinimum'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = "\bminTlsVersion: '1\.2'" }
    @{ Code = 'TransportBelowMinimum'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = "scmMinTlsVersion: '1\.2'" }
    @{ Code = 'UnsafeDiagnosticEnabled'; File = 'modules/app-service.bicep'; Kind = 'Forbid'; Pattern = '(?:httpLoggingEnabled|detailedErrorLoggingEnabled|requestTracingEnabled|remoteDebuggingEnabled): true' }

    # ---- VNet application AND configuration routing (AC-058)
    @{ Code = 'ApplicationRoutingNotForced'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = 'vnetRouteAllEnabled: true' }
    @{ Code = 'ApplicationRoutingNotForced'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = 'virtualNetworkSubnetId: appIntegrationSubnetId' }
    @{ Code = 'ConfigurationRoutingNotForced'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = 'vnetContentShareEnabled: true' }
    @{ Code = 'ConfigurationRoutingNotForced'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = 'vnetImagePullEnabled: true' }
    @{ Code = 'ConfigurationRoutingNotForced'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = 'vnetBackupRestoreEnabled: true' }

    # ---- questionnaire and trusted forwarding (AC-024)
    @{ Code = 'InteractiveTransportDisabled'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = 'webSocketsEnabled: true' }
    @{ Code = 'PlatformForwardingEnabled'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = "ASPNETCORE_FORWARDEDHEADERS_ENABLED: 'false'" }

    # ---- WAF v2 ingress (AC-021, AC-023, AC-154)
    @{ Code = 'GatewayNotWafV2'; File = 'modules/application-gateway.bicep'; Kind = 'Require'; Pattern = "name: 'WAF_v2'\s+tier: 'WAF_v2'" }
    @{ Code = 'WafNotEnforcing'; File = 'modules/application-gateway.bicep'; Kind = 'Require'; Pattern = "state: 'Enabled'\s+mode: 'Prevention'" }
    @{ Code = 'WafNotEnforcing'; File = 'modules/application-gateway.bicep'; Kind = 'Require'; Pattern = 'requestBodyCheck: true' }
    @{ Code = 'WafNotEnforcing'; File = 'modules/application-gateway.bicep'; Kind = 'Require'; Pattern = "ruleSetType: 'Microsoft_DefaultRuleSet'" }
    @{ Code = 'WafNotEnforcing'; File = 'modules/application-gateway.bicep'; Kind = 'Forbid'; Pattern = '(?:exclusions|customRules|ruleGroupOverrides):' }
    @{ Code = 'GatewayMinimumBelowTwo'; File = 'modules/application-gateway.bicep'; Kind = 'Require'; Pattern = '@minValue\(2\)\s+@maxValue\(10\)\s+param minimumCapacity int' }
    @{ Code = 'GatewayMinimumBelowTwo'; File = 'modules/application-gateway.bicep'; Kind = 'Require'; Pattern = 'minCapacity: minimumCapacity' }
    @{ Code = 'PublicListenerNotHttps443Only'; File = 'modules/application-gateway.bicep'; Kind = 'Count'; Expected = 1; Pattern = 'port: \d+\s+\}\s+\}\s+\]\s+sslCertificates' }
    @{ Code = 'PublicListenerNotHttps443Only'; File = 'modules/application-gateway.bicep'; Kind = 'Forbid'; Pattern = 'port: (?!443\b)\d+' }
    @{ Code = 'PublicListenerNotHttps443Only'; File = 'modules/application-gateway.bicep'; Kind = 'Count'; Expected = 1; Pattern = "frontendPorts: \[\s+\{\s+name: 'https'\s+properties: \{\s+port: 443\s+\}\s+\}\s+\]" }
    @{ Code = 'PublicListenerNotHttps443Only'; File = 'modules/application-gateway.bicep'; Kind = 'Forbid'; Pattern = '(?:redirectConfigurations|privateIPAllocationMethod|privateIPAddress):' }
    @{ Code = 'PlaintextForwardingConfigured'; File = 'modules/application-gateway.bicep'; Kind = 'Forbid'; Pattern = "protocol: 'Http'" }
    @{ Code = 'PlaintextForwardingConfigured'; File = 'modules/application-gateway.bicep'; Kind = 'Count'; Expected = 3; Pattern = "protocol: 'Https'" }
    @{ Code = 'BackendNotCertificateValidated'; File = 'modules/application-gateway.bicep'; Kind = 'Require'; Pattern = "protocol: 'Https'\s+port: 443\s+hostName: publicHostName\s+pickHostNameFromBackendAddress: false" }
    @{ Code = 'BackendNotCertificateValidated'; File = 'modules/application-gateway.bicep'; Kind = 'Forbid'; Pattern = '(?:validateCertChainAndExpiry|validateSNI): false' }
    @{ Code = 'BackendNotCertificateValidated'; File = 'modules/application-gateway.bicep'; Kind = 'Forbid'; Pattern = 'trustedRootCertificates:' }
    @{ Code = 'TransportBelowMinimum'; File = 'modules/application-gateway.bicep'; Kind = 'Require'; Pattern = "policyType: 'Predefined'\s+policyName: 'AppGwSslPolicy20220101S'" }
    @{ Code = 'ZoneRedundancyDisabled'; File = 'modules/application-gateway.bicep'; Kind = 'Require'; Pattern = "var zones = location == 'canadacentral' \? \[\s+'1'\s+'2'\s+'3'\s+\] : \[\]" }
    @{ Code = 'ZoneRedundancyDisabled'; File = 'modules/application-gateway.bicep'; Kind = 'Count'; Expected = 2; Pattern = 'zones: zones' }
    @{ Code = 'ZoneRedundancyDisabled'; File = 'modules/egress.bicep'; Kind = 'Require'; Pattern = "var zones = location == 'canadacentral' \? \[\s+'1'\s+'2'\s+'3'\s+\] : \[\]" }
    @{ Code = 'ZoneRedundancyDisabled'; File = 'modules/egress.bicep'; Kind = 'Count'; Expected = 2; Pattern = 'zones: zones' }

    # ---- Spec 016 T06: hosting modules serve exactly the two Canadian regions
    @{ Code = 'RegionNotApproved'; File = 'modules/app-service.bicep'; Kind = 'Require'; Pattern = "@allowed\(\[\s+'canadacentral'\s+'canadaeast'\s+\]\)\s+param location string" }
    @{ Code = 'RegionNotApproved'; File = 'modules/application-gateway.bicep'; Kind = 'Require'; Pattern = "@allowed\(\[\s+'canadacentral'\s+'canadaeast'\s+\]\)\s+param location string" }
    @{ Code = 'RegionNotApproved'; File = 'modules/egress.bicep'; Kind = 'Require'; Pattern = "@allowed\(\[\s+'canadacentral'\s+'canadaeast'\s+\]\)\s+param location string" }
    @{ Code = 'RegionNotApproved'; File = 'modules/private-endpoints.bicep'; Kind = 'Require'; Pattern = "@allowed\(\[\s+'canadacentral'\s+'canadaeast'\s+\]\)\s+param location string" }

    # ---- deny-default network and egress (AC-057, AC-058)
    @{ Code = 'DefaultRouteBypass'; File = 'modules/network.bicep'; Kind = 'Require'; Pattern = "addressPrefix: '0\.0\.0\.0/0'\s+nextHopType: 'VirtualAppliance'\s+nextHopIpAddress: firewallPrivateAddress" }
    @{ Code = 'DefaultRouteBypass'; File = 'modules/network.bicep'; Kind = 'Require'; Pattern = "addressPrefix: '0\.0\.0\.0/0'\s+nextHopType: 'None'" }
    @{ Code = 'DefaultRouteBypass'; File = 'modules/network.bicep'; Kind = 'Forbid'; Pattern = "nextHopType: '(?:Internet|VirtualNetworkGateway)'" }
    @{ Code = 'DefaultRouteBypass'; File = 'modules/network.bicep'; Kind = 'Require'; Pattern = 'disableBgpRoutePropagation: true' }
    @{ Code = 'DefaultRouteBypass'; File = 'modules/network.bicep'; Kind = 'Count'; Expected = 3; Pattern = 'defaultOutboundAccess: false' }
    @{ Code = 'DefaultRouteBypass'; File = 'modules/network.bicep'; Kind = 'Require'; Pattern = 'routeTable: \{\s+id: egressRoutes\.id' }
    @{ Code = 'OutboundNotDenyDefault'; File = 'modules/network.bicep'; Kind = 'Count'; Expected = 1; Pattern = "destinationAddressPrefix: 'Internet'" }
    @{ Code = 'OutboundNotDenyDefault'; File = 'modules/network.bicep'; Kind = 'Require'; Pattern = "name: 'DenyAllOtherOutbound'\s+properties: \{\s+priority: 4000\s+direction: 'Outbound'\s+access: 'Deny'" }
    @{ Code = 'OutboundNotDenyDefault'; File = 'modules/network.bicep'; Kind = 'Require'; Pattern = "destinationAddressPrefix: 'Internet'\s+destinationPortRanges: \[\s+'80'\s+'443'\s+\]" }
    @{ Code = 'OutboundNotDenyDefault'; File = 'modules/network.bicep'; Kind = 'Forbid'; Pattern = "direction: 'Outbound'\s+access: 'Allow'\s+protocol: '\*'" }
    @{ Code = 'InboundNotDenyDefault'; File = 'modules/network.bicep'; Kind = 'Count'; Expected = 3; Pattern = "direction: 'Inbound'\s+access: 'Deny'\s+protocol: '\*'\s+sourceAddressPrefix: '\*'" }
    @{ Code = 'InboundNotDenyDefault'; File = 'modules/network.bicep'; Kind = 'Count'; Expected = 1; Pattern = "sourceAddressPrefix: 'Internet'" }
    @{ Code = 'PrivateEndpointPolicyDisabled'; File = 'modules/network.bicep'; Kind = 'Require'; Pattern = "privateEndpointNetworkPolicies: 'Enabled'" }
    @{ Code = 'OutboundNotDenyDefault'; File = 'modules/egress.bicep'; Kind = 'Require'; Pattern = "threatIntelMode: 'Deny'" }
    @{ Code = 'OutboundNotDenyDefault'; File = 'modules/egress.bicep'; Kind = 'Require'; Pattern = "loadJsonContent\('\.\./contracts/dependency-allowlist\.json'\)" }
    @{ Code = 'OutboundNotDenyDefault'; File = 'modules/egress.bicep'; Kind = 'Count'; Expected = 1; Pattern = "type: 'Allow'" }
    @{ Code = 'OutboundNotDenyDefault'; File = 'modules/egress.bicep'; Kind = 'Forbid'; Pattern = "(?:NetworkRule|NatRule|fqdnTags|webCategories|targetUrls|destinationAddresses|natRuleCollections)" }
    @{ Code = 'OutboundNotDenyDefault'; File = 'modules/egress.bicep'; Kind = 'Require'; Pattern = 'sourceAddresses: \[\s+appIntegrationSubnetPrefix\s+\]' }
    @{ Code = 'TrafficInspectionConfigured'; File = 'modules/egress.bicep'; Kind = 'Forbid'; Pattern = 'terminateTLS: true|transportSecurity|intrusionDetection|explicitProxy' }
    @{ Code = 'TrafficInspectionConfigured'; File = 'modules/egress.bicep'; Kind = 'Require'; Pattern = "tier: 'Standard'\s+\}\s+threatIntelMode" }

    # ---- private endpoints and DNS (AC-049 to AC-052)
    @{ Code = 'PrivateBindingMissing'; File = 'modules/private-endpoints.bicep'; Kind = 'Count'; Expected = 6; Pattern = "groupId: '(?:sites|blob|sqlServer|vault|account)'" }
    @{ Code = 'PrivateBindingMissing'; File = 'modules/private-endpoints.bicep'; Kind = 'Require'; Pattern = "groupId: 'blob'\s+zone: 'SourceStorage'\s+resourceId: accountId" }
    @{ Code = 'PrivateBindingMissing'; File = 'modules/private-endpoints.bicep'; Kind = 'Require'; Pattern = '@maxLength\(3\)\s+param controlAccountResourceIds string\[\]' }
    @{ Code = 'PrivateBindingMissing'; File = 'modules/private-endpoints.bicep'; Kind = 'Count'; Expected = 5; Pattern = '@minLength\(1\)\s+param \w+ResourceId string' }
    @{ Code = 'PrivateBindingMissing'; File = 'modules/private-endpoints.bicep'; Kind = 'Require'; Pattern = 'privateDnsZoneId: zoneIds\[binding\.zone\]' }
    @{ Code = 'PrivateDnsNotBound'; File = 'modules/private-dns.bicep'; Kind = 'Require'; Pattern = 'registrationEnabled: false' }
    @{ Code = 'PrivateDnsNotBound'; File = 'modules/private-dns.bicep'; Kind = 'Count'; Expected = 5; Pattern = "(?m)^\s+(?:SourceStorage|Sql|KeyVault|DocumentIntelligence|Portal): '" }
    @{ Code = 'PrivateBindingMissing'; File = 'main.bicep'; Kind = 'Require'; Pattern = "module privateEndpoints 'modules/private-endpoints\.bicep' = if \(isPrimary\)" }
    @{ Code = 'OutboundNotDenyDefault'; File = 'main.bicep'; Kind = 'Require'; Pattern = "module egress 'modules/egress\.bicep' = if \(isPrimary\)" }
    @{ Code = 'OutboundNotDenyDefault'; File = 'main.bicep'; Kind = 'Require'; Pattern = "egressMode: \(isPrimary \|\| activating\) && profileInputsConsistent \? 'Firewall' : 'Blackhole'" }

    # ---- Spec 016 T06: the recovery host exists only by explicit activation
    @{ Code = 'RecoveryActivationUnguarded'; File = 'main.bicep'; Kind = 'Require'; Pattern = 'var activating = !isPrimary && activation != null' }
    @{ Code = 'RecoveryActivationUnguarded'; File = 'main.bicep'; Kind = 'Require'; Pattern = "fail\('The primary profile activates no recovery host\.'\)" }
    @{ Code = 'RecoveryActivationUnguarded'; File = 'main.bicep'; Kind = 'Require'; Pattern = "fail\('The recovery host deploys into its own Canada East resource group\.'\)" }
    @{ Code = 'RecoveryActivationUnguarded'; File = 'main.bicep'; Kind = 'Count'; Expected = 5; Pattern = "module recovery(?:Egress|Runtime|Portal|Endpoints|Ingress) '[^']+' = if \(activating\)" }
    @{ Code = 'RecoveryActivationUnguarded'; File = 'main.bicep'; Kind = 'Require'; Pattern = "profile: 'CanadaEastRecovery'" }
    @{ Code = 'RecoveryActivationUnguarded'; File = 'main.bicep'; Kind = 'Forbid'; Pattern = "module \w+ 'modules/document-intelligence\.bicep' = if \((?!isPrimary\))" }
    @{ Code = 'RegionNotApproved'; File = 'main.bicep'; Kind = 'Require'; Pattern = "@allowed\(\[\s+'canadacentral'\s+\]\)\s+param primaryRegion string" }
    @{ Code = 'RegionNotApproved'; File = 'main.bicep'; Kind = 'Require'; Pattern = "@allowed\(\[\s+'canadaeast'\s+\]\)\s+param recoveryRegion string" }
    @{ Code = 'PrimaryInputsNotClosed'; File = 'main.bicep'; Kind = 'Count'; Expected = 6; Pattern = '@sealed\(\)\s+type ' }
)

$moduleFiles = @(
    'main.bicep'
    'modules/app-service.bicep'
    'modules/application-gateway.bicep'
    'modules/egress.bicep'
    'modules/network.bicep'
    'modules/private-dns.bicep'
    'modules/private-endpoints.bicep'
    'modules/runtime-configuration.bicep'
)

$parameterFiles = @(
    'parameters/production.example.bicepparam'
    'parameters/recovery.example.bicepparam'
    'parameters/synthetic.bicepparam'
)

function Invoke-Policy {
    $sources = @{}
    foreach ($file in $moduleFiles + $parameterFiles) {
        $source = Read-Source $file
        if ($null -eq $source) {
            Add-Refusal 'PlanFileMissing' $file
            continue
        }
        $sources[$file] = $source
    }

    foreach ($rule in $rules) {
        if (-not $sources.Contains($rule.File)) {
            continue
        }
        $count = [regex]::Matches($sources[$rule.File], $rule.Pattern).Count
        $failed = switch ($rule.Kind) {
            'Require' { $count -eq 0 }
            'Forbid' { $count -gt 0 }
            'Count' { $count -ne $rule.Expected }
        }
        if ($failed) {
            Add-Refusal $rule.Code $rule.File
        }
    }

    # Nothing in the plan enables a diagnostic sink, a public endpoint or a
    # preview capability - in any module or parameter file.
    foreach ($file in $sources.Keys) {
        $source = $sources[$file]
        if ($source -match 'diagnosticSettings|diagnostic-settings') {
            Add-Refusal 'UnsafeDiagnosticEnabled' $file
        }
        if ($source -match "publicNetworkAccess: 'Enabled'") {
            Add-Refusal 'PublicEndpointConfigured' $file
        }
        if ($source -match '(?i)preview') {
            Add-Refusal 'PreviewFeatureConfigured' $file
        }
    }

    # Parameter files: approved regions, capacity never below two, and an
    # evidence manifest that grants nothing.
    foreach ($file in $parameterFiles) {
        if (-not $sources.Contains($file)) {
            continue
        }
        $source = $sources[$file]
        if ($source -notmatch "param primaryRegion = 'canadacentral'" -or
            $source -notmatch "param recoveryRegion = 'canadaeast'") {
            Add-Refusal 'RegionNotApproved' $file
        }
        foreach ($capacity in [regex]::Matches($source, '(?:minimumInstanceCount|maximumInstanceCount|minimumCapacity|maximumCapacity): (?<n>-?\d+)')) {
            if ([int] $capacity.Groups['n'].Value -lt 2) {
                Add-Refusal 'MinimumWorkersBelowTwo' $file
            }
        }
        foreach ($manifest in [regex]::Matches($source, "evidenceManifest: '(?<json>[^']*)'")) {
            try {
                $parsed = $manifest.Groups['json'].Value | ConvertFrom-Json -AsHashtable
                if (@($parsed.Keys) -join ',' -ne 'schemaVersion') {
                    Add-Refusal 'AffirmativeExampleEvidence' $file
                }
            }
            catch {
                Add-Refusal 'AffirmativeExampleEvidence' $file
            }
        }
    }
    if ($sources.Contains('parameters/recovery.example.bicepparam') -and
        $sources['parameters/recovery.example.bicepparam'] -match 'param (?:primary|activation)\b') {
        Add-Refusal 'RecoveryProfileActive' 'parameters/recovery.example.bicepparam'
    }

    Test-Allowlist
}

function Test-Allowlist {
    $file = 'contracts/dependency-allowlist.json'
    $path = Join-Path $InfraRoot $file
    try {
        $allowlist = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
    }
    catch {
        Add-Refusal 'AllowlistUnreadable' $file
        return
    }

    foreach ($destination in @($allowlist['publicDestinations'])) {
        $fqdn = [string] $destination['fqdn']
        $category = [string] $destination['category']
        $protocol = [string] $destination['protocol']
        $port = [int] $destination['port']
        if ($fqdn -cnotmatch '^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)+$') {
            Add-Refusal 'AllowlistDestinationNotExact' $file
        }
        $allowed = ($category -ceq 'identity' -and $fqdn -ceq 'login.microsoftonline.com' -and $protocol -ceq 'Https' -and $port -eq 443) -or
                   ($category -ceq 'certificateStatus' -and $protocol -ceq 'Http' -and $port -eq 80)
        if (-not $allowed) {
            Add-Refusal 'AllowlistDestinationNotApproved' $file
        }
        if (@($destination.Keys).Count -ne 4) {
            Add-Refusal 'AllowlistDestinationNotApproved' $file
        }
    }

    $names = @($allowlist['privateDependencies'] | ForEach-Object { [string] $_['name'] } | Sort-Object)
    if (($names -join ',') -ne 'DocumentIntelligence,KeyVault,SourceStorage,Sql') {
        Add-Refusal 'PrivateBindingMissing' $file
    }
    foreach ($dependency in @($allowlist['privateDependencies']) + @($allowlist['privateApplication'])) {
        if ([string] $dependency['zone'] -cnotmatch '^privatelink\.') {
            Add-Refusal 'PrivateDnsNotBound' $file
        }
        $publicHosts = @($allowlist['publicDestinations'] | ForEach-Object { [string] $_['fqdn'] })
        $suffix = ([string] $dependency['zone']).Substring('privatelink.'.Length)
        if (@($publicHosts | Where-Object { $_.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
            Add-Refusal 'PrivateDependencyPubliclyAllowlisted' $file
        }
    }
}

# ---------------------------------------------------------- compile stage
function Resolve-PinnedBicep {
    $toolchain = Get-Content -LiteralPath (Join-Path $InfraRoot 'contracts/toolchain.json') -Raw | ConvertFrom-Json -AsHashtable
    $bicep = $toolchain['bicep']
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $platform = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
    $runtime = "$platform-$architecture"
    if (-not $bicep['packages'].Contains($runtime)) {
        return $null
    }

    $cache = if ($ToolchainCache) { $ToolchainCache } else { Join-Path ([IO.Path]::GetTempPath()) 'taxai-toolchain' }
    $version = [string] $bicep['version']
    $package = "$($bicep['packageIdPrefix'])$runtime"
    $root = Join-Path $cache "$package.$version"
    $archive = "$root.nupkg"
    New-Item -ItemType Directory -Force -Path $cache | Out-Null

    if (-not (Test-Path -LiteralPath $archive)) {
        try {
            Invoke-WebRequest -Uri "$($bicep['source'])/$package/$version/$package.$version.nupkg" -OutFile $archive -TimeoutSec 300
        }
        catch {
            Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
            return $null
        }
    }

    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash.ToLowerInvariant()
    if ($hash -cne [string] $bicep['packages'][$runtime]) {
        Remove-Item -LiteralPath $archive -Force
        Add-Refusal 'ToolchainIntegrityFailed' 'contracts/toolchain.json'
        return $null
    }

    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $root
    $executable = Join-Path $root ($IsWindows ? 'tools/bicep.exe' : 'tools/bicep')
    if (-not $IsWindows) {
        & chmod +x $executable
    }
    $reported = (& $executable --version) -join ' '
    if ($reported -notmatch [regex]::Escape("Bicep CLI version $version ")) {
        Add-Refusal 'ToolchainVersionMismatch' 'contracts/toolchain.json'
        return $null
    }
    return $executable
}

function Invoke-Bicep {
    param([string] $Executable, [string[]] $Arguments, [string] $Subject)
    $output = & $Executable @Arguments 2>&1
    $text = ($output | ForEach-Object { [string] $_ }) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $text -match '(?i)\b(?:warning|error)\b') {
        Add-Refusal 'CompileOrLintFailed' $Subject
    }
}

function Invoke-Compile {
    $executable = Resolve-PinnedBicep
    if (-not $executable) {
        return $false
    }

    $out = Join-Path ([IO.Path]::GetTempPath()) "taxai-plan-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $out | Out-Null
    try {
        Invoke-Bicep $executable @('build', (Join-Path $InfraRoot 'main.bicep'), '--outdir', $out) 'main.bicep'
        Invoke-Bicep $executable @('lint', (Join-Path $InfraRoot 'main.bicep')) 'main.bicep'
        foreach ($file in $parameterFiles) {
            Invoke-Bicep $executable @('build-params', (Join-Path $InfraRoot $file), '--outfile', (Join-Path $out ([IO.Path]::GetFileName($file) + '.json'))) $file
        }
    }
    finally {
        Remove-Item -LiteralPath $out -Recurse -Force -ErrorAction SilentlyContinue
    }
    return $true
}

# ------------------------------------------------------------------ report
$compiled = $null
if ($Stage -in 'All', 'Policy') {
    Invoke-Policy
}
if ($Stage -in 'All', 'Compile') {
    $compiled = Invoke-Compile
}

if ($refusals.Count -gt 0) {
    Write-Host 'INFRASTRUCTURE PLAN REFUSED (offline check)' -ForegroundColor Red
    foreach ($refusal in ($refusals | Sort-Object -Unique)) {
        Write-Host "  - $refusal" -ForegroundColor Red
    }
    exit 1
}

if ($Stage -in 'All', 'Policy') {
    Write-Host 'Policy stage passed.'
}
if ($null -ne $compiled) {
    if (-not $compiled) {
        Write-Host 'UNEXECUTED: the pinned Bicep toolchain could not be restored, so the compile stage did not run. Record this as unexecuted, not passed.' -ForegroundColor Yellow
        exit 3
    }
    Write-Host 'Compile stage passed with the pinned toolchain.'
}
Write-Host 'Offline plan check only. This is not deployment evidence and not production authorization.'
exit 0
