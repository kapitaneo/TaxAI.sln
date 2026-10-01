// Spec 016 T06 (FR-10, FR-11) - the independent recovery control ledger.
//
// Two dedicated private control accounts, separate from the source store, the
// Data Protection key ring and the application database:
//
//   ledgerCentral  Canada Central, zone-redundant (Standard_ZRS).
//   ledgerEast     Canada East, the authoritative, independently recoverable
//                  copy. [ASSUMED] Canada East offers no availability zones, so
//                  it is locally redundant (Standard_LRS); its independence
//                  comes from being a second region, not from zones.
//
// Neither account replicates to the other. The application writes each fact to
// Canada East and then Canada Central and reads each copy back; an asynchronous
// storage replica is never counted as an acknowledgement.
//
// Each account holds the same three containers, each under a time-based
// immutability policy of 90 days - strictly longer than the 35-day SQL
// point-in-time retention plus the documented weekly backup chain (42 days) -
// with protected append writes off, so no record can be changed or deleted
// while any backup that predates it can be restored:
//
//   taxai-recovery-ledger       terminal destruction, genesis and release facts
//                               (the Portal identity may create, never delete)
//   taxai-recovery-attestation  the provisioning process's current membership
//                               attestations (the Portal identity reads only)
//   taxai-release-evidence      the exact-release evidence documents and their
//                               withdrawal markers (the Portal identity reads only)
//
// A policy is created unlocked; locking is the irreversible operator step in
// docs/operations/canadian-recovery.md, and scripts/azure/Invoke-RecoveryDrill.ps1
// verifies the locked state and the retention. The records are opaque
// identifiers and UTC instants only, so the accounts use platform-managed
// service encryption plus infrastructure encryption; no taxpayer value is held.
//
// Microsoft Entra authorization only; shared keys, SAS, anonymous and
// cross-tenant access off; HTTPS with TLS 1.2 or later; public network access
// disabled with a deny-default firewall and no trusted-service bypass; blob
// soft delete and versioning off (immutability is the retention control). Each
// account is reached only through its own private endpoint in the approved
// Canada Central private-endpoint subnet.

@description('Primary region. Only Canada Central is approved.')
@allowed([
  'canadacentral'
])
param location string

@description('Recovery-only region. Only Canada East is approved.')
@allowed([
  'canadaeast'
])
param recoveryLocation string

@description('Resource name prefix.')
@minLength(3)
@maxLength(16)
param namePrefix string

@description('The Canada Central control-ledger account name (runtime Recovery:ControlLedger:Endpoint).')
@minLength(3)
@maxLength(24)
param centralAccountName string

@description('The Canada East control-ledger account name (runtime Recovery:ControlLedgerReplica:Endpoint).')
@minLength(3)
@maxLength(24)
param eastAccountName string

@description('The Portal user-assigned managed identity principal (object) ID.')
@minLength(36)
@maxLength(36)
param applicationPrincipalId string

@description('Private endpoint subnet.')
param privateEndpointSubnetId string

@description('The blob private DNS zone ID.')
@minLength(1)
param blobZoneId string

// Fixed policy, not tuning: 35-day SQL point-in-time retention + 7-day weekly
// backup chain = 42 days recoverable; the ledger outlives it.
var ledgerImmutabilityDays = 90
var ledgerContainerName = 'taxai-recovery-ledger'
var attestationContainerName = 'taxai-recovery-attestation'
var evidenceContainerName = 'taxai-release-evidence'
var blobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var blobDataReader = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'

var copies = [
  {
    key: 'cc'
    name: centralAccountName
    location: location
    sku: 'Standard_ZRS'
  }
  {
    key: 'ce'
    name: eastAccountName
    location: recoveryLocation
    sku: 'Standard_LRS'
  }
]

resource accounts 'Microsoft.Storage/storageAccounts@2023-05-01' = [for copy in copies: {
  name: copy.name
  location: copy.location
  kind: 'StorageV2'
  sku: {
    name: copy.sku
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
}]

resource blobServices 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = [for (copy, index) in copies: {
  parent: accounts[index]
  name: 'default'
  properties: {
    isVersioningEnabled: false
    deleteRetentionPolicy: {
      enabled: false
    }
    containerDeleteRetentionPolicy: {
      enabled: false
    }
  }
}]

resource ledgers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [for (copy, index) in copies: {
  parent: blobServices[index]
  name: ledgerContainerName
  properties: {
    publicAccess: 'None'
  }
}]

resource attestations 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [for (copy, index) in copies: {
  parent: blobServices[index]
  name: attestationContainerName
  properties: {
    publicAccess: 'None'
  }
}]

resource evidence 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [for (copy, index) in copies: {
  parent: blobServices[index]
  name: evidenceContainerName
  properties: {
    publicAccess: 'None'
  }
}]

resource ledgerImmutability 'Microsoft.Storage/storageAccounts/blobServices/containers/immutabilityPolicies@2023-05-01' = [for (copy, index) in copies: {
  parent: ledgers[index]
  name: 'default'
  properties: {
    immutabilityPeriodSinceCreationInDays: ledgerImmutabilityDays
    allowProtectedAppendWrites: false
  }
}]

resource attestationImmutability 'Microsoft.Storage/storageAccounts/blobServices/containers/immutabilityPolicies@2023-05-01' = [for (copy, index) in copies: {
  parent: attestations[index]
  name: 'default'
  properties: {
    immutabilityPeriodSinceCreationInDays: ledgerImmutabilityDays
    allowProtectedAppendWrites: false
  }
}]

resource evidenceImmutability 'Microsoft.Storage/storageAccounts/blobServices/containers/immutabilityPolicies@2023-05-01' = [for (copy, index) in copies: {
  parent: evidence[index]
  name: 'default'
  properties: {
    immutabilityPeriodSinceCreationInDays: ledgerImmutabilityDays
    allowProtectedAppendWrites: false
  }
}]

// Least privilege, per container: the Portal may create ledger records (the
// immutability policy forbids changing or deleting them) and only read
// attestations and evidence, which the provisioning and release processes write.
resource ledgerAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (copy, index) in copies: {
  scope: ledgers[index]
  name: guid(ledgers[index].id, applicationPrincipalId, blobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributor)
    principalId: applicationPrincipalId
    principalType: 'ServicePrincipal'
  }
}]

resource attestationAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (copy, index) in copies: {
  scope: attestations[index]
  name: guid(attestations[index].id, applicationPrincipalId, blobDataReader)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataReader)
    principalId: applicationPrincipalId
    principalType: 'ServicePrincipal'
  }
}]

resource evidenceAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (copy, index) in copies: {
  scope: evidence[index]
  name: guid(evidence[index].id, applicationPrincipalId, blobDataReader)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataReader)
    principalId: applicationPrincipalId
    principalType: 'ServicePrincipal'
  }
}]

resource endpoints 'Microsoft.Network/privateEndpoints@2024-05-01' = [for (copy, index) in copies: {
  name: '${namePrefix}-pe-ledger-${copy.key}'
  location: location
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'ledger-${copy.key}'
        properties: {
          privateLinkServiceId: accounts[index].id
          groupIds: [
            'blob'
          ]
        }
      }
    ]
  }
}]

resource zoneGroups 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = [for (copy, index) in copies: {
  parent: endpoints[index]
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'ledger-${copy.key}'
        properties: {
          privateDnsZoneId: blobZoneId
        }
      }
    ]
  }
}]

output centralAccountId string = accounts[0].id
output eastAccountId string = accounts[1].id
output immutabilityDays int = ledgerImmutabilityDays
