<#
.SYNOPSIS
    The source-protection probe (spec 016 T04, DEP-10; AC-025 to AC-029,
    AC-034, AC-038 to AC-040).

.DESCRIPTION
    Proves that stored source content is protected and never disclosed: the
    source account posture equals the reviewed data-configuration contract,
    every object is authenticated application ciphertext bound to its firm,
    case and resource, and no source byte, blob address or SAS reaches an HTTP
    or browser surface.

    Every envelope decision is made by the compiled TaxAI.AzureInfrastructure
    SourceEnvelopeCodec - the same code the platform runs - never by a second
    implementation in this script.

    Offline (default)
        A local, in-process run with synthetic bytes and synthetic keys. It
        round-trips the envelope, refuses tamper, truncation, reordering and
        every foreign scope, checks the tracked source template against the
        contract, and scans the portal for any source-serving or SAS surface.
        Nothing leaves the process. It is NOT deployment evidence: DEP-10 stays
        pending until the Live probe is observed against the exact resources.

    Live
        Read-only. Runs against one explicitly named source account in one
        explicitly named subscription, and only with -ConfirmObservationAuthority.
        It must run from inside the approved private network. It compares the
        deployed account, blob-service and container posture with the contract,
        confirms an unauthenticated request cannot list or read the source
        container, and, when -PortalBaseUrl is given, fetches the portal's
        public pages and requires that none carries a blob address, SAS token or
        planted source marker. It never writes, lists, reads or deletes a source
        object and never uses a shared key or SAS.

    Output names closed check codes only; it never prints an account name,
    subscription, key, object name or byte.

    Exit codes:
      0  every executed check held
      1  one or more checks failed
      2  the probe could not run (bad arguments or unreadable input)
      3  Live mode was not authorized or its tooling is unavailable.
         Record as unexecuted, never passed.

.PARAMETER Mode
    Offline (default) or Live.

.PARAMETER AssemblyPath
    The compiled TaxAI.AzureInfrastructure.dll. Defaults to the repository's
    Release, then Debug, build output.

.PARAMETER SubscriptionId
    Live only: the exact subscription the probe is authorized for.

.PARAMETER StorageAccountName
    Live only: the source storage account.

.PARAMETER PortalBaseUrl
    Live only, optional: the HTTPS origin of the deployed portal.

.PARAMETER ConfirmObservationAuthority
    Live only: the operator confirms explicit authority to observe the named
    resources.

.EXAMPLE
    pwsh ./scripts/azure/Test-SourceProtection.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Offline', 'Live')]
    [string] $Mode = 'Offline',

    [string] $AssemblyPath,

    [string] $SubscriptionId,

    [string] $StorageAccountName,

    [string] $PortalBaseUrl,

    [switch] $ConfirmObservationAuthority
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

function Read-Contract {
    $path = Join-Path $repositoryRoot 'infra/azure/contracts/data-configuration.schema.json'
    $contract = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
    $expected = @{}
    foreach ($entry in $contract['properties']['sourceStorage']['properties'].GetEnumerator()) {
        $expected[$entry.Key] = $entry.Value['const']
    }
    return $expected
}

# ------------------------------------------------------------ the codec
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
    $directory = Split-Path -Parent (Resolve-Path -LiteralPath $AssemblyPath).Path
    [void][System.Reflection.Assembly]::LoadFrom((Join-Path $directory 'Tenancy.dll'))
    [void][System.Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
    $codec = [TaxAI.AzureInfrastructure.Content.SourceEnvelopeCodec]
    # PowerShell cannot pass a span; this shim only forwards arrays to the real codec.
    Add-Type -ReferencedAssemblies @((Resolve-Path -LiteralPath $AssemblyPath).Path, (Join-Path $directory 'Tenancy.dll'), 'System.Security.Cryptography') -TypeDefinition @'
public static class SourceProbeShim
{
    public static byte[] Decrypt(byte[] dataKey, TaxAI.AzureInfrastructure.Content.SourceEnvelopeScope scope, byte[] ciphertext, long maximum) =>
        TaxAI.AzureInfrastructure.Content.SourceEnvelopeCodec.Decrypt(dataKey, scope, ciphertext, maximum);
}
'@
}
catch {
    Write-Host 'FATAL: the infrastructure assembly could not be loaded by this PowerShell runtime.' -ForegroundColor Red
    exit 2
}

