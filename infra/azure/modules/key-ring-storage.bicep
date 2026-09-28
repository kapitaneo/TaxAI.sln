// Spec 016 T03 (FR-04) - the shared, protected Data Protection key-ring store.
//
// A dedicated control storage account, separate from source content (the
// source account belongs to T04 and never holds the key ring): Canadian GZRS,
// Microsoft Entra authorization only (shared keys, SAS and anonymous access
// disabled), HTTPS with TLS 1.2 or later, infrastructure encryption, public
// network access disabled with a deny-default firewall, and one private
// endpoint in the approved private-endpoint subnet bound to the existing blob
// private DNS zone.
//
// It holds only the ASP.NET Core Data Protection key ring, and every key-ring
// key is already wrapped by the Key Vault Data Protection key before it is
// written, so no key material is stored here in plaintext. [ASSUMED] The
// account itself therefore uses platform-managed service encryption plus
// infrastructure encryption; it holds no taxpayer source or structured data.
//
// Losing the key ring would invalidate every protected session and
// antiforgery token, so this control store deliberately does NOT inherit the
// source store's short retention: blob versioning and 30-day blob and container
// soft delete stay on.
//
// The Portal identity receives Storage Blob Data Contributor on the key-ring
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

@description('The control storage account name (runtime DataProtection:StorageEndpoint).')
@minLength(3)
@maxLength(24)
param accountName string

@description('The key-ring container name (runtime DataProtection:KeyRingContainer).')
@minLength(3)
@maxLength(63)
param containerName string

@description('The Portal user-assigned managed identity principal (object) ID.')
@minLength(36)
@maxLength(36)
param applicationPrincipalId string

@description('Private endpoint subnet.')
param privateEndpointSubnetId string

@description('The blob private DNS zone ID.')
@minLength(1)
param blobZoneId string

var blobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: accountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_GZRS'
  }
  properties: {
    accessTier: 'Hot'
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
      keySource: 'Microsoft.Storage'
      requireInfrastructureEncryption: true
      services: {
        blob: {
          enabled: true
          keyType: 'Account'
        }
      }
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    isVersioningEnabled: true
    deleteRetentionPolicy: {
      enabled: true
      days: 30
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 30
    }
  }
}

resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'None'
  }
}

resource applicationKeyRingAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: container
  name: guid(container.id, applicationPrincipalId, blobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributor)
    principalId: applicationPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource endpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: '${namePrefix}-pe-keyring'
  location: location
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'keyring'
        properties: {
          privateLinkServiceId: account.id
          groupIds: [
            'blob'
          ]
        }
      }
    ]
  }
}

resource zoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: endpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'keyring'
        properties: {
          privateDnsZoneId: blobZoneId
        }
      }
    ]
  }
}

output accountId string = account.id
