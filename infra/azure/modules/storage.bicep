// Spec 016 T04 (FR-03, FR-04) - the encrypted Canadian source store.
//
// One standard general-purpose v2 account in Canada Central with geo-zone-
// redundant storage (Standard_GZRS: synchronous zonal copies in Canada Central,
// asynchronous replication to the paired Canada East region; no read access to
// the secondary and no zero-loss claim). It holds only upload-once source
// ciphertext and its separate key envelopes, both already encrypted by the
// application with a fresh per-content data key wrapped by the Key Vault
// source-envelope key. Storage service encryption is additional protection: it
// uses the approved customer-managed 'source-blob-cmk' key, reached by a
// dedicated user-assigned identity holding wrap/unwrap on that one key only,
// with infrastructure encryption also on. The versionless key reference follows
// the key's 90-day rotation.
//
// Microsoft Entra authorization only: shared keys, SAS, anonymous and
// cross-tenant access are off. HTTPS with TLS 1.2 or later. Public network
// access is disabled with a deny-default firewall and no trusted-service bypass;
// the only path in is the private endpoint bound by private-endpoints.bicep
// (T02) to this account's resource ID.
//
// The user's safe baseline (FR-03): source blob versioning, blob and container
// soft delete, change feed, point-in-time restore, snapshots and every
// time-based or legal-hold immutability are DISABLED, and no lifecycle
// management policy is created here - due lifecycle deletion is not invented by
// this slice. None of these is a parameter.
//
// The Portal identity receives Storage Blob Data Contributor on the one source
// container only - not the account and not any other container.

@description('Primary region. Only Canada Central is approved.')
@allowed([
  'canadacentral'
])
param location string

@description('Resource name prefix.')
@minLength(3)
@maxLength(16)
param namePrefix string

@description('The source storage account name (runtime SourceStorage:Endpoint).')
@minLength(3)
@maxLength(24)
param accountName string

@description('The Key Vault that holds the source Blob customer-managed key.')
@minLength(3)
@maxLength(24)
param keyVaultName string

@description('The source Blob customer-managed key name.')
@minLength(1)
@maxLength(127)
param sourceBlobKeyName string

@description('The Portal user-assigned managed identity principal (object) ID.')
@minLength(36)
@maxLength(36)
param applicationPrincipalId string

// The one private source container. The application names the same container
// (AzureIntakeContentStore.SourceContainerName); it is not configurable.
var sourceContainerName = 'taxai-source'
var blobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var cryptoServiceEncryptionUser = 'e147488a-f6f5-4113-8e2d-b22465e65bf6'

resource cmkIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-id-source-cmk'
  location: location
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource sourceBlobKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' existing = {
  parent: vault
  name: sourceBlobKeyName
}

resource cmkAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sourceBlobKey
  name: guid(sourceBlobKey.id, cmkIdentity.id, cryptoServiceEncryptionUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cryptoServiceEncryptionUser)
    principalId: cmkIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: accountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_GZRS'
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${cmkIdentity.id}': {}
    }
  }
  properties: {
    accessTier: 'Hot'
    isHnsEnabled: false
    isSftpEnabled: false
    isLocalUserEnabled: false
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    allowCrossTenantReplication: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Disabled'
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'None'
    }
    encryption: {
      keySource: 'Microsoft.Keyvault'
      requireInfrastructureEncryption: true
      identity: {
        userAssignedIdentity: cmkIdentity.id
      }
      keyvaultproperties: {
        keyname: sourceBlobKeyName
        keyvaulturi: vault.properties.vaultUri
      }
      services: {
        blob: {
          enabled: true
          keyType: 'Account'
        }
      }
    }
  }
  dependsOn: [
    cmkAccess
  ]
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    isVersioningEnabled: false
    deleteRetentionPolicy: {
      enabled: false
    }
    containerDeleteRetentionPolicy: {
      enabled: false
    }
    changeFeed: {
      enabled: false
    }
    restorePolicy: {
      enabled: false
    }
  }
}

resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: sourceContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource applicationSourceAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: container
  name: guid(container.id, applicationPrincipalId, blobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributor)
    principalId: applicationPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output accountId string = account.id
output containerName string = sourceContainerName
