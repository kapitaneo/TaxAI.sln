<#
.SYNOPSIS
    Checks a TaxAI production runtime configuration before it is deployed
    (spec 016 T01).

.DESCRIPTION
    Validates a JSON object of App Service application settings - the output of
    infra/azure/modules/runtime-configuration.bicep - against the closed contract
    in infra/azure/contracts/runtime-configuration.schema.json and the same
    cross-field binding rules the Portal's ProductionConfigurationValidator
    enforces at startup:

      - every required key present, no other key, every constant and pattern met;
      - each endpoint is the named resource's own host, and every resource id is
        in the configured subscription and resource group;
      - both Key Vault keys are versionless and in the configured vault, and the
        Data Protection key and account differ from the source ones;
      - the SQL connection targets the configured server as the configured
        managed identity with strict encryption and no stored credential;
      - no value names a preview feature.

    Output names settings and closed refusal codes only; it never prints a
    configured value.

    Modes:
      Offline (default)  Local structural check. Needs no Azure access and proves
                         nothing about deployed resources.
      Live               Read-only inventory check of region and public network
                         access for every configured resource id through the
                         Azure CLI. Requires -SubscriptionId and
                         -ConfirmReadOnlyInventoryAccess. When the CLI or access is
                         unavailable the check is recorded UNEXECUTED - never
                         passed and never skipped.

    Passing either mode does not authorize real-taxpayer processing. The Portal
    admission gate additionally verifies registrations, the issued managed
    identity, the exact artifact version and the release evidence.

    Exit codes:
      0  every check passed
      1  one or more refusals
      2  the check could not run (unreadable input or bad arguments)
      3  live mode requested but not executed

.PARAMETER SettingsPath
    JSON file containing one object of application settings.

.PARAMETER ComputeDigest
    Also print the configuration digest the release evidence manifest must name.

.EXAMPLE
    pwsh ./scripts/azure/Test-ProductionConfiguration.ps1 -SettingsPath ./out/appsettings.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SettingsPath,

    [ValidateSet('Offline', 'Live')]
    [string] $Mode = 'Offline',

    [string] $SchemaPath,

    [string] $SubscriptionId,

    [switch] $ConfirmReadOnlyInventoryAccess,

    [switch] $ComputeDigest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $SchemaPath) {
    $SchemaPath = Join-Path $repositoryRoot 'infra/azure/contracts/runtime-configuration.schema.json'
}

$refusals = [System.Collections.Generic.List[string]]::new()

function Add-Refusal {
    param([Parameter(Mandatory)][string] $Code, [string] $Setting)
    if ($Setting) {
        $refusals.Add("$Code ($Setting)")
    }
    else {
        $refusals.Add($Code)
    }
}

try {
    $schema = Get-Content -LiteralPath $SchemaPath -Raw | ConvertFrom-Json -AsHashtable
    $settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json -AsHashtable
}
catch {
    Write-Host 'FATAL: the settings or schema file could not be read as JSON.' -ForegroundColor Red
    exit 2
}

if ($settings -isnot [System.Collections.IDictionary]) {
    Write-Host 'FATAL: the settings file must contain one JSON object.' -ForegroundColor Red
    exit 2
}

$properties = $schema['properties']
$production = 'TaxAi__Production__'

function Get-Setting {
    param([Parameter(Mandatory)][string] $Name)
    if ($settings.Contains($Name) -and $null -ne $settings[$Name]) {
        return [string] $settings[$Name]
    }
    return $null
}

# ------------------------------------------------------- closed key set
foreach ($key in $settings.Keys) {
    if (-not $properties.Contains($key)) {
        Add-Refusal 'UnapprovedConfigurationEntry' $key
    }
}

foreach ($key in $schema['required']) {
    $value = Get-Setting $key
    if ([string]::IsNullOrWhiteSpace($value)) {
        Add-Refusal 'RequiredSettingMissing' $key
        continue
    }

    $rule = $properties[$key]
    if ($rule.Contains('const') -and -not [string]::Equals($value, [string]$rule['const'], [StringComparison]::Ordinal)) {
        Add-Refusal 'ApprovedConstantNotMet' $key
    }
    if ($rule.Contains('pattern') -and $value -cnotmatch $rule['pattern']) {
        Add-Refusal 'ApprovedPatternNotMet' $key
    }
}

foreach ($key in $settings.Keys) {
    $value = Get-Setting $key
    if ($key -ne "${production}EvidenceManifest" -and $null -ne $value -and $value -match 'preview') {
        Add-Refusal 'PreviewFeatureConfigured' $key
    }
}

# --------------------------------------------------- cross-field binding
$subscription = Get-Setting "${production}SubscriptionId"
$resourceGroup = Get-Setting "${production}ResourceGroup"

