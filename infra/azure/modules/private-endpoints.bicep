// Spec 016 T02 (FR-05) - private endpoint bindings.
//
// Exactly five bindings: the Portal's own inbound endpoint (the gateway's only
// backend) and the four approved dependencies - source Blob, Azure SQL, Key
// Vault and the Canada Central Document Intelligence resource. Resource-owning
// later slices supply concrete resource IDs through these parameters; they do
// not reimplement endpoints. Every binding is required: a missing or empty
// resource ID fails validation, it never deploys a partial topology.
//
// Spec 016 T06: the same module binds the manually activated Canada East
// recovery host in its own recovery virtual network: its own Portal site, the
// same source account, the Canada East SQL server, the same Key Vault and the
// same Canada Central Document Intelligence resource (extraction there works
// only while that resource is reachable; otherwise the fixed 503 applies), plus
// the three control accounts it needs to start - the Data Protection key ring
// and both control-ledger copies. The primary host passes no control account:
// their Canada Central endpoints are created by the modules that own them.
//
// A private endpoint does not by itself disable a public endpoint. Public
// network access is disabled on each resource by its owning slice and verified
// by the live network probe (DEP-07).

@description('Canada Central (primary) or Canada East (manually activated recovery host only).')
@allowed([
  'canadacentral'
  'canadaeast'
])
param location string

@description('Resource name prefix.')
@minLength(3)
@maxLength(16)
param namePrefix string

@description('Private endpoint subnet.')
param subnetId string

@description('Private DNS zone IDs keyed by binding name.')
param zoneIds object

@description('The Portal App Service resource ID.')
@minLength(1)
param portalResourceId string

@description('The source Blob storage account resource ID.')
@minLength(1)
param sourceStorageResourceId string

@description('The Azure SQL logical server resource ID.')
@minLength(1)
param sqlServerResourceId string

@description('The Key Vault resource ID.')
@minLength(1)
param keyVaultResourceId string

@description('The Canada Central Document Intelligence resource ID.')
@minLength(1)
param documentIntelligenceResourceId string

@description('Recovery host only: the key-ring and both control-ledger storage account IDs. Empty for the primary host.')
@maxLength(3)
param controlAccountResourceIds string[]

var requiredBindings = [
  {
    name: 'Portal'
    groupId: 'sites'
    zone: 'Portal'
    resourceId: portalResourceId
  }
  {
    name: 'SourceStorage'
    groupId: 'blob'
    zone: 'SourceStorage'
    resourceId: sourceStorageResourceId
  }
  {
    name: 'Sql'
    groupId: 'sqlServer'
    zone: 'Sql'
    resourceId: sqlServerResourceId
  }
  {
    name: 'KeyVault'
    groupId: 'vault'
    zone: 'KeyVault'
    resourceId: keyVaultResourceId
  }
  {
    name: 'DocumentIntelligence'
    groupId: 'account'
    zone: 'DocumentIntelligence'
    resourceId: documentIntelligenceResourceId
  }
]

var controlBindings = [for (accountId, index) in controlAccountResourceIds: {
  name: 'Control${index}'
  groupId: 'blob'
  zone: 'SourceStorage'
  resourceId: accountId
}]

var bindings = concat(requiredBindings, controlBindings)

resource endpoints 'Microsoft.Network/privateEndpoints@2024-05-01' = [for binding in bindings: {
  name: '${namePrefix}-pe-${toLower(binding.name)}'
  location: location
  properties: {
    subnet: {
      id: subnetId
    }
    privateLinkServiceConnections: [
      {
        name: toLower(binding.name)
        properties: {
          // Each value is one of the five required resource-ID parameters or a
          // recovery-host control account above.
          #disable-next-line use-resource-id-functions
          privateLinkServiceId: binding.resourceId
          groupIds: [
            binding.groupId
          ]
        }
      }
    ]
  }
}]

resource zoneGroups 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = [for (binding, index) in bindings: {
  parent: endpoints[index]
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: toLower(binding.name)
        properties: {
          privateDnsZoneId: zoneIds[binding.zone]
        }
      }
    ]
  }
}]

output endpointIds object = toObject(bindings, binding => binding.name, binding => resourceId('Microsoft.Network/privateEndpoints', '${namePrefix}-pe-${toLower(binding.name)}'))
