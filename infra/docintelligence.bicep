param docIntelAccountName string 
param keyVaultName string
param location string = resourceGroup().location

resource docIntelAccount 'Microsoft.CognitiveServices/accounts@2021-04-30' = {
  name: docIntelAccountName
  location: location
  kind: 'CognitiveServices'
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: 'S0'
  }
  properties:{
    
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' existing = {
  name: keyVaultName
}

resource adminKey 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: 'DocumentIntelligenceSubscriptionKey'
  properties: {
    value:  docIntelAccount.listKeys().key1
  }
}

resource endPoint 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: 'DocumentIntelligenceEndpoint'
  properties: {
    value:  docIntelAccount.properties.endpoint
  }
}


output docIntelPrincipalId string = docIntelAccount.identity.principalId
output docIntelEndpoint string = docIntelAccount.properties.endpoint