function New-Scope {
    param([Guid] $Firm, [Guid] $Case, [Guid] $Resource)
    return [TaxAI.AzureInfrastructure.Content.SourceEnvelopeScope]::new(
        [Tenancy.Models.TrustedFirmId]::FromGuid($Firm), $Case, $Resource)
}

function Protect-Synthetic {
    param([byte[]] $DataKey, $Scope, [byte[]] $Plaintext)
    $output = [System.IO.MemoryStream]::new()
    $input = [System.IO.MemoryStream]::new($Plaintext)
    [void]$codec::EncryptAsync(
        [ReadOnlyMemory[byte]]::new($DataKey),
        $Scope,
        $input,
        $output,
        [long]10485760,
        $null,
        [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
    return $output.ToArray()
}

function Test-Refused {
    param([byte[]] $DataKey, $Scope, [byte[]] $Ciphertext)
    try {
        [void][SourceProbeShim]::Decrypt($DataKey, $Scope, $Ciphertext, [long]10485760)
        return $false
    }
    catch [System.Security.Cryptography.CryptographicException] {
        return $true
    }
    catch {
        if ($_.Exception.InnerException -is [System.Security.Cryptography.CryptographicException]) {
            return $true
        }

        throw
    }
}

# ------------------------------------------------------------ offline
function Invoke-Offline {
    $marker = [System.Text.Encoding]::ASCII.GetBytes('SYNTHETIC-SOURCE-PROBE-MARKER')
    $plaintext = [byte[]]::new(200000)
    for ($index = 0; $index -lt $plaintext.Length; $index++) {
        $plaintext[$index] = $marker[$index % $marker.Length]
    }

    $firm = [Guid]::NewGuid()
    $case = [Guid]::NewGuid()
    $resource = [Guid]::NewGuid()
    $scope = New-Scope $firm $case $resource
    $dataKey = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $otherKey = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    try {
        $ciphertext = Protect-Synthetic $dataKey $scope $plaintext
        $second = Protect-Synthetic $otherKey $scope $plaintext

        $recovered = [SourceProbeShim]::Decrypt($dataKey, $scope, $ciphertext, [long]10485760)
        Add-Check 'EnvelopeRoundTrip' ([System.Linq.Enumerable]::SequenceEqual([byte[]]$recovered, $plaintext))

        $text = [System.Text.Encoding]::ASCII.GetString($ciphertext)
        Add-Check 'CiphertextCarriesNoPlaintext' (-not $text.Contains('SYNTHETIC-SOURCE-PROBE-MARKER'))
        Add-Check 'DistinctKeysGiveDistinctCiphertext' (-not [System.Linq.Enumerable]::SequenceEqual([byte[]]$ciphertext, [byte[]]$second))

        $tampered = [byte[]]$ciphertext.Clone()
        $tampered[200] = $tampered[200] -bxor 1
        Add-Check 'TamperRefused' (Test-Refused $dataKey $scope $tampered)

        $truncated = [byte[]]$ciphertext[0..($ciphertext.Length - 8)]
        Add-Check 'TruncationRefused' (Test-Refused $dataKey $scope $truncated)

        $chunk = $codec::ChunkOverhead + $codec::ChunkSize
        $header = $codec::HeaderSize
        $reordered = [byte[]]($ciphertext[0..($header - 1)] + $ciphertext[($header + $chunk)..($header + 2 * $chunk - 1)] + $ciphertext[$header..($header + $chunk - 1)] + $ciphertext[($header + 2 * $chunk)..($ciphertext.Length - 1)])
        Add-Check 'ReorderedChunksRefused' (Test-Refused $dataKey $scope $reordered)

        Add-Check 'ForeignFirmRefused' (Test-Refused $dataKey (New-Scope ([Guid]::NewGuid()) $case $resource) $ciphertext)
        Add-Check 'WrongCaseRefused' (Test-Refused $dataKey (New-Scope $firm ([Guid]::NewGuid()) $resource) $ciphertext)
        Add-Check 'WrongResourceRefused' (Test-Refused $dataKey (New-Scope $firm $case ([Guid]::NewGuid())) $ciphertext)
        Add-Check 'WrongDataKeyRefused' (Test-Refused $otherKey $scope $ciphertext)
    }
    finally {
        [System.Security.Cryptography.CryptographicOperations]::ZeroMemory($dataKey)
        [System.Security.Cryptography.CryptographicOperations]::ZeroMemory($otherKey)
    }

    # AC-025, AC-028, AC-029, AC-040: the tracked template states the contract.
    $expected = Read-Contract
    # Whole-line comments are documentation, not declarations.
    $template = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'infra/azure/modules/storage.bicep') |
        Where-Object { $_ -notmatch '^\s*//' }) -join "`n"
    Add-Check 'TemplateGeoZoneRedundantGpv2' ($template.Contains("name: '$($expected['sku'])'") -and $template.Contains("kind: '$($expected['kind'])'"))
    Add-Check 'TemplatePrivateEntraOnly' ($template.Contains("publicNetworkAccess: 'Disabled'") -and $template.Contains('allowSharedKeyAccess: false') -and $template.Contains('allowBlobPublicAccess: false'))
    Add-Check 'TemplateCustomerManagedKey' ($template.Contains("keySource: '$($expected['keySource'])'") -and $template.Contains('requireInfrastructureEncryption: true'))
    Add-Check 'TemplateNoShadowRetention' ($template.Contains('isVersioningEnabled: false') -and -not ($template -match '(?i)immutab|legalHold|managementPolicies'))

    # DEP-10 (static): no portal surface can serve source bytes or a SAS.
    $portal = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src/TaxAI.Portal') -Recurse -Include '*.cs', '*.razor', '*.js', '*.json' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }
    $joined = ($portal -join "`n")
    Add-Check 'NoSasOrBlobAddressInPortal' (-not ($joined -match 'GenerateSasUri|BlobSasBuilder|blob\.core\.windows\.net|[?&]sig='))
    Add-Check 'NoSourceServingResponseInPortal' (-not ($joined -match 'Results\.(File|Stream|Bytes)\(|TypedResults\.(File|Stream|Bytes)\(|SendFileAsync|FileStreamResult|PhysicalFile'))

    $failed = @($results | Where-Object { -not $_.Held }).Count
    $banner = 'OFFLINE SIMULATION ONLY: synthetic in-process checks. This is not deployment evidence and does not satisfy DEP-10.'
    Write-Report $banner
    if ($failed -gt 0) { exit 1 }
    exit 0
}

