<#
.SYNOPSIS
  Spec 016 T05 synthetic provider lifecycle probe (DEP-04, DEP-12, DEP-13).
.DESCRIPTION
  Offline runs the real adapter's controlled-transport tests. Live requires an
  explicit subscription, resource, and authority to submit a generated synthetic
  PDF. No caller document or file is accepted. No raw response or operation ID
  is written. A 204 marks deletion; it is not a physical-erasure assurance.
  Run Live from the approved private network. No resource is provisioned here.
  Exit 0: checks held; 1: failed; 2: invalid input; 3: live unexecuted.
#>
[CmdletBinding()]
param(
    [ValidateSet('Offline', 'Live')][string] $Mode = 'Offline',
    [string] $SubscriptionId,
    [string] $ResourceGroup,
    [string] $AccountName,
    [switch] $ConfirmSyntheticOperationAuthority
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ($Mode -eq 'Offline') {
    $project = Join-Path $repoRoot 'tests/DocumentIntelligence.Tests/DocumentIntelligence.Tests.csproj'
    $output = & dotnet test $project --no-restore --filter 'FullyQualifiedName~DocumentIntelligence.Tests.Production' --verbosity quiet 2>&1
    $result = $LASTEXITCODE
    $output = $null
    if ($result -ne 0) { Write-Output 'OFFLINE: provider lifecycle tests failed.'; exit 1 }
    Write-Output 'OFFLINE: controlled-transport provider tests passed. DEP-04/12/13 remain unobserved.'
    exit 0
}
if (-not $ConfirmSyntheticOperationAuthority -or -not $SubscriptionId -or -not $ResourceGroup -or -not $AccountName -or -not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Output 'LIVE UNEXECUTED: explicit resource authority and Azure CLI are required.'
    exit 3
}
$subscription = [guid]::Empty
if (-not [guid]::TryParseExact($SubscriptionId, 'D', [ref]$subscription) -or $AccountName -notmatch '^[a-zA-Z0-9][a-zA-Z0-9-]{1,63}$' -or $ResourceGroup -notmatch '^[a-zA-Z0-9._()-]{1,90}$') {
    Write-Output 'LIVE UNEXECUTED: invalid target.'; exit 2
}

$client = $null
$resultUri = $null
$completed = $false
$deleted = $false
$sourceBytes = $null
$total = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(120))
$work = [Threading.CancellationTokenSource]::CreateLinkedTokenSource($total.Token)
$work.CancelAfter([TimeSpan]::FromSeconds(100))
try {
    # Management inspection is an operator privilege, not an application role.
    $accountJson = & az cognitiveservices account show --subscription $SubscriptionId --resource-group $ResourceGroup --name $AccountName --output json --only-show-errors 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Probe refused.' }
    $account = ($accountJson -join '') | ConvertFrom-Json
    $expectedId = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.CognitiveServices/accounts/$AccountName"
    if ($account.id -ine $expectedId -or $account.location -ne 'canadacentral' -or $account.kind -ne 'FormRecognizer' -or $account.sku.name -ne 'S0' -or
        $account.properties.publicNetworkAccess -ne 'Disabled' -or $account.properties.disableLocalAuth -ne $true -or
        $account.properties.networkAcls.defaultAction -ne 'Deny') { throw 'Probe refused.' }
    $endpoint = "https://$AccountName.cognitiveservices.azure.com"
    if (([uri]$account.properties.endpoint).GetLeftPart([UriPartial]::Authority) -ine $endpoint) { throw 'Probe refused.' }
    $addresses = [Net.Dns]::GetHostAddresses(([uri]$endpoint).Host)
    if ($addresses.Count -eq 0) { throw 'Probe refused.' }
    foreach ($address in $addresses) {
        $octets = $address.GetAddressBytes()
        if ($octets.Length -ne 4 -or -not ($octets[0] -eq 10 -or ($octets[0] -eq 172 -and $octets[1] -ge 16 -and $octets[1] -le 31) -or ($octets[0] -eq 192 -and $octets[1] -eq 168))) { throw 'Probe refused.' }
    }
    $access = & az account get-access-token --subscription $SubscriptionId --resource 'https://cognitiveservices.azure.com/' --query accessToken --output tsv --only-show-errors 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $access) { throw 'Probe refused.' }
    $handler = [Net.Http.SocketsHttpHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.UseCookies = $false
    $handler.UseProxy = $false
    $handler.ActivityHeadersPropagator = $null
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [Threading.Timeout]::InfiniteTimeSpan
    $client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', ($access -join '').Trim())
    $access = $null

    # An entire valid single-page PDF generated in memory, never a tracked fixture.
    $pageText = 'BT /F1 18 Tf 40 100 Td (Synthetic verification only) Tj ET'
    $objects = @(
        '<< /Type /Catalog /Pages 2 0 R >>',
        '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
        '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',
        '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
        "<< /Length $($pageText.Length) >>`nstream`n$pageText`nendstream"
    )
    $pdf = [Text.StringBuilder]::new("%PDF-1.4`n")
    $offsets = [Collections.Generic.List[int]]::new()
    for ($index = 0; $index -lt $objects.Count; $index++) {
        $offsets.Add($pdf.Length)
        [void]$pdf.Append("$($index + 1) 0 obj`n$($objects[$index])`nendobj`n")
    }
    $xref = $pdf.Length
    [void]$pdf.Append("xref`n0 6`n0000000000 65535 f `n")
    foreach ($offset in $offsets) { [void]$pdf.Append($offset.ToString('D10') + " 00000 n `n") }
    [void]$pdf.Append("trailer`n<< /Size 6 /Root 1 0 R >>`nstartxref`n$xref`n%%EOF")
    $sourceBytes = [Text.Encoding]::ASCII.GetBytes($pdf.ToString())
    $basePath = "$endpoint/documentintelligence/documentModels/prebuilt-layout"
    $query = '?api-version=2024-11-30'
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "${basePath}:analyze${query}&features=keyValuePairs")
    $request.Content = [Net.Http.ByteArrayContent]::new($sourceBytes)
    $request.Content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('application/pdf')
    try {
        $response = $client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $work.Token).GetAwaiter().GetResult()
        try {
            $locations = @($response.Headers.GetValues('Operation-Location'))
            $candidate = [uri]$locations[0]
            $resultId = [guid]::Empty
            $prefix = '/documentintelligence/documentModels/prebuilt-layout/analyzeResults/'
            if ($locations.Count -ne 1 -or $candidate.GetLeftPart([UriPartial]::Authority) -ine $endpoint -or
                $candidate.UserInfo -or $candidate.Fragment -or $candidate.Query -ne $query -or -not $candidate.AbsolutePath.StartsWith($prefix) -or
                -not [guid]::TryParseExact($candidate.AbsolutePath.Substring($prefix.Length), 'D', [ref]$resultId)) { throw 'Probe refused.' }
            $resultUri = "${basePath}/analyzeResults/$($resultId.ToString('D'))${query}"
            if ([int]$response.StatusCode -ne 202) { throw 'Probe refused.' }
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        $response = $client.GetAsync($resultUri, $work.Token).GetAwaiter().GetResult()
        try {
            if ([int]$response.StatusCode -eq 200) {
                $payload = $response.Content.ReadAsStringAsync($work.Token).GetAwaiter().GetResult() | ConvertFrom-Json
                if ($payload.status -eq 'succeeded') {
                    $completed = $payload.analyzeResult.apiVersion -eq '2024-11-30' -and $payload.analyzeResult.modelId -eq 'prebuilt-layout'
                    $payload = $null
                    break
                }
                if ($payload.status -notin @('running', 'notStarted')) { throw 'Probe refused.' }
                $payload = $null
            } elseif ([int]$response.StatusCode -notin @(429, 500, 502, 503, 504)) { throw 'Probe refused.' }
        } finally { $response.Dispose() }
        [Threading.Tasks.Task]::Delay([TimeSpan]::FromSeconds(1 -shl $attempt), $work.Token).GetAwaiter().GetResult()
    }
} catch { $completed = $false }
finally {
    if ($resultUri -and $client) {
        $cleanup = [Threading.CancellationTokenSource]::CreateLinkedTokenSource($total.Token)
        $cleanup.CancelAfter([TimeSpan]::FromSeconds(15))
        try {
            for ($attempt = 0; $attempt -lt 3; $attempt++) {
                try {
                    $response = $client.DeleteAsync($resultUri, $cleanup.Token).GetAwaiter().GetResult()
                    try { if ([int]$response.StatusCode -eq 204) { $deleted = $true; break } } finally { $response.Dispose() }
                } catch { }
                [Threading.Tasks.Task]::Delay([TimeSpan]::FromSeconds(1 -shl $attempt), $cleanup.Token).GetAwaiter().GetResult()
            }
        } catch { } finally { $cleanup.Dispose() }
    }
    if ($sourceBytes) { [Array]::Clear($sourceBytes) }
    if ($client) { $client.Dispose() }
    $work.Dispose(); $total.Dispose()
    $resultUri = $null
}
if (-not ($completed -and $deleted)) { Write-Output 'LIVE: provider lifecycle verification failed.'; exit 1 }
Write-Output 'LIVE: approved destination/API/model and explicit deletion acknowledgement observed. Physical-erasure and processor assurances remain separately required.'
exit 0
