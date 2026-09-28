// Spec 016 T02 - the TaxAI regional topology entry point.
//
// profile 'primary'  : Canada Central. The complete ingress/compute/egress
//                      topology: network, private DNS, deny-default egress
//                      firewall, the private Portal App Service, the WAF v2
//                      gateway, and private endpoint bindings for the Portal and
//                      all four approved dependencies.
// profile 'recovery' : Canada East. A passive network foundation only - virtual
//                      network, subnets, NSGs, a route table that DROPS the
//                      default route, and private DNS zones. No gateway, no
//                      application, no firewall, no private endpoint, no active
//                      traffic and never an alternate Document Intelligence.
//
// Every security posture is fixed in the modules. Parameters are addresses,
// names and bounded capacities only; every bound is enforced by type. There is
// no parameter that enables public access, a plaintext backend, port 80, an
// outbound destination, a diagnostic category or a non-Canadian region.
// The resources owned by later slices (source Blob, SQL, Key Vault, Document
// Intelligence) are referenced by the same names the runtime configuration
// emits, so the private endpoint bindings and the validated settings cannot
// drift apart; deploying before those resources exist fails, it never deploys
// a partial binding set.

targetScope = 'resourceGroup'

@description('Deployment profile.')
@allowed([
  'primary'
  'recovery'
])
param profile string

@description('Primary region. Only Canada Central is approved (FR-01).')
@allowed([
  'canadacentral'
])
param primaryRegion string

@description('Recovery-only region. Only Canada East is approved (FR-01, FR-11).')
@allowed([
  'canadaeast'
])
param recoveryRegion string

@description('Resource name prefix.')
@minLength(3)
@maxLength(16)
param namePrefix string

@description('Regional virtual network addressing.')
param network networkAddressing

@description('Primary-only topology and runtime inputs. Required for the primary profile; forbidden for recovery.')
param primary primaryTopology?

@sealed()
type networkAddressing = {
  @description('Virtual network address space.')
  addressPrefix: string
  @description('Application Gateway subnet.')
  gatewaySubnetPrefix: string
  @description('App Service outbound VNet integration subnet.')
  appIntegrationSubnetPrefix: string
  @description('Private endpoint subnet.')
  privateEndpointSubnetPrefix: string
  @description('AzureFirewallSubnet (at least /26).')
  firewallSubnetPrefix: string
}

@sealed()
type hostingSettings = {
  @minLength(2)
  @maxLength(60)
  siteName: string
  @minValue(2)
  @maxValue(10)
  minimumInstanceCount: int
  @minValue(2)
  @maxValue(10)
  maximumInstanceCount: int
  @minLength(40)
  @maxLength(40)
  backendCertificateThumbprint: string
}

@sealed()
type ingressSettings = {
  @minLength(4)
  @maxLength(253)
  publicHostName: string
  @minLength(1)
  gatewayIdentityName: string
  @minLength(1)
  frontendCertificateUri: string
  @minValue(2)
  @maxValue(10)
  minimumCapacity: int
  @minValue(2)
  @maxValue(125)
  maximumCapacity: int
  @minValue(1)
  @maxValue(4000)
  fileUploadLimitInMb: int
}

@sealed()
type runtimeInputs = {
  tenantId: string
  @minLength(1)
  @maxLength(128)
  artifactVersion: string
  managedIdentityName: string
  managedIdentityClientId: string
  managedIdentityPrincipalId: string
  documentIntelligenceName: string
  sourceStorageAccountName: string
  keyVaultName: string
  sqlServerName: string
  sqlDatabaseName: string
  logAnalyticsWorkspaceName: string
  sourceEnvelopeKeyName: string
  controlStorageAccountName: string
  keyRingContainerName: string
  dataProtectionKeyName: string
  applicationDiscriminator: string
  @minValue(1)
  @maxValue(5)
  maximumAttempts: int
  operationDeadline: string
  probeDeadline: string
  evidenceManifest: string
}

@sealed()
type primaryTopology = {
  hosting: hostingSettings
  ingress: ingressSettings
  runtime: runtimeInputs
}

var isPrimary = profile == 'primary'
var location = isPrimary ? primaryRegion : recoveryRegion

// Fail closed: the primary profile without its inputs, or the recovery profile
// with them, cannot compile into a deployment that silently omits a control.
var primaryInputs = isPrimary ? primary! : null
var recoveryWithPrimaryInputs = !isPrimary && primary != null
var profileInputsConsistent = recoveryWithPrimaryInputs ? fail('The recovery profile deploys no primary topology.') : true

module regionalNetwork 'modules/network.bicep' = {
  name: 'network'
  params: {
    location: location
    namePrefix: namePrefix
    addressPrefix: network.addressPrefix
    gatewaySubnetPrefix: network.gatewaySubnetPrefix
    appIntegrationSubnetPrefix: network.appIntegrationSubnetPrefix
    privateEndpointSubnetPrefix: network.privateEndpointSubnetPrefix
    firewallSubnetPrefix: network.firewallSubnetPrefix
    egressMode: isPrimary && profileInputsConsistent ? 'Firewall' : 'Blackhole'
  }
}

