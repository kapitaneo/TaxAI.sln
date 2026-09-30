// Spec 016 T01 — production runtime configuration for the TaxAI Portal.
//
// This module creates no Azure resource. It turns the approved production
// inventory into the exact, closed set of App Service application settings
// described by ../contracts/runtime-configuration.schema.json, so the values the
// Portal's admission gate validates come from reviewed infrastructure-as-code
// rather than hand-edited settings.
//
// Every policy value is fixed here or constrained with @allowed. There is no
// parameter that enables production, relaxes a region, allows public network
// access, selects another Document Intelligence API/model, or adds an outbound
// destination. The output contains infrastructure identifiers only: no secret,
// key material, password or taxpayer value. The evidence manifest is supplied
// by the release process and is itself secret-free.
//
// Producing these settings does not authorize real-taxpayer processing. The
// Portal still refuses to start unless the actual registrations, the issued
// managed identity, the exact artifact version and every DEP item, both provider
// assurances and both role approvals are present and valid.

targetScope = 'resourceGroup'

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

@description('Entra tenant of the Portal managed identity.')
param tenantId string

@description('Exact artifact version; must equal the deployed build\'s informational version.')
@minLength(1)
@maxLength(128)
param artifactVersion string

@description('Name of the Portal user-assigned managed identity.')
param managedIdentityName string

@description('Client id of the Portal user-assigned managed identity.')
param managedIdentityClientId string

@description('Object (principal) id of the Portal user-assigned managed identity.')
param managedIdentityPrincipalId string

@description('Name of the approved Canada Central Document Intelligence resource (custom subdomain).')
param documentIntelligenceName string

@description('Name of the source-content storage account.')
param sourceStorageAccountName string

@description('Name of the Key Vault.')
param keyVaultName string

@description('Name of the Azure SQL logical server.')
param sqlServerName string

@description('Name of the application database.')
param sqlDatabaseName string

@description('Name of the Canada Central Log Analytics workspace.')
param logAnalyticsWorkspaceName string

@description('Name of the versionless Key Vault key that wraps per-content source keys.')
param sourceEnvelopeKeyName string

@description('Name of the private control storage account that holds the Data Protection key ring. Must differ from the source account.')
param controlStorageAccountName string

@description('Key-ring container in the control storage account.')
param keyRingContainerName string

@description('Name of the versionless Key Vault key that wraps the Data Protection key ring. Must differ from the source envelope key.')
param dataProtectionKeyName string

@description('Application discriminator shared only by intended production instances.')
param applicationDiscriminator string

@description('[ASSUMED] Finite maximum attempts for dependency calls (1 to 5).')
@minValue(1)
@maxValue(5)
param maximumAttempts int

@description('[ASSUMED] Finite operation deadline, c-format TimeSpan, greater than zero and at most 00:10:00.')
param operationDeadline string

@description('[ASSUMED] Finite managed-identity probe deadline, c-format TimeSpan, greater than zero and at most 00:01:00.')
param probeDeadline string

@description('Release evidence manifest JSON (production-evidence.schema.json). Secret-free.')
param evidenceManifest string

@description('Spec 016 T06: name of the Canada Central recovery control-ledger account (control-storage.bicep).')
@minLength(3)
@maxLength(24)
param controlLedgerAccountName string

@description('Spec 016 T06: name of the authoritative Canada East recovery control-ledger account (control-storage.bicep).')
@minLength(3)
@maxLength(24)
param controlLedgerReplicaAccountName string

var resourcePrefix = '/subscriptions/${subscription().subscriptionId}/resourceGroups/${resourceGroup().name}/providers'

var privateDependency = {
  Region: primaryRegion
  PublicNetworkAccess: 'Disabled'
  PrivateEndpoint: 'Required'
}

var production = 'TaxAi__Production__'

// Azure public cloud suffixes, read from the deployment environment rather than
// hardcoded. The validator requires exactly these canonical resource hosts.
var keyVaultHost = environment().suffixes.keyvaultDns
var sqlHost = environment().suffixes.sqlServerHostname

