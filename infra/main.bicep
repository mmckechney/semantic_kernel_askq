targetScope = 'subscription'

@minLength(1)
@description('Primary location for all resources')
param location string
param resourceGroupName string
param functionAppName string
param keyVaultName string
param storageAccountName string
param docIntelligenceAccountName string
param aiSearchName string
param currentUserObjectId string
param openAIChatModel string
param openAIChatDeploymentName string
param openAIEmbeddingDeploymentName string
param openAIEmbeddingModel string
param openAIServiceName string
param openAILocation string



var safeStorageAccountName = toLower(replace(storageAccountName, '-', ''))
resource rg 'Microsoft.Resources/resourceGroups@2022-09-01' = {
    name: resourceGroupName
    location: location
}

module keyVault 'keyvault.bicep' = {
    name: 'keyVault'
    scope: resourceGroup(resourceGroupName)
    params: {
        location: location
        keyVaultName: keyVaultName
    }
    dependsOn: [
        rg
    ]
}

module aiSearch 'aisearch.bicep' = {
    name: 'aiSearch'
    scope: resourceGroup(resourceGroupName)
    params: {
        aiSearchName: aiSearchName
        location: location
        keyVaultName: keyVaultName
    }
    dependsOn: [
        rg
        keyVault
    ]
}
module docIntelligence 'docintelligence.bicep' = {
    name: 'docIntelligence'
    scope: resourceGroup(resourceGroupName)
    params: {
        docIntelAccountName: docIntelligenceAccountName
        location: location
        keyVaultName: keyVaultName
    }
    dependsOn: [
        rg
        keyVault
    ]
}
module storageResources 'storage.bicep' = {
    name: 'storageResources'
    scope: resourceGroup(resourceGroupName)
    params: {
        storageAccountName: safeStorageAccountName
        location: location
    }
    dependsOn: [
        rg
        keyVault
    ]
}

module functionResources 'function.bicep' = {
    name: 'azureResources'
    scope: resourceGroup(resourceGroupName)
    params: {
        functionAppName: functionAppName
        location: location
        openAIChatModel: openAIChatModel
        openAIChatDeploymentName: openAIChatDeploymentName
        openAIEmbeddingModel: openAIEmbeddingModel
        openAIEmbeddingDeploymentName: openAIEmbeddingDeploymentName
        storageAccountName: safeStorageAccountName
        extractedBlobContainerName: storageResources.outputs.extractedContainerName
        rawBlobContainerName: storageResources.outputs.rawContainerName
        keyVaultName: keyVaultName
        aiSearchEndpoint : aiSearch.outputs.aiSearchEndpoint
        docIntelligenceEndpoint : docIntelligence.outputs.docIntelEndpoint
    }
    dependsOn: [
        rg
    ]
}

module roleAssignments 'roleassignments.bicep' = {
    name: 'roleAssignments'
    scope: resourceGroup(resourceGroupName)
    params: {
  
        cogSvcsPrincipalId: docIntelligence.outputs.docIntelPrincipalId
        functionPrincipalId: functionResources.outputs.functionAppId
        currentUserObjectId : currentUserObjectId

    }
    dependsOn: [
        rg
    ]
}

module openAI 'azureopenai.bicep' = {
    name: 'azureOpenAI'
    scope: resourceGroup(resourceGroupName)
   params: {
    location: openAILocation
    name: openAIServiceName
    chatModel: openAIChatModel
    chatDeploymentName: openAIChatDeploymentName
    embeddingModel: openAIEmbeddingModel
    embeddingDeploymentName: openAIEmbeddingDeploymentName
    keyVaultName: keyVaultName
   }
    dependsOn: [
        rg
        keyVault
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
}

resource user_openai_user_role 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
    name: guid(currentUserObjectId, openAiUserRole, subscription().id)
    properties: {
        roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', openAiUserRole)
        principalId: currentUserObjectId
    }
}

output docIntelEndpoint string = docIntelligence.outputs.docIntelEndpoint
output extractedContainerName string = storageResources.outputs.extractedContainerName
output rawContainerName string = storageResources.outputs.rawContainerName
output aiSearchEndpoint string = aiSearch.outputs.aiSearchEndpoint

