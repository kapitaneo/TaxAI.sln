// Spec 016 T02 - EXAMPLE passive Canada East recovery network foundation.
//
// NON-DEPLOYABLE example addressing. The recovery profile deploys only the
// virtual network, subnets, NSGs, a route table that drops the default route,
// and private DNS zones. It supplies no primary topology: no gateway, no
// application, no firewall, no private endpoint, no active traffic and never an
// alternate Document Intelligence resource. Recovery activation is a separate,
// manual, validated operation owned by the recovery slice.
//
// Spec 016 T06 recovery configuration. The Canada East data-recovery resources
// are deployed by the PRIMARY profile before any disaster, never by this one:
// the authoritative Canada East control-ledger account (control-storage.bicep)
// and the SQL geo-secondary behind a Manual failover group (recovery.bicep).
// This profile therefore still takes no primary inputs and stays passive.
//
// Activating recovered application service is manual and follows
// docs/operations/canadian-recovery.md: controlled SQL failover, customer-
// managed failover of the source and key-ring accounts, then a host started
// with the separately signed CanadaEastRecovery runtime profile
// (TaxAi:Production:Recovery:Profile), whose admission gate and recovery
// orchestrator keep it quarantined until reconciliation completes. The Canada
// East recovery host (App Service, WAF and egress in this region) is deployed
// only when an operator supplies the sealed `activation` input from the real,
// uncommitted release inventory (docs/operations/canadian-recovery.md). This
// example never supplies it and stays passive, and the Canada Central Document
// Intelligence resource never fails over.

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
