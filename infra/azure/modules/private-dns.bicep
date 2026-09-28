// Spec 016 T02 (FR-05) - private DNS for the Portal and its four approved
// private dependencies.
//
// Each zone is linked to the given virtual networks without auto-registration,
// so every application worker resolves the dependency host names to the private
// endpoint addresses. The zone set is fixed; there is no zone parameter.

@description('Virtual networks whose resolvers use these zones.')
@minLength(1)
param virtualNetworkIds string[]

@description('Link name prefix.')
@minLength(3)
@maxLength(16)
param namePrefix string

var zones = {
  SourceStorage: 'privatelink.blob.${environment().suffixes.storage}'
  Sql: 'privatelink${environment().suffixes.sqlServerHostname}'
  KeyVault: 'privatelink.vaultcore.azure.net'
  DocumentIntelligence: 'privatelink.cognitiveservices.azure.com'
  Portal: 'privatelink.azurewebsites.net'
}

resource privateZones 'Microsoft.Network/privateDnsZones@2024-06-01' = [for zone in items(zones): {
  name: zone.value
  location: 'global'
}]

resource links 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = [for (link, index) in flatten(map(range(0, length(items(zones))), zoneIndex => map(range(0, length(virtualNetworkIds)), networkIndex => {
  zoneIndex: zoneIndex
  networkIndex: networkIndex
}))): {
  parent: privateZones[link.zoneIndex]
  name: '${namePrefix}-link-${link.networkIndex}'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: virtualNetworkIds[link.networkIndex]
    }
  }
}]

output zoneIds object = toObject(items(zones), zone => zone.key, zone => resourceId('Microsoft.Network/privateDnsZones', zone.value))
