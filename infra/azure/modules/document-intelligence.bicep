// Spec 016 T05. One regional processor; this template is not processor assurance.
targetScope = 'resourceGroup'

@allowed(['canadacentral'])
param location string
@minLength(2)
@maxLength(64)
param accountName string
param applicationPrincipalId string

resource account 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: accountName
  location: location
  kind: 'FormRecognizer'
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: accountName
    disableLocalAuth: true
    publicNetworkAccess: 'Disabled'
    restrictOutboundNetworkAccess: true
    allowedFqdnList: []
    networkAcls: {
      defaultAction: 'Deny'
      ipRules: []
      virtualNetworkRules: []
    }
  }
}

// No built-in Cognitive Services User: it also grants listKeys and support actions.
// The result subresource includes deletion and retrieval, but not model management.
resource runtimeRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: guid(resourceGroup().id, accountName, 'document-analysis-runtime')
  properties: {
    roleName: '${accountName}-analysis-runtime'
    type: 'CustomRole'
    assignableScopes: [resourceGroup().id]
    permissions: [{
      actions: []
      notActions: []
      dataActions: [
        'Microsoft.CognitiveServices/accounts/FormRecognizer/documentmodels:analyze/action'
        'Microsoft.CognitiveServices/accounts/FormRecognizer/documentmodels/analyzeresults/*'
        'Microsoft.CognitiveServices/accounts/FormRecognizer/info/read'
      ]
      notDataActions: []
    }]
  }
}
resource applicationRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(account.id, applicationPrincipalId, runtimeRole.id)
  scope: account
  properties: {
    roleDefinitionId: runtimeRole.id
    principalId: applicationPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output resourceId string = account.id
