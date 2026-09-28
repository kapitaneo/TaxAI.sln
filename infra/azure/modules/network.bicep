// Spec 016 T02 (FR-02, FR-05) - the regional virtual network.
//
// Four distinct subnets, because App Service ingress and egress are distinct
// controls and Azure requires distinct supported subnets for each:
//   gateway          Application Gateway WAF v2 only (public 443 + platform ports)
//   appIntegration   App Service VNet integration (outbound only), delegated
//   privateEndpoints private endpoints for the Portal and its four dependencies
//   AzureFirewallSubnet  the deny-default regional egress firewall (primary only)
//
// Outbound from the application is deny by default. The integration subnet's
// route table sends every non-VNet destination to the firewall (primary) or
// drops it (recovery, where no firewall exists until manual recovery
// activation). Its NSG admits only the private-endpoint subnet and firewall-
// routed HTTP(S); everything else is denied. No security value is a parameter.

@description('Deployment region. Canada Central (primary) or Canada East (passive recovery foundation).')
@allowed([
  'canadacentral'
  'canadaeast'
])
param location string

@description('Resource name prefix.')
@minLength(3)
@maxLength(16)
param namePrefix string

@description('Virtual network address space.')
param addressPrefix string

@description('Application Gateway subnet.')
param gatewaySubnetPrefix string

@description('App Service outbound VNet integration subnet.')
param appIntegrationSubnetPrefix string

@description('Private endpoint subnet.')
param privateEndpointSubnetPrefix string

@description('AzureFirewallSubnet (at least /26).')
param firewallSubnetPrefix string

@description('Firewall: route the default route to the regional firewall. Blackhole: drop it (passive recovery foundation).')
@allowed([
  'Firewall'
  'Blackhole'
])
param egressMode string

// Azure Firewall always takes the first usable address of AzureFirewallSubnet.
// The live route probe verifies that the observed firewall address equals it.
var firewallPrivateAddress = cidrHost(firewallSubnetPrefix, 3)

resource gatewayNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: '${namePrefix}-nsg-gateway'
  location: location
  properties: {
    securityRules: [
      {
        name: 'AllowHttpsFromInternet'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: 'Internet'
          sourcePortRange: '*'
          destinationAddressPrefix: gatewaySubnetPrefix
          destinationPortRange: '443'
        }
      }
      {
        name: 'AllowGatewayManager'
        properties: {
          priority: 110
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: 'GatewayManager'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '65200-65535'
        }
      }
      {
        name: 'AllowAzureLoadBalancer'
        properties: {
          priority: 120
          direction: 'Inbound'
          access: 'Allow'
          protocol: '*'
          sourceAddressPrefix: 'AzureLoadBalancer'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '*'
        }
      }
      {
        name: 'DenyAllOtherInbound'
        properties: {
          priority: 4000
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '*'
        }
      }
    ]
  }
}

resource appIntegrationNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: '${namePrefix}-nsg-app-integration'
  location: location
  properties: {
    securityRules: [
      {
        name: 'AllowPrivateDependencies'
        properties: {
          priority: 100
          direction: 'Outbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: appIntegrationSubnetPrefix
          sourcePortRange: '*'
          destinationAddressPrefix: privateEndpointSubnetPrefix
          destinationPortRanges: [
            '443'
            '1433'
          ]
        }
      }
      {
        // Only reachable through the default route to the firewall, whose
        // policy is the allowlist. The NSG sees the original destination.
        name: 'AllowFirewallRoutedWeb'
        properties: {
          priority: 110
          direction: 'Outbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: appIntegrationSubnetPrefix
          sourcePortRange: '*'
          destinationAddressPrefix: 'Internet'
          destinationPortRanges: [
            '80'
            '443'
          ]
        }
      }
      {
        name: 'DenyAllOtherOutbound'
        properties: {
          priority: 4000
          direction: 'Outbound'
          access: 'Deny'
          protocol: '*'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '*'
        }
      }
      {
        name: 'DenyAllInbound'
        properties: {
          priority: 4000
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '*'
        }
      }
    ]
  }
}

resource privateEndpointNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: '${namePrefix}-nsg-private-endpoints'
  location: location
  properties: {
    securityRules: [
      {
        name: 'AllowApplicationToDependencies'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: appIntegrationSubnetPrefix
          sourcePortRange: '*'
          destinationAddressPrefix: privateEndpointSubnetPrefix
          destinationPortRanges: [
            '443'
            '1433'
          ]
        }
      }
      {
        name: 'AllowGatewayToPortal'
        properties: {
          priority: 110
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: gatewaySubnetPrefix
          sourcePortRange: '*'
          destinationAddressPrefix: privateEndpointSubnetPrefix
          destinationPortRange: '443'
        }
      }
      {
        name: 'DenyAllOtherInbound'
        properties: {
          priority: 4000
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '*'
        }
      }
    ]
  }
}

resource egressRoutes 'Microsoft.Network/routeTables@2024-05-01' = {
  name: '${namePrefix}-rt-app-egress'
  location: location
  properties: {
    disableBgpRoutePropagation: true
    routes: [
      egressMode == 'Firewall'
        ? {
            name: 'DefaultToFirewall'
            properties: {
              addressPrefix: '0.0.0.0/0'
              nextHopType: 'VirtualAppliance'
              nextHopIpAddress: firewallPrivateAddress
            }
          }
        : {
            name: 'DefaultDropped'
            properties: {
              addressPrefix: '0.0.0.0/0'
              nextHopType: 'None'
            }
          }
    ]
  }
}

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${namePrefix}-vnet'
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        addressPrefix
      ]
    }
    subnets: [
      {
        name: 'snet-gateway'
        properties: {
          addressPrefix: gatewaySubnetPrefix
          networkSecurityGroup: {
            id: gatewayNsg.id
          }
          defaultOutboundAccess: false
        }
      }
      {
        name: 'snet-app-integration'
        properties: {
          addressPrefix: appIntegrationSubnetPrefix
          networkSecurityGroup: {
            id: appIntegrationNsg.id
          }
          routeTable: {
            id: egressRoutes.id
          }
          defaultOutboundAccess: false
          delegations: [
            {
              name: 'app-service'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
      {
        name: 'snet-private-endpoints'
        properties: {
          addressPrefix: privateEndpointSubnetPrefix
          networkSecurityGroup: {
            id: privateEndpointNsg.id
          }
          privateEndpointNetworkPolicies: 'Enabled'
          defaultOutboundAccess: false
        }
      }
      {
        name: 'AzureFirewallSubnet'
        properties: {
          addressPrefix: firewallSubnetPrefix
        }
      }
    ]
  }
}

output virtualNetworkId string = virtualNetwork.id
output gatewaySubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', virtualNetwork.name, 'snet-gateway')
output appIntegrationSubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', virtualNetwork.name, 'snet-app-integration')
output privateEndpointSubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', virtualNetwork.name, 'snet-private-endpoints')
output firewallSubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', virtualNetwork.name, 'AzureFirewallSubnet')
output firewallPrivateAddress string = firewallPrivateAddress
