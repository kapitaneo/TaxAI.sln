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
//
// Spec 016 T03 adds the key-protection resources, all primary-only and all
// named by the same runtime inputs: the Canadian Key Vault with its four
// purpose-separated RSA-HSM customer-managed keys (key-vault.bicep), the
// separate private Data Protection key-ring store (key-ring-storage.bicep) and
// the secure Azure SQL Database with its TDE protector (sql.bicep). The
// dependency private endpoints are created only after the Key Vault and SQL
// resources they bind exist.
//
// Spec 016 T04 adds the encrypted Canadian source store (storage.bicep): the
// GPv2 GZRS account named by the same runtime input, its Key Vault source-Blob
// CMK, the one private source container and no versioning, soft delete,
// snapshot or immutability retention. Its private endpoint is T02's existing
// source-storage binding, created only after the account exists.
//
// Spec 016 T06 adds recovery, primary-only and named from the same inputs: the
// two immutable Canadian control-ledger accounts (control-storage.bicep; Canada
// Central and the authoritative Canada East copy, names derived from the prefix
// and resource group), the SQL backup policy (sql.bicep: 35-day point-in-time,
// geo-zone backups, no long-term retention) and the passive Canada East SQL
// geo-secondary behind a Manual failover group (recovery.bicep). The runtime
// configuration names both ledger copies.
//
// Spec 016 T06 amendment 2 (AC-107, DEP-18): the recovery profile stays the
// passive network foundation unless an operator explicitly supplies the
// sealed `activation` input during a declared regional recovery. Only then does
// it deploy, in its own Canada East resource group, the recovery application
// host: deny-default egress, the same hardened App Service (non-zonal: Canada
// East has no availability zones), its private endpoints to the SAME approved
// inventory in the data resource group (Canada East SQL server, source and
// key-ring accounts, Key Vault, both ledger copies and the Canada Central
// Document Intelligence resource), and the WAF v2 gateway. Its settings are the
// separately signed RegionalRecovery profile. Activation never happens by
// itself, never creates a second Document Intelligence resource and never opens
// a public data path; the host still starts quarantined until reconciliation
// releases its database incarnation.

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

@description('Recovery profile only, and only during an operator-declared regional recovery: the Canada East recovery host. Forbidden for the primary profile.')
param activation recoveryActivation?

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
type recoveryActivation = {
  @description('The resource group that holds the approved data inventory (the primary deployment\'s group). Never the recovery group itself.')
  @minLength(1)
  @maxLength(90)
  dataResourceGroup: string
  @description('The same topology and runtime inputs as the primary host; the recovery evidence manifest is the one signed for the RegionalRecovery configuration.')
  topology: primaryTopology
}

@sealed()
type primaryTopology = {
  hosting: hostingSettings
  ingress: ingressSettings
  runtime: runtimeInputs
}

var isPrimary = profile == 'primary'
var location = isPrimary ? primaryRegion : recoveryRegion

// Spec 016 T06: availability-zone capability per deployment role, stated
// once and explicitly rather than inferred from a region name. The primary
// deployment is zonal (FR-02); the current recovery region offers no
// availability zones (Microsoft Learn, "List of Azure regions", read
// 2026-09-30), so the manually activated recovery host is non-zonal.
var zoneCapability = {
  primary: true
  recovery: false
}

// Spec 016 T06: deterministic, globally unique control-ledger account names.
var ledgerStem = toLower(replace(namePrefix, '-', ''))
// The recovery host derives the same names from the data resource group.
var controlLedgerCentralName = take('${ledgerStem}ledcc${uniqueString(dataResourceGroupId, 'ledger-cc')}', 24)
var controlLedgerEastName = take('${ledgerStem}ledce${uniqueString(dataResourceGroupId, 'ledger-ce')}', 24)

// Fail closed: the primary profile without its inputs, or the recovery profile
// with them, cannot compile into a deployment that silently omits a control.
var primaryInputs = isPrimary ? primary! : null
var recoveryWithPrimaryInputs = !isPrimary && primary != null
var profileInputsConsistent = recoveryWithPrimaryInputs ? fail('The recovery profile deploys no primary topology.') : true

