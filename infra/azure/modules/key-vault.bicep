// Spec 016 T03 (FR-04) - the Canadian Key Vault and its approved
// customer-managed keys.
//
// One Premium vault in Canada Central: Azure RBAC authorization only (no access
// policies), soft delete with the maximum 90-day retention AND purge protection,
// public network access disabled with a deny-default firewall (trusted Azure
// services may still reach it, which the Azure SQL TDE and Storage CMK
// integrations require behind a firewall), and no deployment, disk-encryption or
// template-deployment access. The Portal reaches it only through its private
// endpoint (private-endpoints.bicep, T02).
//
// Four distinct RSA-HSM 3072 keys, one per purpose, each non-exportable, allowed
// only to wrap and unwrap, and rotated by Key Vault every 90 days:
//   - the source-envelope key (per-content data keys, consumed by T04),
//   - the Data Protection key (the shared key ring),
//   - the SQL TDE protector (sql.bicep grants the SQL identity access to it),
//   - the source Blob service-encryption key (bound by T04's storage module).
// Rotation adds a version; it never deletes one. Retiring an old version is a
// separate, drilled operation refused while any retained ciphertext, key-ring
// entry or recoverable backup still needs it (Invoke-KeyRotationDrill.ps1).
//
// The Portal identity receives exactly two data-plane grants on this vault's
// keys - wrap/unwrap on the source-envelope and Data Protection keys - plus read
// access to exactly the two intake credential secrets. The secrets' values are
// provisioned out of band by the documented procedure (docs/operations/
// key-rotation.md) and never appear in this repository; referencing them here
// makes a deployment that precedes their provisioning fail rather than grant a
// vault-wide role.

@description('Primary region. Only Canada Central is approved.')
@allowed([
  'canadacentral'
])
param location string

@description('The Key Vault name (the runtime profile binds its endpoint).')
@minLength(3)
@maxLength(24)
param keyVaultName string

@description('Entra tenant that owns the vault.')
@minLength(36)
@maxLength(36)
param tenantId string

@description('The source-envelope wrapping key name (runtime SourceEnvelope:KeyUri).')
@minLength(1)
@maxLength(127)
param sourceEnvelopeKeyName string

@description('The Data Protection wrapping key name (runtime DataProtection:KeyUri).')
@minLength(1)
@maxLength(127)
param dataProtectionKeyName string

@description('The Portal user-assigned managed identity principal (object) ID.')
@minLength(36)
@maxLength(36)
param applicationPrincipalId string

// Fixed names for the two keys no runtime setting names, and for the two intake
// credential secrets the Portal reads by fixed name.
var sqlTdeKeyName = 'sql-tde-protector'
var sourceBlobKeyName = 'source-blob-cmk'
var intakeSecretNames = [
  'taxai-intake-digest-key'
  'taxai-intake-fingerprint-key'
]

// Built-in data-plane roles, by their fixed role definition IDs.
var cryptoServiceEncryptionUser = 'e147488a-f6f5-4113-8e2d-b22465e65bf6'
var secretsUser = '4633458b-17de-408a-b874-0445c86b69e6'

var rotationPolicy = {
  lifetimeActions: [
    {
      trigger: {
        timeAfterCreate: 'P90D'
      }
      action: {
        type: 'rotate'
      }
    }
  ]
}

var keyNames = [
  sourceEnvelopeKeyName
  dataProtectionKeyName
  sqlTdeKeyName
  sourceBlobKeyName
]

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'premium'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Disabled'
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: false
  }
}

// A repeated name defines the same key twice, which Azure Resource Manager
// rejects, so purposes can never silently share a key.
resource keys 'Microsoft.KeyVault/vaults/keys@2023-07-01' = [for name in keyNames: {
  parent: vault
  name: name
  properties: {
    kty: 'RSA-HSM'
    keySize: 3072
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
    attributes: {
      enabled: true
      exportable: false
    }
    rotationPolicy: rotationPolicy
  }
}]

resource intakeSecrets 'Microsoft.KeyVault/vaults/secrets@2023-07-01' existing = [for name in intakeSecretNames: {
  parent: vault
  name: name
}]

// keys[0] is the source-envelope key and keys[1] the Data Protection key.
resource applicationKeyAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for index in range(0, 2): {
  scope: keys[index]
  name: guid(keys[index].id, applicationPrincipalId, cryptoServiceEncryptionUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cryptoServiceEncryptionUser)
    principalId: applicationPrincipalId
    principalType: 'ServicePrincipal'
  }
}]

resource applicationSecretAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (name, index) in intakeSecretNames: {
  scope: intakeSecrets[index]
  name: guid(intakeSecrets[index].id, applicationPrincipalId, secretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', secretsUser)
    principalId: applicationPrincipalId
    principalType: 'ServicePrincipal'
  }
}]

output vaultId string = vault.id
output sqlTdeKeyName string = sqlTdeKeyName
output sqlTdeKeyUriWithVersion string = keys[2].properties.keyUriWithVersion
output sourceBlobKeyName string = sourceBlobKeyName
