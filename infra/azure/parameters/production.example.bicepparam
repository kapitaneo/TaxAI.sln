// Spec 016 T02 - EXAMPLE primary production topology parameters.
//
// NON-DEPLOYABLE. Every identifier below is an obvious placeholder (all-zero
// GUIDs, example.invalid host, zero thumbprint) that the Portal admission gate,
// the offline configuration check and Azure itself refuse. The real values are
// supplied by the approved release inventory, never committed. The evidence
// manifest here carries no DEP result, assurance or approval: an example file
// never grants real-data admission.

using '../main.bicep'

param profile = 'primary'
param primaryRegion = 'canadacentral'
param recoveryRegion = 'canadaeast'
param namePrefix = 'taxai-prod'

param network = {
  addressPrefix: '10.40.0.0/22'
  gatewaySubnetPrefix: '10.40.0.0/24'
  appIntegrationSubnetPrefix: '10.40.1.0/26'
  privateEndpointSubnetPrefix: '10.40.1.64/26'
  firewallSubnetPrefix: '10.40.1.128/26'
}

param primary = {
  hosting: {
    siteName: 'taxai-prod-portal-example'
    minimumInstanceCount: 2
    maximumInstanceCount: 4
    backendCertificateThumbprint: '0000000000000000000000000000000000000000'
  }
  ingress: {
    publicHostName: 'portal.example.invalid'
    gatewayIdentityName: 'taxai-prod-id-gateway'
    frontendCertificateUri: 'https://taxai-prod-kv-example.vault.azure.net/secrets/frontend-certificate'
    minimumCapacity: 2
    maximumCapacity: 4
    fileUploadLimitInMb: 25
  }
  runtime: {
    tenantId: '00000000-0000-0000-0000-000000000000'
    artifactVersion: '0.0.0-example'
    managedIdentityName: 'taxai-prod-id-portal'
    managedIdentityClientId: '00000000-0000-0000-0000-000000000000'
    managedIdentityPrincipalId: '00000000-0000-0000-0000-000000000000'
    documentIntelligenceName: 'taxai-prod-di-example'
    sourceStorageAccountName: 'taxaiprodsrcexample'
    keyVaultName: 'taxai-prod-kv-example'
    sqlServerName: 'taxai-prod-sql-example'
    sqlDatabaseName: 'taxai'
    logAnalyticsWorkspaceName: 'taxai-prod-law-example'
    sourceEnvelopeKeyName: 'source-envelope'
    controlStorageAccountName: 'taxaiprodctlexample'
    keyRingContainerName: 'data-protection'
    dataProtectionKeyName: 'data-protection'
    applicationDiscriminator: 'taxai-portal-example'
    maximumAttempts: 3
    operationDeadline: '00:02:00'
    probeDeadline: '00:00:30'
    evidenceManifest: '{"schemaVersion":1}'
  }
}