// Spec 016 T06: activation is recovery-only, explicit and never co-located
// with the data inventory it binds.
var activating = !isPrimary && activation != null
var primaryWithActivation = isPrimary && activation != null
var activationInputsConsistent = primaryWithActivation ? fail('The primary profile activates no recovery host.') : (activating && toLower(activation!.dataResourceGroup) == toLower(resourceGroup().name) ? fail('The recovery host deploys into its own Canada East resource group.') : true)
var recoveryInputs = activating && activationInputsConsistent ? activation!.topology : null
var dataResourceGroupName = activating ? activation!.dataResourceGroup : resourceGroup().name
var dataResourceGroupId = activating ? '${subscription().id}/resourceGroups/${activation!.dataResourceGroup}' : resourceGroup().id
var recoverySqlServerName = activating ? take('${recoveryInputs!.runtime.sqlServerName}-ce', 63) : ''

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
    egressMode: (isPrimary || activating) && profileInputsConsistent ? 'Firewall' : 'Blackhole'
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
    zoneRedundant: zoneCapability.primary
    namePrefix: namePrefix
    firewallSubnetId: regionalNetwork.outputs.firewallSubnetId
    appIntegrationSubnetPrefix: network.appIntegrationSubnetPrefix
  }
}

module keyVault 'modules/key-vault.bicep' = if (isPrimary) {
  name: 'key-vault'
  params: {
    location: primaryRegion
    keyVaultName: primaryInputs!.runtime.keyVaultName
    tenantId: primaryInputs!.runtime.tenantId
    sourceEnvelopeKeyName: primaryInputs!.runtime.sourceEnvelopeKeyName
    dataProtectionKeyName: primaryInputs!.runtime.dataProtectionKeyName
    applicationPrincipalId: primaryInputs!.runtime.managedIdentityPrincipalId
  }
}

module keyRingStorage 'modules/key-ring-storage.bicep' = if (isPrimary) {
  name: 'key-ring-storage'
  params: {
    location: primaryRegion
    namePrefix: namePrefix
    accountName: primaryInputs!.runtime.controlStorageAccountName
    containerName: primaryInputs!.runtime.keyRingContainerName
    applicationPrincipalId: primaryInputs!.runtime.managedIdentityPrincipalId
    privateEndpointSubnetId: regionalNetwork.outputs.privateEndpointSubnetId
    blobZoneId: privateDns.outputs.zoneIds.SourceStorage
  }
}

module sourceStorage 'modules/storage.bicep' = if (isPrimary) {
  name: 'source-storage'
  params: {
    location: primaryRegion
    namePrefix: namePrefix
    accountName: primaryInputs!.runtime.sourceStorageAccountName
    keyVaultName: primaryInputs!.runtime.keyVaultName
    sourceBlobKeyName: keyVault!.outputs.sourceBlobKeyName
    applicationPrincipalId: primaryInputs!.runtime.managedIdentityPrincipalId
  }
}

module sql 'modules/sql.bicep' = if (isPrimary) {
  name: 'sql'
  params: {
    location: primaryRegion
    namePrefix: namePrefix
    sqlServerName: primaryInputs!.runtime.sqlServerName
    sqlDatabaseName: primaryInputs!.runtime.sqlDatabaseName
    tenantId: primaryInputs!.runtime.tenantId
    keyVaultName: primaryInputs!.runtime.keyVaultName
    tdeKeyName: keyVault!.outputs.sqlTdeKeyName
    tdeKeyUriWithVersion: keyVault!.outputs.sqlTdeKeyUriWithVersion
  }
}

module controlStorage 'modules/control-storage.bicep' = if (isPrimary) {
  name: 'control-storage'
  params: {
    location: primaryRegion
    recoveryLocation: recoveryRegion
    namePrefix: namePrefix
    centralAccountName: controlLedgerCentralName
    eastAccountName: controlLedgerEastName
    applicationPrincipalId: primaryInputs!.runtime.managedIdentityPrincipalId
    privateEndpointSubnetId: regionalNetwork.outputs.privateEndpointSubnetId
    blobZoneId: privateDns.outputs.zoneIds.SourceStorage
  }
}

