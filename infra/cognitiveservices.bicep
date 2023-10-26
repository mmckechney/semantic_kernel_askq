param cognitiveServicesAccountName string 
param location string = resourceGroup().location

resource cognitiveServicesAccount 'Microsoft.CognitiveServices/accounts@2021-04-30' = {
  name: cognitiveServicesAccountName
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

output cogsvcsPrincipalId string = cognitiveServicesAccount.identity.principalId
