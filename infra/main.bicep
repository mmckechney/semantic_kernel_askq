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
param openAiEndpoint string
param openAiKey string
param openAIChatModel string = 'gpt-4'
param openAIEmbeddingModel string = 'text-embedding-ada-002'


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
        openAiEndpoint : openAiEndpoint
        openAiKey: openAiKey

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
        keyVaultName: keyVaultName
        location: location
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
        storageAccountName: storageAccountName
        keyVaultName: keyVaultName
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
        openAIEmbeddingModel: openAIEmbeddingModel
        storageAccountName: storageAccountName
        extractedBlobContainerName: storageResources.outputs.extractedContainerName
        rawBlobContainerName: storageResources.outputs.rawContainerName
        keyVaultName: keyVaultName
    }
    dependsOn: [
        rg
        storageResources
    ]
}

module roleAssignments 'roleassignments.bicep' = {
    name: 'roleAssignments'
    scope: resourceGroup(resourceGroupName)
    params: {
        functionAppName: functionAppName
        cognitiveServicesAccountName: docIntelligenceAccountName
        cogSvcsPrincipalId: docIntelligence.outputs.docIntelPrincipalId
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

output docIntelEndpoint string = docIntelligence.outputs.docIntelEndpoint
