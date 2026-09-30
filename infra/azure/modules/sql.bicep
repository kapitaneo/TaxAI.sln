// Spec 016 T03 (FR-04) - the secure Azure SQL Database.
//
// The existing Azure SQL Database family (a logical server, not Managed
// Instance) in Canada Central: Microsoft Entra-only authentication (no SQL
// logins, no stored password), public network access disabled, outbound
// network access restricted, TLS 1.2 or later, and a zone-redundant database.
// The Portal reaches it only through its private endpoint
// (private-endpoints.bicep, T02) with managed-identity authentication and
// strict encryption, which the Portal's production profile validator and the
// Persistence connection guard both enforce.
//
// Transparent data encryption uses the customer-managed SQL TDE protector in
// the approved Key Vault, reached by a dedicated user-assigned SQL identity that
// holds wrap/unwrap on that one key only. The encryption protector auto-rotates
// with the key's 90-day rotation, so the server always follows the current
// version and old versions stay available to backups while retained.
//
// The Entra administrator is a dedicated user-assigned migration identity: only
// it applies reviewed migrations. The Portal identity is never an administrator.
// Spec 016 T06 (FR-11, AC-111, AC-112) adds the backup policy: point-in-time
// restore at the 35-day maximum with 12-hour differentials, geo-zone-redundant
// backup storage (Canada Central zones, asynchronously copied to the paired
// Canada East region, so a Canadian geo-restore remains possible), and an
// explicitly empty long-term-retention policy - no seven-year raw SQL archive
// exists because application records live seven years. The Canada East
// geo-secondary and the manual failover group are recovery.bicep. Nothing here
// enables a diagnostic or auditing sink.

@description('Primary region. Only Canada Central is approved.')
@allowed([
  'canadacentral'
])
param location string

@description('Resource name prefix.')
@minLength(3)
@maxLength(16)
param namePrefix string

@description('The SQL logical server name (runtime Sql:Endpoint).')
@minLength(1)
@maxLength(63)
param sqlServerName string

@description('The application database name.')
@minLength(1)
@maxLength(128)
param sqlDatabaseName string

@description('Entra tenant for the server administrator.')
@minLength(36)
@maxLength(36)
param tenantId string

@description('The Key Vault that holds the TDE protector.')
@minLength(3)
@maxLength(24)
param keyVaultName string

@description('The TDE protector key name.')
@minLength(1)
@maxLength(127)
param tdeKeyName string

@description('The TDE protector key identifier, including its current version.')
@minLength(1)
param tdeKeyUriWithVersion string

var cryptoServiceEncryptionUser = 'e147488a-f6f5-4113-8e2d-b22465e65bf6'
var tdeKeyVersion = last(split(tdeKeyUriWithVersion, '/'))

resource tdeIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-id-sql-tde'
  location: location
}

resource migrationIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-id-sql-migration'
  location: location
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource tdeKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' existing = {
  parent: vault
  name: tdeKeyName
}

resource tdeKeyAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: tdeKey
  name: guid(tdeKey.id, tdeIdentity.id, cryptoServiceEncryptionUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cryptoServiceEncryptionUser)
    principalId: tdeIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource server 'Microsoft.Sql/servers@2023-08-01' = {
  name: sqlServerName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${tdeIdentity.id}': {}
    }
  }
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    restrictOutboundNetworkAccess: 'Enabled'
    primaryUserAssignedIdentityId: tdeIdentity.id
    keyId: tdeKeyUriWithVersion
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      principalType: 'Application'
      login: migrationIdentity.name
      sid: migrationIdentity.properties.clientId
      tenantId: tenantId
    }
  }
  dependsOn: [
    tdeKeyAccess
  ]
}

resource serverKey 'Microsoft.Sql/servers/keys@2023-08-01' = {
  parent: server
  name: '${keyVaultName}_${tdeKeyName}_${tdeKeyVersion}'
  properties: {
    serverKeyType: 'AzureKeyVault'
    uri: tdeKeyUriWithVersion
  }
}

resource encryptionProtector 'Microsoft.Sql/servers/encryptionProtector@2023-08-01' = {
  parent: server
  name: 'current'
  properties: {
    serverKeyType: 'AzureKeyVault'
    serverKeyName: serverKey.name
    autoRotationEnabled: true
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: server
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'GP_Gen5_2'
    tier: 'GeneralPurpose'
  }
  properties: {
    zoneRedundant: true
    requestedBackupStorageRedundancy: 'GeoZone'
  }
  dependsOn: [
    encryptionProtector
  ]
}

// Spec 016 T06: the maximum practical short-term window, capped at 35 days.
resource shortTermRetention 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2023-08-01' = {
  parent: database
  name: 'default'
  properties: {
    retentionDays: 35
    diffBackupIntervalInHours: 12
  }
}

// Spec 016 T06: explicitly no long-term retention of any kind.
resource longTermRetention 'Microsoft.Sql/servers/databases/backupLongTermRetentionPolicies@2023-08-01' = {
  parent: database
  name: 'default'
  properties: {
    weeklyRetention: 'PT0S'
    monthlyRetention: 'PT0S'
    yearlyRetention: 'PT0S'
    weekOfYear: 1
  }
}

output serverId string = server.id
output databaseId string = database.id
output tdeIdentityId string = tdeIdentity.id
output migrationIdentityName string = migrationIdentity.name
output migrationIdentityClientId string = migrationIdentity.properties.clientId