output appSettings object = {
  '${production}Environment': 'Production'
  '${production}PrimaryRegion': primaryRegion
  '${production}RecoveryRegion': recoveryRegion
  '${production}TenantId': tenantId
  '${production}SubscriptionId': subscription().subscriptionId
  '${production}ResourceGroup': resourceGroup().name
  '${production}ArtifactVersion': artifactVersion
  '${production}ManagedIdentity__ClientId': managedIdentityClientId
  '${production}ManagedIdentity__PrincipalId': managedIdentityPrincipalId
  '${production}ManagedIdentity__ResourceId': '${resourcePrefix}/Microsoft.ManagedIdentity/userAssignedIdentities/${managedIdentityName}'
  '${production}DocumentIntelligence__Endpoint': 'https://${documentIntelligenceName}.cognitiveservices.azure.com/'
  '${production}DocumentIntelligence__ResourceId': '${resourcePrefix}/Microsoft.CognitiveServices/accounts/${documentIntelligenceName}'
  '${production}DocumentIntelligence__Region': privateDependency.Region
  '${production}DocumentIntelligence__PublicNetworkAccess': privateDependency.PublicNetworkAccess
  '${production}DocumentIntelligence__PrivateEndpoint': privateDependency.PrivateEndpoint
  '${production}DocumentIntelligence__ApiVersion': '2024-11-30'
  '${production}DocumentIntelligence__Model': 'prebuilt-layout'
  '${production}DocumentIntelligence__Features': 'keyValuePairs'
  '${production}SourceStorage__Endpoint': 'https://${sourceStorageAccountName}.blob.${environment().suffixes.storage}/'
  '${production}SourceStorage__ResourceId': '${resourcePrefix}/Microsoft.Storage/storageAccounts/${sourceStorageAccountName}'
  '${production}SourceStorage__Region': privateDependency.Region
  '${production}SourceStorage__PublicNetworkAccess': privateDependency.PublicNetworkAccess
  '${production}SourceStorage__PrivateEndpoint': privateDependency.PrivateEndpoint
  '${production}KeyVault__Endpoint': 'https://${keyVaultName}${keyVaultHost}/'
  '${production}KeyVault__ResourceId': '${resourcePrefix}/Microsoft.KeyVault/vaults/${keyVaultName}'
  '${production}KeyVault__Region': privateDependency.Region
  '${production}KeyVault__PublicNetworkAccess': privateDependency.PublicNetworkAccess
  '${production}KeyVault__PrivateEndpoint': privateDependency.PrivateEndpoint
  '${production}Sql__Endpoint': '${sqlServerName}${sqlHost}'
  '${production}Sql__ResourceId': '${resourcePrefix}/Microsoft.Sql/servers/${sqlServerName}'
  '${production}Sql__Region': privateDependency.Region
  '${production}Sql__PublicNetworkAccess': privateDependency.PublicNetworkAccess
  '${production}Sql__PrivateEndpoint': privateDependency.PrivateEndpoint
  '${production}Monitoring__WorkspaceResourceId': '${resourcePrefix}/Microsoft.OperationalInsights/workspaces/${logAnalyticsWorkspaceName}'
  '${production}Monitoring__Region': primaryRegion
  '${production}Monitoring__RetentionDays': '30'
  '${production}SourceEnvelope__KeyUri': 'https://${keyVaultName}${keyVaultHost}/keys/${sourceEnvelopeKeyName}'
  '${production}DataProtection__StorageEndpoint': 'https://${controlStorageAccountName}.blob.${environment().suffixes.storage}/'
  '${production}DataProtection__StorageResourceId': '${resourcePrefix}/Microsoft.Storage/storageAccounts/${controlStorageAccountName}'
  '${production}DataProtection__KeyRingContainer': keyRingContainerName
  '${production}DataProtection__KeyUri': 'https://${keyVaultName}${keyVaultHost}/keys/${dataProtectionKeyName}'
  '${production}DataProtection__ApplicationDiscriminator': applicationDiscriminator
  '${production}Retention__FinalizedSourceDays': '30'
  '${production}Retention__RejectedSourceDays': '7'
  '${production}Retention__DuplicateSourceDays': '7'
  '${production}Retention__AbandonedSourceDays': '30'
  '${production}Retention__StagedUploadHours': '24'
  '${production}Resilience__MaximumAttempts': string(maximumAttempts)
  '${production}Resilience__OperationDeadline': operationDeadline
  '${production}Resilience__ProbeDeadline': probeDeadline
  '${production}Recovery__Profile': 'Primary'
  '${production}Recovery__ControlLedger__Endpoint': 'https://${controlLedgerAccountName}.blob.${environment().suffixes.storage}/'
  '${production}Recovery__ControlLedger__ResourceId': '${resourcePrefix}/Microsoft.Storage/storageAccounts/${controlLedgerAccountName}'
  '${production}Recovery__ControlLedger__Region': primaryRegion
  '${production}Recovery__ControlLedger__PublicNetworkAccess': privateDependency.PublicNetworkAccess
  '${production}Recovery__ControlLedger__PrivateEndpoint': privateDependency.PrivateEndpoint
  '${production}Recovery__ControlLedgerReplica__Endpoint': 'https://${controlLedgerReplicaAccountName}.blob.${environment().suffixes.storage}/'
  '${production}Recovery__ControlLedgerReplica__ResourceId': '${resourcePrefix}/Microsoft.Storage/storageAccounts/${controlLedgerReplicaAccountName}'
  '${production}Recovery__ControlLedgerReplica__Region': recoveryRegion
  '${production}Recovery__ControlLedgerReplica__PublicNetworkAccess': privateDependency.PublicNetworkAccess
  '${production}Recovery__ControlLedgerReplica__PrivateEndpoint': privateDependency.PrivateEndpoint
  '${production}Recovery__SqlPointInTimeRestoreDays': '35'
  '${production}Recovery__BackupChainDays': '7'
  '${production}Recovery__ControlLedgerRetentionDays': '90'
  '${production}EvidenceManifest': evidenceManifest
  TaxAi__Extraction__Provider: 'AzureDocumentIntelligence'
  TaxAi__Extraction__ContentReader: 'AzureBlobSourceReader'
  ConnectionStrings__TaxAiDatabase: 'Server=tcp:${sqlServerName}${sqlHost},1433;Database=${sqlDatabaseName};Encrypt=Strict;TrustServerCertificate=False;Authentication=Active Directory Managed Identity;User Id=${managedIdentityClientId}'
}