module sqlRecovery 'modules/recovery.bicep' = if (isPrimary) {
  name: 'sql-recovery'
  params: {
    location: recoveryRegion
    primaryServerName: primaryInputs!.runtime.sqlServerName
    secondaryServerName: take('${primaryInputs!.runtime.sqlServerName}-ce', 63)
    failoverGroupName: '${namePrefix}-fog'
    sqlDatabaseName: primaryInputs!.runtime.sqlDatabaseName
    tenantId: primaryInputs!.runtime.tenantId
    keyVaultName: primaryInputs!.runtime.keyVaultName
    tdeKeyName: keyVault!.outputs.sqlTdeKeyName
    tdeKeyUriWithVersion: keyVault!.outputs.sqlTdeKeyUriWithVersion
    tdeIdentityId: sql!.outputs.tdeIdentityId
    migrationIdentityName: sql!.outputs.migrationIdentityName
    migrationIdentityClientId: sql!.outputs.migrationIdentityClientId
  }
}

module documentIntelligence 'modules/document-intelligence.bicep' = if (isPrimary) {
  name: 'document-intelligence'
  params: {
    location: primaryRegion
    accountName: primaryInputs!.runtime.documentIntelligenceName
    applicationPrincipalId: primaryInputs!.runtime.managedIdentityPrincipalId
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
    profile: 'Primary'
    dataResourceGroupName: resourceGroup().name
    controlLedgerAccountName: controlLedgerCentralName
    controlLedgerReplicaAccountName: controlLedgerEastName
  }
}

module portal 'modules/app-service.bicep' = if (isPrimary) {
  name: 'app-service'
  params: {
    location: primaryRegion
    zoneRedundant: zoneCapability.primary
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
    documentIntelligenceResourceId: documentIntelligence!.outputs.resourceId
    controlAccountResourceIds: []
  }
  dependsOn: [
    keyVault
    sql
    sourceStorage
  ]
}

