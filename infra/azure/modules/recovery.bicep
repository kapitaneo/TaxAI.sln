// Spec 016 T06 (FR-11) - passive Canada East data recovery for Azure SQL.
//
// Deployed by the primary profile, before any disaster, so recovery never
// depends on creating resources during an outage:
//
//   * a Canada East logical server with the same posture as the primary:
//     Microsoft Entra-only authentication with the dedicated migration
//     identity as administrator, public network access disabled, outbound
//     network access restricted, TLS 1.2 or later, and transparent data
//     encryption by the same customer-managed TDE protector in the Canadian
//     Key Vault through the same dedicated TDE identity;
//   * a failover group on the primary server whose read-write failover policy
//     is Manual: cross-region activation is a controlled operator action
//     (AC-109), never automatic. Geo-replication is asynchronous, so the drill
//     measures the observed structured RPO against the 15-minute target
//     (AC-108) rather than assuming it;
//   * the geo-secondary of the application database, created by the failover
//     group with the same geo-zone backup storage and the same 35-day retention.
//
// It creates no application hosting, gateway, private endpoint in Canada East
// or Document Intelligence resource. Canada East application-hosting
// activation is a recorded pending plan-amendment item (evidence/T06.md);
// Document Intelligence never fails over.

@description('Recovery-only region. Only Canada East is approved.')
@allowed([
  'canadaeast'
])
param location string

@description('The primary SQL logical server name.')
@minLength(1)
@maxLength(63)
param primaryServerName string

@description('The Canada East SQL logical server name (the recovery profile\'s Sql:Endpoint).')
@minLength(1)
@maxLength(63)
param secondaryServerName string

@description('The failover group name.')
@minLength(1)
@maxLength(63)
param failoverGroupName string

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

@description('The dedicated user-assigned TDE identity created by sql.bicep.')
@minLength(1)
param tdeIdentityId string

@description('The dedicated migration identity name (the Entra administrator).')
@minLength(1)
param migrationIdentityName string

@description('The dedicated migration identity client ID.')
@minLength(36)
@maxLength(36)
param migrationIdentityClientId string

var tdeKeyVersion = last(split(tdeKeyUriWithVersion, '/'))

resource primary 'Microsoft.Sql/servers@2023-08-01' existing = {
  name: primaryServerName
}

resource primaryDatabase 'Microsoft.Sql/servers/databases@2023-08-01' existing = {
  parent: primary
  name: sqlDatabaseName
}

resource secondary 'Microsoft.Sql/servers@2023-08-01' = {
  name: secondaryServerName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${tdeIdentityId}': {}
    }
  }
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    restrictOutboundNetworkAccess: 'Enabled'
    primaryUserAssignedIdentityId: tdeIdentityId
    keyId: tdeKeyUriWithVersion
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      principalType: 'Application'
      login: migrationIdentityName
      sid: migrationIdentityClientId
      tenantId: tenantId
    }
  }
}

resource secondaryKey 'Microsoft.Sql/servers/keys@2023-08-01' = {
  parent: secondary
  name: '${keyVaultName}_${tdeKeyName}_${tdeKeyVersion}'
  properties: {
    serverKeyType: 'AzureKeyVault'
    uri: tdeKeyUriWithVersion
  }
}

resource secondaryProtector 'Microsoft.Sql/servers/encryptionProtector@2023-08-01' = {
  parent: secondary
  name: 'current'
  properties: {
    serverKeyType: 'AzureKeyVault'
    serverKeyName: secondaryKey.name
    autoRotationEnabled: true
  }
}

// Manual, operator-initiated failover only. No read-only listener traffic and
// no automatic grace period: a regional outage never moves traffic by itself.
resource failoverGroup 'Microsoft.Sql/servers/failoverGroups@2023-08-01' = {
  parent: primary
  name: failoverGroupName
  properties: {
    readWriteEndpoint: {
      failoverPolicy: 'Manual'
    }
    readOnlyEndpoint: {
      failoverPolicy: 'Disabled'
    }
    partnerServers: [
      {
        id: secondary.id
      }
    ]
    databases: [
      primaryDatabase.id
    ]
  }
  dependsOn: [
    secondaryProtector
  ]
}

output secondaryServerId string = secondary.id
output failoverGroupId string = failoverGroup.id
