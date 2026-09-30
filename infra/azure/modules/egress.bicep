// Spec 016 T02 (FR-05) - deny-default regional egress.
//
// One zone-redundant Azure Firewall (Standard) in Canada Central with a policy
// that admits only the reviewed public destinations in
// ../contracts/dependency-allowlist.json, from the App Service integration
// subnet only. Azure Firewall denies everything a rule does not allow. There is
// no network rule, no wildcard FQDN, no FQDN tag, no service tag, no DNAT and no
// destination parameter. Threat intelligence denies. There is no TLS
// inspection (the tier cannot perform it), and no diagnostic setting is created:
// no firewall log category - payload-bearing or otherwise - is enabled here.

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

@description('AzureFirewallSubnet.')
param firewallSubnetId string

@description('The only source allowed through the firewall: the App Service integration subnet.')
param appIntegrationSubnetPrefix string

var allowlist = loadJsonContent('../contracts/dependency-allowlist.json')

// Spec 016 T06: the same deny-default egress serves the manually activated
// Canada East recovery host with the identical allowlist. Three zones wherever
// the region offers them: Canada Central (Canada East offers none).
var zones = location == 'canadacentral' ? [
  '1'
  '2'
  '3'
] : []

resource egressAddress 'Microsoft.Network/publicIPAddresses@2024-05-01' = {
  name: '${namePrefix}-pip-egress'
  location: location
  sku: {
    name: 'Standard'
    tier: 'Regional'
  }
  zones: zones
  properties: {
    publicIPAllocationMethod: 'Static'
    publicIPAddressVersion: 'IPv4'
  }
}

resource egressPolicy 'Microsoft.Network/firewallPolicies@2024-05-01' = {
  name: '${namePrefix}-fwp-egress'
  location: location
  properties: {
    sku: {
      tier: 'Standard'
    }
    threatIntelMode: 'Deny'
  }
}

resource approvedDestinations 'Microsoft.Network/firewallPolicies/ruleCollectionGroups@2024-05-01' = {
  parent: egressPolicy
  name: 'approved-destinations'
  properties: {
    priority: 100
    ruleCollections: [
      {
        ruleCollectionType: 'FirewallPolicyFilterRuleCollection'
        name: 'allow-reviewed-destinations'
        priority: 100
        action: {
          type: 'Allow'
        }
        rules: [for destination in allowlist.publicDestinations: {
          ruleType: 'ApplicationRule'
          name: '${destination.category}-${replace(destination.fqdn, '.', '-')}'
          sourceAddresses: [
            appIntegrationSubnetPrefix
          ]
          protocols: [
            {
              protocolType: destination.protocol
              port: destination.port
            }
          ]
          targetFqdns: [
            destination.fqdn
          ]
          terminateTLS: false
        }]
      }
    ]
  }
}

resource firewall 'Microsoft.Network/azureFirewalls@2024-05-01' = {
  name: '${namePrefix}-fw-egress'
  location: location
  zones: zones
  dependsOn: [
    approvedDestinations
  ]
  properties: {
    sku: {
      name: 'AZFW_VNet'
      tier: 'Standard'
    }
    firewallPolicy: {
      id: egressPolicy.id
    }
    ipConfigurations: [
      {
        name: 'egress'
        properties: {
          subnet: {
            id: firewallSubnetId
          }
          publicIPAddress: {
            id: egressAddress.id
          }
        }
      }
    ]
  }
}

output firewallPrivateAddress string = firewall.properties.ipConfigurations[0].properties.privateIPAddress
