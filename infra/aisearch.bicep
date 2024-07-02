param aiSearchName string
param keyVaultName string
param location string = resourceGroup().location



resource aiSearchInstance 'Microsoft.Search/searchServices@2023-11-01' = {
  name: aiSearchName
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
  name: 'AiSearchKey'
  properties: {
    value:  aiSearchInstance.listAdminKeys().primaryKey
  }
}

resource endPoint 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: 'AiSearchEndpoint'
  properties: {
    value: 'https://${aiSearchName}.search.windows.net'
  }
}
