param cognitiveSearchName string
param keyVaultName string
param location string = resourceGroup().location



resource cognitiveSearchInstance 'Microsoft.Search/searchServices@2022-09-01' = {
  name: cognitiveSearchName
  location: location
  sku: {
    name: 'basic'
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' existing = {
  name: keyVaultName
}

resource adminKey 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: 'CognitiveSearchAdminKey'
  properties: {
    value:  cognitiveSearchInstance.listAdminKeys().primaryKey
  }
}

resource endPoint 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: 'CognitiveSearchEndpoint'
  properties: {
    value: 'https://${cognitiveSearchName}.search.windows.net'
  }
}