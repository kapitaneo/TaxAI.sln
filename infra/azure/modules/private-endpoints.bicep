// Spec 016 T02 (FR-05) - private endpoint bindings.
//
// Exactly five bindings: the Portal's own inbound endpoint (the gateway's only
// backend) and the four approved dependencies - source Blob, Azure SQL, Key
// Vault and the Canada Central Document Intelligence resource. Resource-owning
// later slices supply concrete resource IDs through these parameters; they do
// not reimplement endpoints. Every binding is required: a missing or empty
// resource ID fails validation, it never deploys a partial topology.
//
// A private endpoint does not by itself disable a public endpoint. Public
// network access is disabled on each resource by its owning slice and verified
// by the live network probe (DEP-07).

@description('Primary region. Only Canada Central is approved.')
@allowed([
  'canadacentral'
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

var bindings = [
  {
    name: 'Portal'
    groupId: 'sites'
    resourceId: portalResourceId
  }
  {
    name: 'SourceStorage'
    groupId: 'blob'
    resourceId: sourceStorageResourceId
  }
  {
    name: 'Sql'
    groupId: 'sqlServer'
    resourceId: sqlServerResourceId
  }
  {
    name: 'KeyVault'
    groupId: 'vault'
    resourceId: keyVaultResourceId
  }
  {
    name: 'DocumentIntelligence'
    groupId: 'account'
    resourceId: documentIntelligenceResourceId
  }
]

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
          // Each value is one of the five required resource-ID parameters above.
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
          privateDnsZoneId: zoneIds[binding.name]
        }
      }
    ]
  }
}]

output endpointIds object = toObject(bindings, binding => binding.name, binding => resourceId('Microsoft.Network/privateEndpoints', '${namePrefix}-pe-${toLower(binding.name)}'))
