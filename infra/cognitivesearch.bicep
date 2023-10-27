param cognitiveSearchName string
param location string = resourceGroup().location



resource cognitiveSearchInstance 'Microsoft.Search/searchServices@2022-09-01' = {
  name: cognitiveSearchName
  location: location
  sku: {
    name: 'basic'
  }
}
