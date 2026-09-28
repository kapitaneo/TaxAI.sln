// Spec 016 T02 - EXAMPLE passive Canada East recovery network foundation.
//
// NON-DEPLOYABLE example addressing. The recovery profile deploys only the
// virtual network, subnets, NSGs, a route table that drops the default route,
// and private DNS zones. It supplies no primary topology: no gateway, no
// application, no firewall, no private endpoint, no active traffic and never an
// alternate Document Intelligence resource. Recovery activation is a separate,
// manual, validated operation owned by the recovery slice.

using '../main.bicep'

param profile = 'recovery'
param primaryRegion = 'canadacentral'
param recoveryRegion = 'canadaeast'
param namePrefix = 'taxai-dr'

param network = {
  addressPrefix: '10.41.0.0/22'
  gatewaySubnetPrefix: '10.41.0.0/24'
  appIntegrationSubnetPrefix: '10.41.1.0/26'
  privateEndpointSubnetPrefix: '10.41.1.64/26'
  firewallSubnetPrefix: '10.41.1.128/26'
}
