targetScope = 'subscription'

@minLength(1)
@description('Primary location for all resources')
param location string
param resourceGroupName string
param functionAppName string
param storageAccountName string
param cognitiveServicesAccountName string
param openAiEndpoint string
param openAiKey string
param openAIChatModel string = 'gpt-4'
param openAIEmbeddingModel string = 'text-embedding-ada-002'


resource rg 'Microsoft.Resources/resourceGroups@2022-09-01' = {
  name: resourceGroupName
  location: location
}

module cognitiveServices 'cognitiveservices.bicep' = {
  name: 'cognitiveServices'
  scope: resourceGroup(resourceGroupName)
  params: {
    cognitiveServicesAccountName: cognitiveServicesAccountName
    location: location
  }
  dependsOn: [
    rg
  ]
}
module storageResources 'storage.bicep' = {
  name: 'storageResources'
  scope: resourceGroup(resourceGroupName)
  params: {
    storageAccountName: storageAccountName
    location: location
  }
  dependsOn: [
    rg
  ]
}

module functionResources 'function.bicep' = {
  name: 'azureResources'
  scope: resourceGroup(resourceGroupName)
  params: {
    cognitiveServicesAccountName: cognitiveServicesAccountName
    functionAppName: functionAppName
    location: location
    openAIChatModel: openAIChatModel
    openAIEmbeddingModel: openAIEmbeddingModel
    openAiEndpoint: openAiEndpoint
    openAiKey: openAiKey
    storageAccountName: storageAccountName
    extractedBlobContainerName: storageResources.outputs.extractedContainerName
    rawBlobContainerName: storageResources.outputs.rawContainerName
  }
  dependsOn: [
    rg
    storageResources
    cognitiveServices
  ]
}

module roleAssignments 'roleassignments.bicep' = {
  name: 'roleAssignments'
  scope: resourceGroup(resourceGroupName)
  params: {
    functionAppName: functionAppName
    cognitiveServicesAccountName: cognitiveServicesAccountName
    cogSvcsPrincipalId: cognitiveServices.outputs.cogsvcsPrincipalId
    extractedBlobContainerName: storageResources.outputs.extractedContainerName
    rawBlobContainerName: storageResources.outputs.rawContainerName
    functionPrincipalId: functionResources.outputs.functionAppId
    storageAccountName: storageAccountName
  }
  dependsOn: [
    rg
    functionResources
    storageResources
  ]
}



var openAiUserRole = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
var functionAppId = functionResources.outputs.functionAppId
resource func_openai_user_role 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(functionAppName, openAiUserRole, subscription().id)
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', openAiUserRole)
    principalId: functionAppId
 }
  dependsOn: [
    functionResources
  ]
 }
 

