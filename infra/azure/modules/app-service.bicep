// Spec 016 T02 (FR-02, FR-05) - the private TaxAI Portal on App Service.
//
// Premium v3 P1v3 in Canada Central, zone redundant, never fewer than two
// running workers (the plan capacity and every autoscale bound are at least
// two). The site and its SCM endpoint have public network access disabled; the
// only way in is the Portal's private endpoint, which is the Application
// Gateway's only backend. HTTPS only, TLS 1.2 minimum for site and SCM, no FTP,
// no basic publishing credentials.
//
// Outbound uses VNet integration with application AND configuration traffic
// routed into the virtual network (route-all, content share, image pull and
// backup/restore), where the integration subnet's route table and NSG enforce
// the deny-default egress. WebSockets are on for the one approved Interactive
// Server questionnaire island; nothing here widens interactivity.
//
// Spec 016 T06: the same hardened site also serves as the manually activated
// recovery host (AC-107, DEP-18). Zone redundancy is an explicit capability
// input: main.bicep states it per deployment role (the primary deployment is
// zonal; the current recovery region offers no availability zones). Every
// other control is identical for both roles.
//
// No diagnostic setting is created. The platform's own forwarded-header switch
// is off: the Portal honours exactly one trusted forwarded hop itself, and
// refuses to start in Production if the platform switch is on.

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

@description('Globally unique App Service site name.')
@minLength(2)
@maxLength(60)
param siteName string

@description('Whether the deployment region offers availability zones. An explicit capability supplied by main.bicep per deployment role; it is never inferred from a region name.')
param zoneRedundant bool

@description('Running workers the plan starts with. Never below two.')
@minValue(2)
@maxValue(10)
param minimumInstanceCount int

@description('Autoscale ceiling. At least the minimum.')
@minValue(2)
@maxValue(10)
param maximumInstanceCount int

@description('App Service outbound VNet integration subnet.')
param appIntegrationSubnetId string

@description('The Portal user-assigned managed identity resource ID.')
@minLength(1)
param managedIdentityResourceId string

@description('The approved public host name the gateway preserves on the backend leg.')
@minLength(4)
@maxLength(253)
param publicHostName string

@description('Thumbprint of the site certificate bound to the public host name for backend TLS validation.')
@minLength(40)
@maxLength(40)
param backendCertificateThumbprint string

@description('Exact runtime application settings emitted by runtime-configuration.bicep.')
param runtimeSettings object

var platformSettings = {
  ASPNETCORE_FORWARDEDHEADERS_ENABLED: 'false'
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${namePrefix}-plan'
  location: location
  kind: 'linux'
  sku: {
    name: 'P1v3'
    tier: 'PremiumV3'
    capacity: minimumInstanceCount
  }
  properties: {
    reserved: true
    zoneRedundant: zoneRedundant
    perSiteScaling: false
    elasticScaleEnabled: false
  }
}

resource site 'Microsoft.Web/sites@2024-04-01' = {
  name: siteName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityResourceId}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    publicNetworkAccess: 'Disabled'
    clientAffinityEnabled: true
    clientCertEnabled: false
    virtualNetworkSubnetId: appIntegrationSubnetId
    vnetRouteAllEnabled: true
    vnetContentShareEnabled: true
    vnetImagePullEnabled: true
    vnetBackupRestoreEnabled: true
    keyVaultReferenceIdentity: managedIdentityResourceId
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      http20Enabled: true
      webSocketsEnabled: true
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      ftpsState: 'Disabled'
      remoteDebuggingEnabled: false
      detailedErrorLoggingEnabled: false
      httpLoggingEnabled: false
      requestTracingEnabled: false
      ipSecurityRestrictionsDefaultAction: 'Deny'
      scmIpSecurityRestrictionsDefaultAction: 'Deny'
      scmIpSecurityRestrictionsUseMain: false
      minimumElasticInstanceCount: minimumInstanceCount
      appSettings: [for setting in items(union(runtimeSettings, platformSettings)): {
        name: setting.key
        value: setting.value
      }]
    }
  }
}

resource ftpCredentials 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: site
  name: 'ftp'
  properties: {
    allow: false
  }
}

resource scmCredentials 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: site
  name: 'scm'
  properties: {
    allow: false
  }
}

resource publicHost 'Microsoft.Web/sites/hostNameBindings@2024-04-01' = {
  parent: site
  name: publicHostName
  properties: {
    siteName: site.name
    hostNameType: 'Verified'
    sslState: 'SniEnabled'
    thumbprint: backendCertificateThumbprint
  }
}

resource autoscale 'Microsoft.Insights/autoscalesettings@2022-10-01' = {
  name: '${namePrefix}-autoscale'
  location: location
  properties: {
    enabled: true
    targetResourceUri: plan.id
    profiles: [
      {
        name: 'never-below-two'
        capacity: {
          minimum: string(minimumInstanceCount)
          default: string(minimumInstanceCount)
          maximum: string(max(minimumInstanceCount, maximumInstanceCount))
        }
        rules: [
          {
            metricTrigger: {
              metricName: 'CpuPercentage'
              metricResourceUri: plan.id
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT10M'
              timeAggregation: 'Average'
              operator: 'GreaterThan'
              threshold: 70
            }
            scaleAction: {
              direction: 'Increase'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT10M'
            }
          }
          {
            metricTrigger: {
              metricName: 'CpuPercentage'
              metricResourceUri: plan.id
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT20M'
              timeAggregation: 'Average'
              operator: 'LessThan'
              threshold: 30
            }
            scaleAction: {
              direction: 'Decrease'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT20M'
            }
          }
        ]
      }
    ]
  }
}

output siteId string = site.id
output defaultHostName string = site.properties.defaultHostName
output planId string = plan.id