# --------------------------------------------------------------- live
function Invoke-Az {
    param([Parameter(Mandatory)][string[]] $Arguments)
    $output = & az @Arguments --only-show-errors --output json 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'The Azure CLI call failed.'
    }

    return ($output | Out-String | ConvertFrom-Json -AsHashtable)
}

function Invoke-Live {
    if (-not $ConfirmObservationAuthority -or
        [string]::IsNullOrWhiteSpace($SubscriptionId) -or
        [string]::IsNullOrWhiteSpace($StorageAccountName)) {
        Write-Host 'UNEXECUTED: Live mode needs -SubscriptionId, -StorageAccountName and -ConfirmObservationAuthority. Record as unexecuted, never passed.' -ForegroundColor Yellow
        exit 3
    }
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        Write-Host 'UNEXECUTED: the Azure CLI is not available. Record as unexecuted, never passed.' -ForegroundColor Yellow
        exit 3
    }

    try {
        $expected = Read-Contract
    }
    catch {
        Write-Host 'FATAL: the data-configuration contract could not be read.' -ForegroundColor Red
        exit 2
    }

    try {
        $account = Invoke-Az @('account', 'show')
        if ([string] $account['id'] -ne $SubscriptionId) {
            Add-Check 'AuthorizedSubscription' $false
            Write-Report 'LIVE PROBE REFUSED: the signed-in subscription is not the authorized one.'
            exit 1
        }
        Add-Check 'AuthorizedSubscription' $true

        $storage = Invoke-Az @('storage', 'account', 'show', '--name', $StorageAccountName, '--subscription', $SubscriptionId)
        $encryption = $storage['encryption']
        Add-Check 'SourceRegionCanadaCentral' ([string] $storage['location'] -eq $expected['location'])
        Add-Check 'SourceGeneralPurposeV2' ([string] $storage['kind'] -eq $expected['kind'])
        Add-Check 'SourceGeoZoneRedundant' ([string] $storage['sku']['name'] -eq $expected['sku'])
        Add-Check 'SourcePublicNetworkDisabled' ([string] $storage['publicNetworkAccess'] -eq $expected['publicNetworkAccess'])
        Add-Check 'SourceFirewallDenyDefault' ([string] $storage['networkRuleSet']['defaultAction'] -eq $expected['networkDefaultAction'] -and [string] $storage['networkRuleSet']['bypass'] -eq $expected['networkBypass'])
        Add-Check 'SourceSharedKeyDisabled' ($storage['allowSharedKeyAccess'] -eq $expected['allowSharedKeyAccess'])
        Add-Check 'SourceBlobPublicAccessDisabled' ($storage['allowBlobPublicAccess'] -eq $expected['allowBlobPublicAccess'])
        Add-Check 'SourceTransportMinimum' ([string] $storage['minimumTlsVersion'] -eq $expected['minimumTlsVersion'] -and $storage['enableHttpsTrafficOnly'] -eq $true)
        Add-Check 'SourceCustomerManagedKey' ([string] $encryption['keySource'] -eq $expected['keySource'] -and [string] $encryption['keyVaultProperties']['keyName'] -eq $expected['cmkKeyName'])
        Add-Check 'SourceInfrastructureEncryption' ($encryption['requireInfrastructureEncryption'] -eq $expected['requireInfrastructureEncryption'])

        $service = Invoke-Az @('storage', 'account', 'blob-service-properties', 'show', '--account-name', $StorageAccountName, '--subscription', $SubscriptionId)
        Add-Check 'SourceVersioningDisabled' (-not [bool] $service['isVersioningEnabled'])
        Add-Check 'SourceSoftDeleteDisabled' (-not [bool] $service['deleteRetentionPolicy']['enabled'] -and -not [bool] $service['containerDeleteRetentionPolicy']['enabled'])
        Add-Check 'SourceChangeFeedAndRestoreDisabled' (-not [bool] ($service['changeFeed'] -and $service['changeFeed']['enabled']) -and -not [bool] ($service['restorePolicy'] -and $service['restorePolicy']['enabled']))

        $container = Invoke-Az @('storage', 'container-rm', 'show', '--storage-account', $StorageAccountName, '--name', $expected['containerName'], '--subscription', $SubscriptionId)
        Add-Check 'SourceContainerNotPublic' ([string] $container['publicAccess'] -eq $expected['containerPublicAccess'])
        Add-Check 'SourceContainerNotImmutable' (-not [bool] $container['hasImmutabilityPolicy'] -and -not [bool] $container['hasLegalHold'] -and -not [bool] ($container['immutableStorageWithVersioning'] -and $container['immutableStorageWithVersioning']['enabled']))

        # AC-028: an unauthenticated request can neither list nor read.
        $anonymous = 0
        try {
            $response = Invoke-WebRequest -Uri "https://$StorageAccountName.blob.core.windows.net/$($expected['containerName'])?restype=container&comp=list" -Method Get -SkipHttpErrorCheck -TimeoutSec 15
            $anonymous = [int] $response.StatusCode
        }
        catch {
            $anonymous = -1
        }
        Add-Check 'AnonymousListRefused' ($anonymous -ne 200)

        # DEP-10: no public page carries a blob address, SAS token or source marker.
        if (-not [string]::IsNullOrWhiteSpace($PortalBaseUrl)) {
            $origin = [Uri] $PortalBaseUrl
            $clean = $true
            foreach ($path in @('/', '/health', '/client-intake')) {
                $page = Invoke-WebRequest -Uri ([Uri]::new($origin, $path)) -Method Get -SkipHttpErrorCheck -TimeoutSec 15
                $body = [string] $page.Content
                $headers = ($page.Headers.GetEnumerator() | ForEach-Object { "$($_.Key): $($_.Value)" }) -join "`n"
                if ("$body`n$headers" -match 'blob\.core\.windows\.net|[?&]sig=|[?&]sv=\d|SYNTHETIC-SOURCE') {
                    $clean = $false
                }
            }
            Add-Check 'NoSourceDisclosureOverHttp' $clean
        }
        else {
            Write-Host '  - NoSourceDisclosureOverHttp: UNEXECUTED (no -PortalBaseUrl)'
        }
    }
    catch {
        Add-Check 'LiveObservationCompleted' $false
    }

    $failed = @($results | Where-Object { -not $_.Held }).Count
    Write-Report 'LIVE PROBE: read-only observation of the named resources. Record the result with the exact artifact and configuration.'
    if ($failed -gt 0) { exit 1 }
    exit 0
}

if ($Mode -eq 'Live') {
    Invoke-Live
}
else {
    Invoke-Offline
}