module privateDns 'modules/private-dns.bicep' = {
  name: 'private-dns'
  params: {
    namePrefix: namePrefix
    virtualNetworkIds: [
      regionalNetwork.outputs.virtualNetworkId
    ]
  }
}

module egress 'modules/egress.bicep' = if (isPrimary) {
  name: 'egress'
  params: {
    location: primaryRegion
    namePrefix: namePrefix
    firewallSubnetId: regionalNetwork.outputs.firewallSubnetId
    appIntegrationSubnetPrefix: network.appIntegrationSubnetPrefix
  }
}

module runtimeConfiguration 'modules/runtime-configuration.bicep' = if (isPrimary) {
  name: 'runtime-configuration'
  params: {
    primaryRegion: primaryRegion
    recoveryRegion: recoveryRegion
    tenantId: primaryInputs!.runtime.tenantId
    artifactVersion: primaryInputs!.runtime.artifactVersion
    managedIdentityName: primaryInputs!.runtime.managedIdentityName
    managedIdentityClientId: primaryInputs!.runtime.managedIdentityClientId
    managedIdentityPrincipalId: primaryInputs!.runtime.managedIdentityPrincipalId
    documentIntelligenceName: primaryInputs!.runtime.documentIntelligenceName
    sourceStorageAccountName: primaryInputs!.runtime.sourceStorageAccountName
    keyVaultName: primaryInputs!.runtime.keyVaultName
    sqlServerName: primaryInputs!.runtime.sqlServerName
    sqlDatabaseName: primaryInputs!.runtime.sqlDatabaseName
    logAnalyticsWorkspaceName: primaryInputs!.runtime.logAnalyticsWorkspaceName
    sourceEnvelopeKeyName: primaryInputs!.runtime.sourceEnvelopeKeyName
    controlStorageAccountName: primaryInputs!.runtime.controlStorageAccountName
    keyRingContainerName: primaryInputs!.runtime.keyRingContainerName
    dataProtectionKeyName: primaryInputs!.runtime.dataProtectionKeyName
    applicationDiscriminator: primaryInputs!.runtime.applicationDiscriminator
    maximumAttempts: primaryInputs!.runtime.maximumAttempts
    operationDeadline: primaryInputs!.runtime.operationDeadline
    probeDeadline: primaryInputs!.runtime.probeDeadline
    evidenceManifest: primaryInputs!.runtime.evidenceManifest
  }
}

module portal 'modules/app-service.bicep' = if (isPrimary) {
  name: 'app-service'
  params: {
    location: primaryRegion
    namePrefix: namePrefix
    siteName: primaryInputs!.hosting.siteName
    minimumInstanceCount: primaryInputs!.hosting.minimumInstanceCount
    maximumInstanceCount: primaryInputs!.hosting.maximumInstanceCount
    backendCertificateThumbprint: primaryInputs!.hosting.backendCertificateThumbprint
    appIntegrationSubnetId: regionalNetwork.outputs.appIntegrationSubnetId
    managedIdentityResourceId: resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', primaryInputs!.runtime.managedIdentityName)
    publicHostName: primaryInputs!.ingress.publicHostName
    runtimeSettings: runtimeConfiguration!.outputs.appSettings
  }
  dependsOn: [
    egress
  ]
}

module privateEndpoints 'modules/private-endpoints.bicep' = if (isPrimary) {
  name: 'private-endpoints'
  params: {
    location: primaryRegion
    namePrefix: namePrefix
    subnetId: regionalNetwork.outputs.privateEndpointSubnetId
    zoneIds: privateDns.outputs.zoneIds
    portalResourceId: portal!.outputs.siteId
    sourceStorageResourceId: resourceId('Microsoft.Storage/storageAccounts', primaryInputs!.runtime.sourceStorageAccountName)
    sqlServerResourceId: resourceId('Microsoft.Sql/servers', primaryInputs!.runtime.sqlServerName)
    keyVaultResourceId: resourceId('Microsoft.KeyVault/vaults', primaryInputs!.runtime.keyVaultName)
    documentIntelligenceResourceId: resourceId('Microsoft.CognitiveServices/accounts', primaryInputs!.runtime.documentIntelligenceName)
  }
}

module ingress 'modules/application-gateway.bicep' = if (isPrimary) {
  name: 'application-gateway'
  params: {
    location: primaryRegion
    namePrefix: namePrefix
    gatewaySubnetId: regionalNetwork.outputs.gatewaySubnetId
    gatewayIdentityResourceId: resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', primaryInputs!.ingress.gatewayIdentityName)
    frontendCertificateUri: primaryInputs!.ingress.frontendCertificateUri
    publicHostName: primaryInputs!.ingress.publicHostName
    backendHostName: portal!.outputs.defaultHostName
    minimumCapacity: primaryInputs!.ingress.minimumCapacity
    maximumCapacity: primaryInputs!.ingress.maximumCapacity
    fileUploadLimitInMb: primaryInputs!.ingress.fileUploadLimitInMb
  }
  dependsOn: [
    privateEndpoints
  ]
}

output virtualNetworkId string = regionalNetwork.outputs.virtualNetworkId
output plannedFirewallAddress string = regionalNetwork.outputs.firewallPrivateAddress
