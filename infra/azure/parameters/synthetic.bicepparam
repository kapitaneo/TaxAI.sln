// Spec 016 T02 - the isolated synthetic verification topology.
//
// Concrete, safe parameters for an isolated synthetic-only non-production
// subscription, used to run the private-network and egress probes against an
// exact build before real-data admission. Synthetic data only. Nothing here is
// a secret, and nothing here is production evidence: the manifest carries no
// DEP result, assurance or approval, so the Portal admission gate refuses it.
// The identifiers the release process issues (tenant, identity ids, artifact
// version, certificate thumbprint) are read from the environment of the
// explicitly authorized synthetic run.

using '../main.bicep'

param profile = 'primary'
param primaryRegion = 'canadacentral'
param recoveryRegion = 'canadaeast'
param namePrefix = 'taxai-syn'

param network = {
  addressPrefix: '10.60.0.0/22'
  gatewaySubnetPrefix: '10.60.0.0/24'
  appIntegrationSubnetPrefix: '10.60.1.0/26'
  privateEndpointSubnetPrefix: '10.60.1.64/26'
  firewallSubnetPrefix: '10.60.1.128/26'
}

param primary = {
  hosting: {
    siteName: 'taxai-syn-portal'
    minimumInstanceCount: 2
    maximumInstanceCount: 2
    backendCertificateThumbprint: readEnvironmentVariable('TAXAI_SYNTHETIC_BACKEND_THUMBPRINT', '0000000000000000000000000000000000000000')
  }
  ingress: {
    publicHostName: readEnvironmentVariable('TAXAI_SYNTHETIC_PUBLIC_HOST', 'synthetic.example.invalid')
    gatewayIdentityName: 'taxai-syn-id-gateway'
    frontendCertificateUri: 'https://taxai-syn-kv.vault.azure.net/secrets/frontend-certificate'
    minimumCapacity: 2
    maximumCapacity: 2
    fileUploadLimitInMb: 25
  }
  runtime: {
    tenantId: readEnvironmentVariable('TAXAI_SYNTHETIC_TENANT_ID', '00000000-0000-0000-0000-000000000000')
    artifactVersion: readEnvironmentVariable('TAXAI_SYNTHETIC_ARTIFACT_VERSION', '0.0.0-synthetic')
    managedIdentityName: 'taxai-syn-id-portal'
    managedIdentityClientId: readEnvironmentVariable('TAXAI_SYNTHETIC_IDENTITY_CLIENT_ID', '00000000-0000-0000-0000-000000000000')
    managedIdentityPrincipalId: readEnvironmentVariable('TAXAI_SYNTHETIC_IDENTITY_PRINCIPAL_ID', '00000000-0000-0000-0000-000000000000')
    documentIntelligenceName: 'taxai-syn-di'
    sourceStorageAccountName: 'taxaisynsrc'
    keyVaultName: 'taxai-syn-kv'
    sqlServerName: 'taxai-syn-sql'
    sqlDatabaseName: 'taxai'
    logAnalyticsWorkspaceName: 'taxai-syn-law'
    sourceEnvelopeKeyName: 'source-envelope'
    controlStorageAccountName: 'taxaisynctl'
    keyRingContainerName: 'data-protection'
    dataProtectionKeyName: 'data-protection'
    applicationDiscriminator: 'taxai-portal-synthetic'
    maximumAttempts: 3
    operationDeadline: '00:02:00'
    probeDeadline: '00:00:30'
    evidenceManifest: '{"schemaVersion":1}'
  }
}
