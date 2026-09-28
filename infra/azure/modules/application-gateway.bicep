// Spec 016 T02 (FR-02) - the only application ingress.
//
// Application Gateway WAF v2 in Canada Central, zone redundant, never fewer
// than two instances. Exactly one frontend port (443) and one HTTPS listener;
// there is no port 80, no redirect listener and no private frontend. TLS
// terminates here (the approved Q-A1 transient in-memory exception) and is
// re-encrypted: the only backend setting is HTTPS on 443 to the private Portal,
// presenting the preserved public host name so the backend certificate's
// host name is validated against a well-known CA chain. There is no plaintext
// backend leg (AC-023, AC-154).
//
// WAF runs in Prevention mode with the GA Microsoft default rule set. Request
// body inspection stays on; the file-upload limit is a bounded parameter so the
// approved Spec 014 upload limit is preserved rather than silently truncated.
// No diagnostic setting is created: no access, performance or firewall log -
// all of which can carry request URIs or matched request content - is enabled
// here, and no body/packet capture exists.

@description('Primary region. Only Canada Central is approved.')
@allowed([
  'canadacentral'
])
param location string

@description('Resource name prefix.')
@minLength(3)
@maxLength(16)
param namePrefix string

@description('Application Gateway subnet.')
param gatewaySubnetId string

@description('User-assigned identity the gateway uses to read its frontend certificate.')
@minLength(1)
param gatewayIdentityResourceId string

@description('Versionless Key Vault URI of the public frontend certificate (an identifier, not key material).')
@minLength(1)
param frontendCertificateUri string

@description('The approved public host name. Preserved on the backend leg.')
@minLength(4)
@maxLength(253)
param publicHostName string

@description('The private Portal default host name (resolved to its private endpoint).')
@minLength(4)
param backendHostName string

@description('Minimum gateway instances. Never below two.')
@minValue(2)
@maxValue(10)
param minimumCapacity int

@description('Maximum gateway instances.')
@minValue(2)
@maxValue(125)
param maximumCapacity int

@description('WAF file-upload limit in megabytes; must be at least the approved intake file limit.')
@minValue(1)
@maxValue(4000)
param fileUploadLimitInMb int

var gatewayName = '${namePrefix}-agw'

resource ingressAddress 'Microsoft.Network/publicIPAddresses@2024-05-01' = {
  name: '${namePrefix}-pip-ingress'
  location: location
  sku: {
    name: 'Standard'
    tier: 'Regional'
  }
  zones: [
    '1'
    '2'
    '3'
  ]
  properties: {
    publicIPAllocationMethod: 'Static'
    publicIPAddressVersion: 'IPv4'
  }
}

resource wafPolicy 'Microsoft.Network/ApplicationGatewayWebApplicationFirewallPolicies@2024-05-01' = {
  name: '${namePrefix}-waf'
  location: location
  properties: {
    policySettings: {
      state: 'Enabled'
      mode: 'Prevention'
      requestBodyCheck: true
      requestBodyEnforcement: true
      maxRequestBodySizeInKb: 128
      fileUploadEnforcement: true
      fileUploadLimitInMb: fileUploadLimitInMb
    }
    managedRules: {
      managedRuleSets: [
        {
          ruleSetType: 'Microsoft_DefaultRuleSet'
          ruleSetVersion: '2.1'
        }
      ]
    }
  }
}

resource gateway 'Microsoft.Network/applicationGateways@2024-05-01' = {
  name: gatewayName
  location: location
  zones: [
    '1'
    '2'
    '3'
  ]
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${gatewayIdentityResourceId}': {}
    }
  }
  properties: {
    sku: {
      name: 'WAF_v2'
      tier: 'WAF_v2'
    }
    autoscaleConfiguration: {
      minCapacity: minimumCapacity
      maxCapacity: max(minimumCapacity, maximumCapacity)
    }
    enableHttp2: true
    firewallPolicy: {
      id: wafPolicy.id
    }
    sslPolicy: {
      policyType: 'Predefined'
      policyName: 'AppGwSslPolicy20220101S'
    }
    gatewayIPConfigurations: [
      {
        name: 'gateway'
        properties: {
          subnet: {
            id: gatewaySubnetId
          }
        }
      }
    ]
    frontendIPConfigurations: [
      {
        name: 'public'
        properties: {
          publicIPAddress: {
            id: ingressAddress.id
          }
        }
      }
    ]
    frontendPorts: [
      {
        name: 'https'
        properties: {
          port: 443
        }
      }
    ]
    sslCertificates: [
      {
        name: 'frontend'
        properties: {
          keyVaultSecretId: frontendCertificateUri
        }
      }
    ]
    httpListeners: [
      {
        name: 'https'
        properties: {
          protocol: 'Https'
          hostName: publicHostName
          requireServerNameIndication: true
          frontendIPConfiguration: {
            id: resourceId('Microsoft.Network/applicationGateways/frontendIPConfigurations', gatewayName, 'public')
          }
          frontendPort: {
            id: resourceId('Microsoft.Network/applicationGateways/frontendPorts', gatewayName, 'https')
          }
          sslCertificate: {
            id: resourceId('Microsoft.Network/applicationGateways/sslCertificates', gatewayName, 'frontend')
          }
        }
      }
    ]
    backendAddressPools: [
      {
        name: 'portal'
        properties: {
          backendAddresses: [
            {
              fqdn: backendHostName
            }
          ]
        }
      }
    ]
    probes: [
      {
        name: 'portal-health'
        properties: {
          protocol: 'Https'
          host: publicHostName
          path: '/health'
          interval: 30
          timeout: 30
          unhealthyThreshold: 3
          pickHostNameFromBackendHttpSettings: false
          match: {
            statusCodes: [
              '200'
            ]
          }
        }
      }
    ]
    backendHttpSettingsCollection: [
      {
        name: 'portal-https'
        properties: {
          protocol: 'Https'
          port: 443
          hostName: publicHostName
          pickHostNameFromBackendAddress: false
          cookieBasedAffinity: 'Enabled'
          affinityCookieName: 'TaxAiGatewayAffinity'
          requestTimeout: 120
          probe: {
            id: resourceId('Microsoft.Network/applicationGateways/probes', gatewayName, 'portal-health')
          }
        }
      }
    ]
    requestRoutingRules: [
      {
        name: 'portal'
        properties: {
          ruleType: 'Basic'
          priority: 100
          httpListener: {
            id: resourceId('Microsoft.Network/applicationGateways/httpListeners', gatewayName, 'https')
          }
          backendAddressPool: {
            id: resourceId('Microsoft.Network/applicationGateways/backendAddressPools', gatewayName, 'portal')
          }
          backendHttpSettings: {
            id: resourceId('Microsoft.Network/applicationGateways/backendHttpSettingsCollection', gatewayName, 'portal-https')
          }
        }
      }
    ]
  }
}

output gatewayId string = gateway.id
output ingressAddressId string = ingressAddress.id