function Get-ResourceName {
    param([string] $ResourceId, [string] $Type)
    if (-not $ResourceId) { return $null }
    $pattern = '^/subscriptions/(?<s>[0-9a-fA-F-]{36})/resourceGroups/(?<g>[A-Za-z0-9._()-]{1,90})/providers/(?<t>[A-Za-z0-9.]+/[A-Za-z0-9]+)/(?<n>[A-Za-z0-9-]{1,64})$'
    $match = [regex]::Match($ResourceId, $pattern)
    if (-not $match.Success -or
        -not [string]::Equals($match.Groups['s'].Value, $subscription, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals($match.Groups['g'].Value, $resourceGroup, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals($match.Groups['t'].Value, $Type, [StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }
    return $match.Groups['n'].Value
}

function Get-Host {
    param([string] $Endpoint)
    if (-not $Endpoint) { return $null }
    if ($Endpoint -match '^[a-z0-9.-]+$') { return $Endpoint }
    $uri = $null
    if ([Uri]::TryCreate($Endpoint, [UriKind]::Absolute, [ref] $uri)) { return $uri.IdnHost }
    return $null
}

$dependencies = @(
    @{ Name = 'DocumentIntelligence'; Type = 'Microsoft.CognitiveServices/accounts'; Suffix = '.cognitiveservices.azure.com' }
    @{ Name = 'SourceStorage'; Type = 'Microsoft.Storage/storageAccounts'; Suffix = '.blob.core.windows.net' }
    @{ Name = 'KeyVault'; Type = 'Microsoft.KeyVault/vaults'; Suffix = '.vault.azure.net' }
    @{ Name = 'Sql'; Type = 'Microsoft.Sql/servers'; Suffix = '.database.windows.net' }
)

$hosts = @{}
foreach ($dependency in $dependencies) {
    $name = Get-ResourceName (Get-Setting "${production}$($dependency.Name)__ResourceId") $dependency.Type
    $endpointHost = Get-Host (Get-Setting "${production}$($dependency.Name)__Endpoint")
    if (-not $name -or -not $endpointHost -or
        -not [string]::Equals($endpointHost, "$name$($dependency.Suffix)", [StringComparison]::OrdinalIgnoreCase)) {
        Add-Refusal 'DependencyBindingInvalid' $dependency.Name
    }
    else {
        $hosts[$dependency.Name] = $endpointHost
    }
}

if (-not (Get-ResourceName (Get-Setting "${production}ManagedIdentity__ResourceId") 'Microsoft.ManagedIdentity/userAssignedIdentities')) {
    Add-Refusal 'ManagedIdentityBindingMissing' 'ManagedIdentity'
}
if (-not (Get-ResourceName (Get-Setting "${production}Monitoring__WorkspaceResourceId") 'Microsoft.OperationalInsights/workspaces')) {
    Add-Refusal 'MonitoringBindingInvalid' 'Monitoring'
}

function Get-KeyName {
    param([string] $KeyUri)
    if (-not $KeyUri -or -not $hosts.Contains('KeyVault')) { return $null }
    $prefix = "https://$($hosts['KeyVault'])/keys/"
    if (-not $KeyUri.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { return $null }
    $name = $KeyUri.Substring($prefix.Length)
    if ($name -cnotmatch '^[A-Za-z0-9-]{1,127}$') { return $null }
    return $name
}

$sourceKey = Get-KeyName (Get-Setting "${production}SourceEnvelope__KeyUri")
if (-not $sourceKey) {
    Add-Refusal 'SourceEnvelopeEncryptionMissing' 'SourceEnvelope'
}

$dataProtectionKey = Get-KeyName (Get-Setting "${production}DataProtection__KeyUri")
$controlName = Get-ResourceName (Get-Setting "${production}DataProtection__StorageResourceId") 'Microsoft.Storage/storageAccounts'
$controlHost = Get-Host (Get-Setting "${production}DataProtection__StorageEndpoint")
if (-not $dataProtectionKey -or
    [string]::Equals($dataProtectionKey, $sourceKey, [StringComparison]::OrdinalIgnoreCase) -or
    -not $controlName -or -not $controlHost -or
    -not [string]::Equals($controlHost, "$controlName.blob.core.windows.net", [StringComparison]::OrdinalIgnoreCase) -or
    ($hosts.Contains('SourceStorage') -and [string]::Equals($controlHost, $hosts['SourceStorage'], [StringComparison]::OrdinalIgnoreCase))) {
    Add-Refusal 'DataProtectionConfigurationMissing' 'DataProtection'
}

$connection = Get-Setting 'ConnectionStrings__TaxAiDatabase'
$clientId = Get-Setting "${production}ManagedIdentity__ClientId"
if ($connection -and $hosts.Contains('Sql')) {
    if (-not $connection.StartsWith("Server=tcp:$($hosts['Sql']),1433;", [StringComparison]::OrdinalIgnoreCase) -or
        -not $connection.EndsWith(";User Id=$clientId", [StringComparison]::OrdinalIgnoreCase)) {
        Add-Refusal 'SqlBindingInvalid' 'ConnectionStrings__TaxAiDatabase'
    }
}

# ---------------------------------------------------- evidence manifest
$manifestText = Get-Setting "${production}EvidenceManifest"
if ($manifestText) {
    try {
        $manifest = $manifestText | ConvertFrom-Json -AsHashtable
        if ($manifest['schemaVersion'] -ne 1) {
            Add-Refusal 'EvidenceManifestInvalid' 'EvidenceManifest'
        }
    }
    catch {
        Add-Refusal 'EvidenceManifestInvalid' 'EvidenceManifest'
    }
}

# ------------------------------------------------------ configuration digest
# The same canonical form as Tenancy.Services.ProductionEvidencePolicy:
# upper-invariant colon-form keys in ordinal order, each written as
# "<key length>:<KEY>=<value length>:<value>\n", SHA-256, lower-case hex. The
# evidence manifest itself is excluded.
function Get-ConfigurationDigest {
    $entries = foreach ($key in $properties.Keys) {
        if (-not $key.StartsWith($production, [StringComparison]::Ordinal) -or
            $key -eq "${production}EvidenceManifest") {
            continue
        }
        [pscustomobject]@{
            Key = $key.Replace('__', ':').ToUpperInvariant()
            Value = Get-Setting $key
        }
    }

    $ordered = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in $entries) {
        $ordered.Add($entry)
    }
    $ordered.Sort([Comparison[object]] {
        param($left, $right)
        [string]::CompareOrdinal($left.Key, $right.Key)
    })

    $builder = [System.Text.StringBuilder]::new()
    foreach ($entry in $ordered) {
        $valueLength = if ($null -eq $entry.Value) { -1 } else { $entry.Value.Length }
        [void] $builder.Append("$($entry.Key.Length):$($entry.Key)=${valueLength}:$($entry.Value)`n")
    }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($builder.ToString())
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

# ------------------------------------------------------------------ report
if ($ComputeDigest) {
    Write-Host "CONFIGURATION DIGEST: $(Get-ConfigurationDigest)"
}

if ($refusals.Count -gt 0) {
    Write-Host 'PRODUCTION CONFIGURATION REFUSED (offline structural check)' -ForegroundColor Red
    foreach ($refusal in $refusals) {
        Write-Host "  - $refusal" -ForegroundColor Red
    }
    exit 1
}

Write-Host 'Offline structural check passed. This is not deployment evidence and not production authorization.'

if ($Mode -eq 'Offline') {
    exit 0
}

# ------------------------------------------------------------- live mode
if (-not $SubscriptionId -or -not $ConfirmReadOnlyInventoryAccess) {
    Write-Host 'FATAL: Live mode requires -SubscriptionId and -ConfirmReadOnlyInventoryAccess.' -ForegroundColor Red
    exit 2
}

if (-not [string]::Equals($SubscriptionId, $subscription, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Host 'PRODUCTION CONFIGURATION REFUSED: the explicit subscription does not match the configured one.' -ForegroundColor Red
    exit 1
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Host 'UNEXECUTED: the Azure CLI is unavailable, so the live inventory check did not run. Record this as unexecuted, not passed.' -ForegroundColor Yellow
    exit 3
}

$liveRefusals = [System.Collections.Generic.List[string]]::new()
$resourceKeys = @($properties.Keys | Where-Object { $_ -like "${production}*ResourceId" -or $_ -eq "${production}Monitoring__WorkspaceResourceId" })
foreach ($key in $resourceKeys) {
    $id = Get-Setting $key
    $raw = & az resource show --ids $id --subscription $SubscriptionId --query '{location: location, publicNetworkAccess: properties.publicNetworkAccess}' --output json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $raw) {
        Write-Host "UNEXECUTED: $key could not be read with the supplied access. Record this as unexecuted, not passed." -ForegroundColor Yellow
        exit 3
    }

    $observed = ($raw -join "`n") | ConvertFrom-Json
    if ($observed.location -ne 'canadacentral') {
        $liveRefusals.Add("ResourceOutsideCanadaCentral ($key)")
    }
    if ($key -notlike '*ManagedIdentity*' -and $key -notlike '*Monitoring*' -and
        $observed.publicNetworkAccess -ne 'Disabled') {
        $liveRefusals.Add("PublicEndpointConfigured ($key)")
    }
}

if ($liveRefusals.Count -gt 0) {
    Write-Host 'PRODUCTION CONFIGURATION REFUSED (live inventory check)' -ForegroundColor Red
    foreach ($refusal in $liveRefusals) {
        Write-Host "  - $refusal" -ForegroundColor Red
    }
    exit 1
}

Write-Host 'Live read-only inventory check passed for region and public network access. This alone is not DEP evidence approval or production authorization.'
exit 0