module ingress 'modules/application-gateway.bicep' = if (isPrimary) {
  name: 'application-gateway'
  params: {
    location: primaryRegion
    zoneRedundant: zoneCapability.primary
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

// ---------------------------------------------------------------------------
// Spec 016 T06: the manually activated Canada East recovery host (AC-107,
// DEP-18). Deployed only with the explicit activation input, into the recovery
// resource group, binding the approved inventory in the data resource group.

module recoveryEgress 'modules/egress.bicep' = if (activating) {
  name: 'recovery-egress'
  params: {
    location: recoveryRegion
    zoneRedundant: zoneCapability.recovery
    namePrefix: namePrefix
    firewallSubnetId: regionalNetwork.outputs.firewallSubnetId
    appIntegrationSubnetPrefix: network.appIntegrationSubnetPrefix
  }
}

module recoveryRuntime 'modules/runtime-configuration.bicep' = if (activating) {
  name: 'recovery-runtime-configuration'
  params: {
    primaryRegion: primaryRegion
    recoveryRegion: recoveryRegion
    profile: 'RegionalRecovery'
    dataResourceGroupName: dataResourceGroupName
    tenantId: recoveryInputs!.runtime.tenantId
    artifactVersion: recoveryInputs!.runtime.artifactVersion
    managedIdentityName: recoveryInputs!.runtime.managedIdentityName
    managedIdentityClientId: recoveryInputs!.runtime.managedIdentityClientId
    managedIdentityPrincipalId: recoveryInputs!.runtime.managedIdentityPrincipalId
    documentIntelligenceName: recoveryInputs!.runtime.documentIntelligenceName
    sourceStorageAccountName: recoveryInputs!.runtime.sourceStorageAccountName
    keyVaultName: recoveryInputs!.runtime.keyVaultName
    sqlServerName: recoverySqlServerName
    sqlDatabaseName: recoveryInputs!.runtime.sqlDatabaseName
    logAnalyticsWorkspaceName: recoveryInputs!.runtime.logAnalyticsWorkspaceName
    sourceEnvelopeKeyName: recoveryInputs!.runtime.sourceEnvelopeKeyName
    controlStorageAccountName: recoveryInputs!.runtime.controlStorageAccountName
    keyRingContainerName: recoveryInputs!.runtime.keyRingContainerName
    dataProtectionKeyName: recoveryInputs!.runtime.dataProtectionKeyName
    applicationDiscriminator: recoveryInputs!.runtime.applicationDiscriminator
    maximumAttempts: recoveryInputs!.runtime.maximumAttempts
    operationDeadline: recoveryInputs!.runtime.operationDeadline
    probeDeadline: recoveryInputs!.runtime.probeDeadline
    evidenceManifest: recoveryInputs!.runtime.evidenceManifest
    controlLedgerAccountName: controlLedgerCentralName
    controlLedgerReplicaAccountName: controlLedgerEastName
  }
}

module recoveryPortal 'modules/app-service.bicep' = if (activating) {
  name: 'recovery-app-service'
  params: {
    location: recoveryRegion
    zoneRedundant: zoneCapability.recovery
    namePrefix: namePrefix
    siteName: take('${recoveryInputs!.hosting.siteName}-ce', 60)
    minimumInstanceCount: recoveryInputs!.hosting.minimumInstanceCount
    maximumInstanceCount: recoveryInputs!.hosting.maximumInstanceCount
    backendCertificateThumbprint: recoveryInputs!.hosting.backendCertificateThumbprint
    appIntegrationSubnetId: regionalNetwork.outputs.appIntegrationSubnetId
    managedIdentityResourceId: resourceId(subscription().subscriptionId, dataResourceGroupName, 'Microsoft.ManagedIdentity/userAssignedIdentities', recoveryInputs!.runtime.managedIdentityName)
    publicHostName: recoveryInputs!.ingress.publicHostName
    runtimeSettings: recoveryRuntime!.outputs.appSettings
  }
  dependsOn: [
    recoveryEgress
  ]
}

module recoveryEndpoints 'modules/private-endpoints.bicep' = if (activating) {
  name: 'recovery-private-endpoints'
  params: {
    location: recoveryRegion
    namePrefix: namePrefix
    subnetId: regionalNetwork.outputs.privateEndpointSubnetId
    zoneIds: privateDns.outputs.zoneIds
    portalResourceId: recoveryPortal!.outputs.siteId
    sourceStorageResourceId: resourceId(subscription().subscriptionId, dataResourceGroupName, 'Microsoft.Storage/storageAccounts', recoveryInputs!.runtime.sourceStorageAccountName)
    sqlServerResourceId: resourceId(subscription().subscriptionId, dataResourceGroupName, 'Microsoft.Sql/servers', recoverySqlServerName)
    keyVaultResourceId: resourceId(subscription().subscriptionId, dataResourceGroupName, 'Microsoft.KeyVault/vaults', recoveryInputs!.runtime.keyVaultName)
    documentIntelligenceResourceId: resourceId(subscription().subscriptionId, dataResourceGroupName, 'Microsoft.CognitiveServices/accounts', recoveryInputs!.runtime.documentIntelligenceName)
    controlAccountResourceIds: [
      resourceId(subscription().subscriptionId, dataResourceGroupName, 'Microsoft.Storage/storageAccounts', recoveryInputs!.runtime.controlStorageAccountName)
      resourceId(subscription().subscriptionId, dataResourceGroupName, 'Microsoft.Storage/storageAccounts', controlLedgerCentralName)
      resourceId(subscription().subscriptionId, dataResourceGroupName, 'Microsoft.Storage/storageAccounts', controlLedgerEastName)
    ]
  }
}

module recoveryIngress 'modules/application-gateway.bicep' = if (activating) {
  name: 'recovery-application-gateway'
  params: {
    location: recoveryRegion
    zoneRedundant: zoneCapability.recovery
    namePrefix: namePrefix
    gatewaySubnetId: regionalNetwork.outputs.gatewaySubnetId
    gatewayIdentityResourceId: resourceId(subscription().subscriptionId, dataResourceGroupName, 'Microsoft.ManagedIdentity/userAssignedIdentities', recoveryInputs!.ingress.gatewayIdentityName)
    frontendCertificateUri: recoveryInputs!.ingress.frontendCertificateUri
    publicHostName: recoveryInputs!.ingress.publicHostName
    backendHostName: recoveryPortal!.outputs.defaultHostName
    minimumCapacity: recoveryInputs!.ingress.minimumCapacity
    maximumCapacity: recoveryInputs!.ingress.maximumCapacity
    fileUploadLimitInMb: recoveryInputs!.ingress.fileUploadLimitInMb
  }
  dependsOn: [
    recoveryEndpoints
  ]
}

output virtualNetworkId string = regionalNetwork.outputs.virtualNetworkId
output plannedFirewallAddress string = regionalNetwork.outputs.firewallPrivateAddress
output recoveryIngressAddressId string = activating ? recoveryIngress!.outputs.ingressAddressId : ''
output recoverySiteName string = activating ? take('${recoveryInputs!.hosting.siteName}-ce', 60) : ''
